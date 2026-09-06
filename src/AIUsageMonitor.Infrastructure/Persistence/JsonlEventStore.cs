using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AIUsageMonitor.Application.Orchestration;
using Microsoft.Extensions.Logging;

namespace AIUsageMonitor.Infrastructure.Persistence;

/// <summary>
/// Small Infrastructure-only seam for deterministic JSONL partition read-failure tests. The
/// Application layer never sees this abstraction or its exceptions.
/// </summary>
internal interface IJsonlPartitionReader
{
    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);
}

internal sealed class FileSystemJsonlPartitionReader : IJsonlPartitionReader
{
    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }
}

/// <summary>
/// Shared append/read mechanics for monthly JSONL streams. Readers parse one line at a time and
/// isolate malformed records so a bad optional history partition cannot block startup or other
/// months.
/// </summary>
public sealed class JsonlEventStore<TRecord>
    where TRecord : class
{
    private readonly ApplicationDataPaths _paths;
    private readonly JsonFileStore _files;
    private readonly ILogger<JsonlEventStore<TRecord>> _logger;
    private readonly IJsonlPartitionReader _partitionReader;

    public JsonlEventStore(
        ApplicationDataPaths paths,
        JsonFileStore files,
        ILogger<JsonlEventStore<TRecord>> logger)
        : this(paths, files, logger, new FileSystemJsonlPartitionReader())
    {
    }

    internal JsonlEventStore(
        ApplicationDataPaths paths,
        JsonFileStore files,
        ILogger<JsonlEventStore<TRecord>> logger,
        IJsonlPartitionReader partitionReader)
    {
        _paths = paths;
        _files = files;
        _logger = logger;
        _partitionReader = partitionReader ?? throw new ArgumentNullException(nameof(partitionReader));
    }

    public async Task AppendAsync(
        string directory,
        DateTimeOffset timestamp,
        TRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await _paths.EnsureDirectoriesAsync(cancellationToken).ConfigureAwait(false);

        var path = _paths.GetMonthlyPartition(directory, timestamp);
        await _files.ExecuteExclusiveAsync(path, async () =>
        {
            Directory.CreateDirectory(directory);
            await using var stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 16 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            stream.Position = stream.Length;
            if (HasUnterminatedTail(stream))
            {
                _logger.LogWarning(
                    "Isolating an unterminated JSONL tail before appending to {FilePath}",
                    path);
                await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            }

            var line = JsonSerializer.Serialize(record, JsonFileStore.JsonlSerializerOptions);
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(line), cancellationToken)
                .ConfigureAwait(false);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResult?> ReadLatestAsync<TResult>(
        string directory,
        Func<TRecord, DateTimeOffset> timestampSelector,
        Func<TRecord, TResult?> map,
        Func<TResult, DateTimeOffset> mappedTimestampSelector,
        CancellationToken cancellationToken = default)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(timestampSelector);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(mappedTimestampSelector);

        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var path in EnumerateMonthlyPathsDescending(directory))
        {
            var records = await ReadFileAsync(path, timestampSelector, cancellationToken)
                .ConfigureAwait(false);
            TResult? latest = null;
            DateTimeOffset latestTimestamp = default;

            foreach (var record in records)
            {
                var mapped = map(record);
                if (mapped is null)
                {
                    continue;
                }

                var candidateTimestamp = mappedTimestampSelector(mapped);
                if (latest is null || candidateTimestamp >= latestTimestamp)
                {
                    latest = mapped;
                    latestTimestamp = candidateTimestamp;
                }
            }

            // JSONL partitions are named by the UTC month of the captured event. Once a valid
            // matching value exists in the newest partition, older partitions cannot supersede it.
            if (latest is not null)
            {
                return latest;
            }
        }

        return null;
    }

    public async IAsyncEnumerable<TRecord> ReadRangeAsync(
        string directory,
        DateTimeOffset from,
        DateTimeOffset to,
        Func<TRecord, DateTimeOffset> timestampSelector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (from > to || !Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var path in EnumerateMonthlyPaths(directory, from, to))
        {
            var records = await ReadFileAsync(path, timestampSelector, cancellationToken).ConfigureAwait(false);
            foreach (var record in records)
            {
                var timestamp = timestampSelector(record);
                if (timestamp >= from && timestamp <= to)
                {
                    yield return record;
                }
            }
        }
    }

    /// <summary>
    /// Reads a requested monthly range while preserving valid records and reporting whether the
    /// result is complete, degraded, or unavailable. Missing partitions are normal absence and do
    /// not create an issue.
    /// </summary>
    public async Task<HistoryReadResult<TRecord>> ReadRangeWithStatusAsync(
        string directory,
        DateTimeOffset from,
        DateTimeOffset to,
        Func<TRecord, DateTimeOffset> timestampSelector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timestampSelector);

        if (from > to)
        {
            return new HistoryReadResult<TRecord>([], HistoryReadStatus.Success);
        }

        var records = new List<TRecord>();
        var issues = new List<HistoryReadIssue>();
        var completedPartitions = 0;

        foreach (var path in EnumerateRequestedMonthlyPaths(directory, from, to))
        {
            var partition = await ReadFileWithStatusAsync(path, timestampSelector, cancellationToken)
                .ConfigureAwait(false);
            foreach (var record in partition.Records)
            {
                var timestamp = timestampSelector(record);
                if (timestamp >= from && timestamp <= to)
                {
                    records.Add(record);
                }
            }
            issues.AddRange(partition.Issues);
            if (partition.Completed)
            {
                completedPartitions++;
            }
        }

        var hasStorageFailure = issues.Any(static issue =>
            issue.Kind is HistoryReadIssueKind.PermissionFailure or HistoryReadIssueKind.IoFailure);
        var status = hasStorageFailure
            ? completedPartitions > 0 ? HistoryReadStatus.Partial : HistoryReadStatus.Unavailable
            : issues.Count == 0 ? HistoryReadStatus.Success : HistoryReadStatus.Partial;

        return new HistoryReadResult<TRecord>(records, status, issues);
    }

    public async IAsyncEnumerable<TRecord> ReadAllAsync(
        string directory,
        Func<TRecord, DateTimeOffset> timestampSelector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            var records = await ReadFileAsync(path, timestampSelector, cancellationToken).ConfigureAwait(false);
            foreach (var record in records)
            {
                yield return record;
            }
        }
    }

    /// <summary>
    /// Reads every partition while preserving valid records and reporting malformed or
    /// unavailable history. Callers provide a hard record bound so a damaged or unexpectedly
    /// large stream cannot become an unbounded read.
    /// </summary>
    public async Task<HistoryReadResult<TRecord>> ReadAllWithStatusAsync(
        string directory,
        Func<TRecord, DateTimeOffset> timestampSelector,
        int maximumRecords,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timestampSelector);
        if (maximumRecords <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        if (!Directory.Exists(directory))
            return new HistoryReadResult<TRecord>([], HistoryReadStatus.Success);

        var records = new List<TRecord>();
        var issues = new List<HistoryReadIssue>();
        var completedPartitions = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            var partition = await ReadFileWithStatusAsync(path, timestampSelector, cancellationToken).ConfigureAwait(false);
            records.AddRange(partition.Records);
            issues.AddRange(partition.Issues);
            if (partition.Completed)
                completedPartitions++;
            if (records.Count > maximumRecords)
            {
                issues.Add(CreateIssue(path, HistoryReadIssueKind.CorruptRecord, "History record capacity was exceeded."));
                break;
            }
        }

        var hasStorageFailure = issues.Any(static issue =>
            issue.Kind is HistoryReadIssueKind.PermissionFailure or HistoryReadIssueKind.IoFailure);
        var status = hasStorageFailure
            ? completedPartitions > 0 ? HistoryReadStatus.Partial : HistoryReadStatus.Unavailable
            : issues.Count == 0 ? HistoryReadStatus.Success : HistoryReadStatus.Partial;
        return new HistoryReadResult<TRecord>(records, status, issues);
    }

    private async Task<IReadOnlyList<TRecord>> ReadFileAsync(
        string path,
        Func<TRecord, DateTimeOffset> timestampSelector,
        CancellationToken cancellationToken)
    {
        var result = await ReadFileWithStatusAsync(path, timestampSelector, cancellationToken)
            .ConfigureAwait(false);
        return result.Records;
    }

    private Task<PartitionReadResult> ReadFileWithStatusAsync(
        string path,
        Func<TRecord, DateTimeOffset> timestampSelector,
        CancellationToken cancellationToken) =>
        _files.ExecuteExclusiveAsync(
            path,
            () => ReadFileCoreWithStatusAsync(path, timestampSelector, cancellationToken),
            cancellationToken);

    private static bool HasUnterminatedTail(FileStream stream)
    {
        if (stream.Length == 0)
        {
            return false;
        }

        stream.Position = stream.Length - 1;
        var lastByte = stream.ReadByte();
        stream.Position = stream.Length;
        return lastByte != '\n';
    }

    private async Task<PartitionReadResult> ReadFileCoreWithStatusAsync(
        string path,
        Func<TRecord, DateTimeOffset> timestampSelector,
        CancellationToken cancellationToken)
    {
        var records = new List<TRecord>();
        var issues = new List<HistoryReadIssue>();
        var completed = false;
        try
        {
            await using var stream = await _partitionReader
                .OpenReadAsync(path, cancellationToken)
                .ConfigureAwait(false);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            var lineNumber = 0;
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                TRecord? record;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaElement) ||
                        !schemaElement.TryGetInt32(out var schemaVersion) ||
                        schemaVersion != JsonFileStore.CurrentSchemaVersion)
                    {
                        issues.Add(CreateIssue(
                            path,
                            HistoryReadIssueKind.UnsupportedSchema,
                            "Unsupported JSONL record schema was skipped."));
                        _logger.LogWarning("Skipping unsupported JSONL record in {FilePath}", path);
                        continue;
                    }

                    record = JsonSerializer.Deserialize<TRecord>(line, JsonFileStore.SerializerOptions);
                }
                catch (JsonException exception)
                {
                    issues.Add(CreateIssue(
                        path,
                        HistoryReadIssueKind.CorruptRecord,
                        "Malformed JSONL record was skipped."));
                    _logger.LogWarning(exception, "Skipping corrupt JSONL record in {FilePath}", path);
                    continue;
                }
                catch (InvalidOperationException exception)
                {
                    issues.Add(CreateIssue(
                        path,
                        HistoryReadIssueKind.CorruptRecord,
                        "Malformed JSONL record was skipped."));
                    _logger.LogWarning(exception, "Skipping invalid JSONL record in {FilePath}", path);
                    continue;
                }

                if (record is null)
                {
                    issues.Add(CreateIssue(
                        path,
                        HistoryReadIssueKind.CorruptRecord,
                        "Empty JSONL record was skipped."));
                    _logger.LogWarning("Skipping empty JSONL record in {FilePath}", path);
                    continue;
                }

                try
                {
                    _ = timestampSelector(record);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    issues.Add(CreateIssue(
                        path,
                        HistoryReadIssueKind.CorruptRecord,
                        "Invalid JSONL record was skipped."));
                    _logger.LogWarning(exception, "Skipping invalid JSONL record in {FilePath}", path);
                    continue;
                }

                records.Add(record);
            }

            completed = true;
        }
        catch (FileNotFoundException)
        {
            // A requested month with no file is normal absence, not a read failure.
        }
        catch (DirectoryNotFoundException)
        {
            // A project/history directory that has not been created is normal absence.
        }
        catch (UnauthorizedAccessException exception)
        {
            issues.Add(CreateIssue(
                path,
                HistoryReadIssueKind.PermissionFailure,
                "The history partition could not be read because access was denied."));
            _logger.LogWarning(exception, "Permission denied while reading JSONL partition {FilePath}", path);
        }
        catch (IOException exception)
        {
            issues.Add(CreateIssue(
                path,
                HistoryReadIssueKind.IoFailure,
                "The history partition could not be read because of an I/O failure."));
            _logger.LogWarning(exception, "I/O failure while reading JSONL partition {FilePath}", path);
        }

        return new PartitionReadResult(records, issues, completed);
    }

    private static HistoryReadIssue CreateIssue(
        string path,
        HistoryReadIssueKind kind,
        string message) =>
        new(kind, Path.GetFileName(path) ?? "history", message);

    private static IEnumerable<string> EnumerateMonthlyPathsDescending(string directory) =>
        Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
            .OrderByDescending(
                static path => Path.GetFileName(path),
                StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateMonthlyPaths(string directory, DateTimeOffset from, DateTimeOffset to)
    {
        foreach (var path in EnumerateRequestedMonthlyPaths(directory, from, to))
        {
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> EnumerateRequestedMonthlyPaths(
        string directory,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var month = new DateTime(from.UtcDateTime.Year, from.UtcDateTime.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var finalMonth = new DateTime(to.UtcDateTime.Year, to.UtcDateTime.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        while (month <= finalMonth)
        {
            yield return Path.Combine(directory, $"{month:yyyy-MM}.jsonl");
            month = month.AddMonths(1);
        }
    }

    private sealed record PartitionReadResult(
        IReadOnlyList<TRecord> Records,
        IReadOnlyList<HistoryReadIssue> Issues,
        bool Completed);
}
