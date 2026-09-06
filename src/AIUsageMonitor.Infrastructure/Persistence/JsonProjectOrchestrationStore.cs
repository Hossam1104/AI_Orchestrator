using AIUsageMonitor.Application.Orchestration;
using Microsoft.Extensions.Logging;

namespace AIUsageMonitor.Infrastructure.Persistence;

/// <summary>
/// Project-isolated monthly JSONL persistence for orchestration metadata. Each stream is
/// physically rooted below one GUID directory and every record carries the same project id for a
/// second, independent isolation check.
/// </summary>
public sealed class JsonProjectOrchestrationStore : IProjectOrchestrationStore, IReviewMetadataReader
{
    private readonly ApplicationDataPaths _paths;
    private readonly JsonlEventStore<ExecutionRunRecord> _runs;
    private readonly JsonlEventStore<EvidenceMetadataRecord> _evidence;
    private readonly JsonlEventStore<ReviewMetadataRecord> _reviews;
    private readonly JsonlEventStore<ActivityAuditRecordFile> _activity;
    private readonly ILogger<JsonProjectOrchestrationStore> _logger;

    public JsonProjectOrchestrationStore(
        ApplicationDataPaths paths,
        JsonlEventStore<ExecutionRunRecord> runs,
        JsonlEventStore<EvidenceMetadataRecord> evidence,
        JsonlEventStore<ReviewMetadataRecord> reviews,
        JsonlEventStore<ActivityAuditRecordFile> activity,
        ILogger<JsonProjectOrchestrationStore> logger)
    {
        _paths = paths;
        _runs = runs;
        _evidence = evidence;
        _reviews = reviews;
        _activity = activity;
        _logger = logger;
    }

    public async Task AppendExecutionRunAsync(
        ExecutionRun run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        await _paths.EnsureProjectDirectoriesAsync(run.ProjectId, cancellationToken).ConfigureAwait(false);
        await _runs.AppendAsync(
                _paths.GetProjectRunsDirectory(run.ProjectId),
                run.RecordedAt,
                ExecutionRunRecord.FromApplication(run),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HistoryReadResult<ExecutionRun>> ReadExecutionRunsAsync(
        Guid projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var raw = await _runs.ReadRangeWithStatusAsync(
                _paths.GetProjectRunsDirectory(projectId),
                from,
                to,
                static value => value.RecordedAt,
                cancellationToken)
            .ConfigureAwait(false);
        var records = new List<ExecutionRun>();
        var issues = raw.Issues.ToList();
        foreach (var record in raw.Records)
        {
            if (record.ProjectId != projectId || !string.Equals(record.RecordType, "execution-run", StringComparison.Ordinal))
            {
                continue;
            }

            TryAdd(record, records, issues);
        }

        return BuildResult(
            raw.Status,
            records.OrderBy(static value => value.RecordedAt).ThenBy(static value => value.RecordId).ToArray(),
            issues);
    }

    public async Task AppendEvidenceAsync(
        EvidenceMetadata evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        await _paths.EnsureProjectDirectoriesAsync(evidence.ProjectId, cancellationToken).ConfigureAwait(false);
        await _evidence.AppendAsync(
                _paths.GetProjectEvidenceDirectory(evidence.ProjectId),
                evidence.CapturedAt,
                EvidenceMetadataRecord.FromApplication(evidence),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HistoryReadResult<EvidenceMetadata>> ReadEvidenceAsync(
        Guid projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var raw = await _evidence.ReadRangeWithStatusAsync(
                _paths.GetProjectEvidenceDirectory(projectId),
                from,
                to,
                static value => value.CapturedAt,
                cancellationToken)
            .ConfigureAwait(false);
        var records = new List<EvidenceMetadata>();
        var issues = raw.Issues.ToList();
        foreach (var record in raw.Records)
        {
            if (record.ProjectId != projectId || !string.Equals(record.RecordType, "evidence-metadata", StringComparison.Ordinal))
            {
                continue;
            }

            TryAdd(record, records, issues);
        }

        return BuildResult(
            raw.Status,
            records.OrderBy(static value => value.CapturedAt).ThenBy(static value => value.EvidenceId).ToArray(),
            issues);
    }

    public async Task AppendReviewAsync(
        ReviewMetadata review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        await _paths.EnsureProjectDirectoriesAsync(review.ProjectId, cancellationToken).ConfigureAwait(false);
        await _reviews.AppendAsync(
                _paths.GetProjectReviewsDirectory(review.ProjectId),
                review.OccurredAt,
                ReviewMetadataRecord.FromApplication(review),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HistoryReadResult<ReviewMetadata>> ReadReviewsAsync(
        Guid projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var raw = await _reviews.ReadRangeWithStatusAsync(
                _paths.GetProjectReviewsDirectory(projectId),
                from,
                to,
                static value => value.OccurredAt,
                cancellationToken)
            .ConfigureAwait(false);
        var records = new List<ReviewMetadata>();
        var issues = raw.Issues.ToList();
        foreach (var record in raw.Records)
        {
            if (record.ProjectId != projectId || !string.Equals(record.RecordType, "review-metadata", StringComparison.Ordinal))
            {
                continue;
            }

            TryAdd(record, records, issues);
        }

        return BuildResult(
            raw.Status,
            records.OrderBy(static value => value.OccurredAt).ThenBy(static value => value.ReviewId).ToArray(),
            issues);
    }

    public async Task<HistoryReadResult<ReviewMetadata>> ReadAllReviewsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            return new HistoryReadResult<ReviewMetadata>(
                [],
                HistoryReadStatus.Unavailable,
                [new(HistoryReadIssueKind.CorruptRecord, "review-metadata", "Project id is required.")]);
        }

        var raw = await _reviews.ReadAllWithStatusAsync(
                _paths.GetProjectReviewsDirectory(projectId),
                static value => value.OccurredAt,
                ReviewWorkflowLimits.MaxReviewRecords,
                cancellationToken)
            .ConfigureAwait(false);
        var records = new List<ReviewMetadata>();
        var issues = raw.Issues.ToList();
        foreach (var record in raw.Records)
        {
            if (record.ProjectId != projectId || !string.Equals(record.RecordType, "review-metadata", StringComparison.Ordinal))
            {
                issues.Add(CreateMappingIssue(record.OccurredAt, "A review record failed project or record-type isolation."));
                continue;
            }
            TryAdd(record, records, issues);
        }

        return BuildResult(
            raw.Status,
            records.OrderBy(static value => value.OccurredAt).ThenBy(static value => value.ReviewId).ToArray(),
            issues);
    }

    public async Task AppendActivityAsync(
        ActivityAuditRecord activity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        await _paths.EnsureProjectDirectoriesAsync(activity.ProjectId, cancellationToken).ConfigureAwait(false);
        await _activity.AppendAsync(
                _paths.GetProjectActivityDirectory(activity.ProjectId),
                activity.OccurredAt,
                ActivityAuditRecordFile.FromApplication(activity),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HistoryReadResult<ActivityAuditRecord>> ReadActivityAsync(
        Guid projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var raw = await _activity.ReadRangeWithStatusAsync(
                _paths.GetProjectActivityDirectory(projectId),
                from,
                to,
                static value => value.OccurredAt,
                cancellationToken)
            .ConfigureAwait(false);
        var records = new List<ActivityAuditRecord>();
        var issues = raw.Issues.ToList();
        foreach (var record in raw.Records)
        {
            if (record.ProjectId != projectId || !string.Equals(record.RecordType, "activity-audit", StringComparison.Ordinal))
            {
                continue;
            }

            TryAdd(record, records, issues);
        }

        return BuildResult(
            raw.Status,
            records.OrderBy(static value => value.OccurredAt).ThenBy(static value => value.ActivityId).ToArray(),
            issues);
    }

    private void TryAdd(
        ExecutionRunRecord record,
        ICollection<ExecutionRun> destination,
        ICollection<HistoryReadIssue> issues)
    {
        try
        {
            destination.Add(record.ToApplication());
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "Skipping invalid execution run record {RunId}", record.RunId);
            issues.Add(CreateMappingIssue(record.RecordedAt, "Invalid execution run record was skipped."));
        }
    }

    private void TryAdd(
        EvidenceMetadataRecord record,
        ICollection<EvidenceMetadata> destination,
        ICollection<HistoryReadIssue> issues)
    {
        try
        {
            destination.Add(record.ToApplication());
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "Skipping invalid evidence record {EvidenceId}", record.EvidenceId);
            issues.Add(CreateMappingIssue(record.CapturedAt, "Invalid evidence record was skipped."));
        }
    }

    private void TryAdd(
        ReviewMetadataRecord record,
        ICollection<ReviewMetadata> destination,
        ICollection<HistoryReadIssue> issues)
    {
        try
        {
            destination.Add(record.ToApplication());
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "Skipping invalid review record {ReviewId}", record.ReviewId);
            issues.Add(CreateMappingIssue(record.OccurredAt, "Invalid review record was skipped."));
        }
    }

    private void TryAdd(
        ActivityAuditRecordFile record,
        ICollection<ActivityAuditRecord> destination,
        ICollection<HistoryReadIssue> issues)
    {
        try
        {
            destination.Add(record.ToApplication());
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "Skipping invalid activity record {ActivityId}", record.ActivityId);
            issues.Add(CreateMappingIssue(record.OccurredAt, "Invalid activity record was skipped."));
        }
    }

    private static HistoryReadResult<T> BuildResult<T>(
        HistoryReadStatus status,
        IReadOnlyList<T> records,
        IReadOnlyList<HistoryReadIssue> issues)
    {
        if (status == HistoryReadStatus.Success && issues.Count > 0)
        {
            status = HistoryReadStatus.Partial;
        }

        return new HistoryReadResult<T>(records, status, issues);
    }

    private static HistoryReadIssue CreateMappingIssue(DateTimeOffset timestamp, string message) =>
        new(
            HistoryReadIssueKind.CorruptRecord,
            $"{timestamp.UtcDateTime:yyyy-MM}.jsonl",
            message);
}
