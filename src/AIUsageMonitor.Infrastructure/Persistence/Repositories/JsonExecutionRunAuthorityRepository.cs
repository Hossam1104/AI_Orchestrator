using AIUsageMonitor.Application.Orchestration;
using Microsoft.Extensions.Logging;

namespace AIUsageMonitor.Infrastructure.Persistence.Repositories;

/// <summary>GUID-isolated, immutable create-once persistence for execution-run authorities.</summary>
public sealed class JsonExecutionRunAuthorityRepository : IExecutionRunAuthorityRepository
{
    private const string ExpectedRecordType = "execution-run-authority";

    private readonly ApplicationDataPaths _paths;
    private readonly JsonFileStore _files;
    private readonly ILogger<JsonExecutionRunAuthorityRepository> _logger;

    public JsonExecutionRunAuthorityRepository(
        ApplicationDataPaths paths,
        JsonFileStore files,
        ILogger<JsonExecutionRunAuthorityRepository> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ExecutionRunAuthorityRepositoryWriteResult> CreateAsync(
        ExecutionRunAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ValidateForWrite(authority);

        var inputPath = _paths.GetExecutionRunAuthorityInputCheckpointFile(authority.ProjectId, authority.InputRecoveryCheckpointReference);
        var path = _paths.GetExecutionRunAuthorityFile(authority.ProjectId, authority.RunId);
        try
        {
            await _paths.EnsureProjectDirectoriesAsync(authority.ProjectId, cancellationToken).ConfigureAwait(false);
            await _files.CreateNewAsync(inputPath, ExecutionRunAuthorityRecord.FromApplication(authority), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Permission denied while creating execution-run authority {RunId}", authority.RunId);
            return new(ExecutionRunAuthorityRepositoryWriteStatus.Unavailable, "Run-authority persistence is unavailable.");
        }
        catch (IOException) when (File.Exists(inputPath))
        {
            _logger.LogInformation("Rejected duplicate execution claim for Ready checkpoint {CheckpointId}", authority.InputRecoveryCheckpointReference.CheckpointId);
            return new(ExecutionRunAuthorityRepositoryWriteStatus.InputCheckpointConflict, "The immutable Ready checkpoint already has an execution claim.");
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "I/O failure while creating execution-run authority {RunId}", authority.RunId);
            return new(ExecutionRunAuthorityRepositoryWriteStatus.Unavailable, "Run-authority persistence is unavailable.");
        }

        try
        {
            await _files.CreateNewAsync(path, ExecutionRunAuthorityRecord.FromApplication(authority), cancellationToken)
                .ConfigureAwait(false);
            return new(ExecutionRunAuthorityRepositoryWriteStatus.Created);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException) when (File.Exists(path))
        {
            return new(ExecutionRunAuthorityRepositoryWriteStatus.RunConflict, "The immutable run authority already exists.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The durable Ready claim was created but its RunId index could not be persisted for {RunId}", authority.RunId);
            return new(ExecutionRunAuthorityRepositoryWriteStatus.Unavailable, "Run-authority persistence is unavailable.");
        }
    }

    public async Task<ExecutionRunAuthorityReadResult> GetAsync(
        Guid projectId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        ValidateGuid(runId, nameof(runId));

        var path = _paths.GetExecutionRunAuthorityFile(projectId, runId);
        var result = await _files.ReadPreservingAsync<ExecutionRunAuthorityRecord>(path, cancellationToken)
            .ConfigureAwait(false);
        return result.Status switch
        {
            FileReadStatus.Missing => new(ExecutionRunAuthorityReadState.Missing, ErrorMessage: "Run authority is missing."),
            FileReadStatus.Empty => new(ExecutionRunAuthorityReadState.Invalid, ErrorMessage: "Run authority JSON is empty."),
            FileReadStatus.IoFailure or FileReadStatus.PermissionFailure => new(ExecutionRunAuthorityReadState.Unavailable, ErrorMessage: "Run-authority persistence is unavailable."),
            FileReadStatus.UnsupportedSchema => new(ExecutionRunAuthorityReadState.UnsupportedFutureVersion, ErrorMessage: "Run-authority storage schema is newer than supported."),
            FileReadStatus.Corrupt => new(ExecutionRunAuthorityReadState.Invalid, ErrorMessage: "Run authority JSON is invalid."),
            FileReadStatus.Valid => MapValid(projectId, runId, path, result.Value),
            _ => new(ExecutionRunAuthorityReadState.Invalid, ErrorMessage: "Run authority is invalid.")
        };
    }

    public async Task<ExecutionRunAuthorityReadResult> GetByInputCheckpointAsync(
        Guid projectId,
        RecoveryCheckpointReference inputCheckpointReference,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        ArgumentNullException.ThrowIfNull(inputCheckpointReference);
        var path = _paths.GetExecutionRunAuthorityInputCheckpointFile(projectId, inputCheckpointReference);
        var result = await _files.ReadPreservingAsync<ExecutionRunAuthorityRecord>(path, cancellationToken).ConfigureAwait(false);
        var mapped = result.Status switch
        {
            FileReadStatus.Missing => new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.Missing, ErrorMessage: "Ready checkpoint execution claim is missing."),
            FileReadStatus.Empty => new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.Invalid, ErrorMessage: "Ready checkpoint execution claim is empty."),
            FileReadStatus.IoFailure or FileReadStatus.PermissionFailure => new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.Unavailable, ErrorMessage: "Run-authority persistence is unavailable."),
            FileReadStatus.UnsupportedSchema => new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.UnsupportedFutureVersion, ErrorMessage: "Run-authority storage schema is newer than supported."),
            FileReadStatus.Corrupt => new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.Invalid, ErrorMessage: "Ready checkpoint execution claim is invalid."),
            FileReadStatus.Valid => MapValid(projectId, result.Value?.RunId ?? Guid.Empty, path, result.Value),
            _ => new ExecutionRunAuthorityReadResult(ExecutionRunAuthorityReadState.Invalid, ErrorMessage: "Ready checkpoint execution claim is invalid.")
        };
        return mapped.IsValid && !SameCheckpoint(mapped.Authority!.InputRecoveryCheckpointReference, inputCheckpointReference)
            ? new(ExecutionRunAuthorityReadState.IntegrityFailure, ErrorMessage: "Ready checkpoint execution claim does not match its immutable input authority.")
            : mapped;
    }

    private ExecutionRunAuthorityReadResult MapValid(
        Guid projectId,
        Guid runId,
        string path,
        ExecutionRunAuthorityRecord? record)
    {
        if (record is null)
        {
            return new(ExecutionRunAuthorityReadState.Invalid, ErrorMessage: "Run-authority payload is missing.");
        }

        if (!string.Equals(record.RecordType, ExpectedRecordType, StringComparison.Ordinal))
        {
            return new(ExecutionRunAuthorityReadState.Invalid, ErrorMessage: "Run-authority record type is invalid.");
        }

        if (record.SchemaVersion > ExecutionRunAuthoritySchema.CurrentVersion)
        {
            return new(ExecutionRunAuthorityReadState.UnsupportedFutureVersion, ErrorMessage: "Run-authority schema is newer than supported.");
        }

        if (record.SchemaVersion < ExecutionRunAuthoritySchema.CurrentVersion)
        {
            return new(ExecutionRunAuthorityReadState.MigrationRequired, ErrorMessage: "An explicit run-authority migrator is required.");
        }

        if (record.ProjectId != projectId || record.RunId != runId || record.WorkspacePlanProjectId != projectId)
        {
            _logger.LogWarning("Rejected execution-run authority identity mismatch at {Path}", path);
            return new(ExecutionRunAuthorityReadState.Invalid, ErrorMessage: "Run-authority identity does not match its GUID-derived path.");
        }

        if (record.WorkGraphId is null ||
            record.WorkGraphSchemaVersion is null ||
            record.WorkGraphContentHash is null ||
            record.WorkGraphNodeId is null ||
            record.Budgets is null ||
            !ExecutionRunAuthorityReference.IsSha256(record.ContentHash) ||
            !ExecutionRunAuthorityReference.IsSha256(record.WorkGraphContentHash) ||
            !ExecutionRunAuthorityReference.IsSha256(record.WorkspaceReceiptContentHash) ||
            !ExecutionRunAuthorityReference.IsSha256(record.InputRecoveryCheckpointContentHash))
        {
            return new(ExecutionRunAuthorityReadState.IntegrityFailure, ErrorMessage: "Run-authority integrity fields are invalid.");
        }

        try
        {
            var withoutHash = record.ToApplicationForIntegrityValidation();
            if (ExecutionRunAuthorityIntegrity.ComputeCanonicalPayloadBytes(withoutHash) > ExecutionRunAuthorityLimits.MaxCanonicalPayloadBytes)
            {
                return new(ExecutionRunAuthorityReadState.Invalid, ErrorMessage: "Run authority exceeds its canonical payload size bound.");
            }

            var calculatedHash = ExecutionRunAuthorityIntegrity.ComputeContentHash(withoutHash);
            if (!string.Equals(calculatedHash, record.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return new(ExecutionRunAuthorityReadState.IntegrityFailure, ErrorMessage: "Run-authority content hash does not match its payload.");
            }

            return new(ExecutionRunAuthorityReadState.Valid, record.ToApplication());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
        {
            _logger.LogWarning(exception, "Rejected invalid or tampered execution-run authority at {Path}", path);
            return new(ExecutionRunAuthorityReadState.IntegrityFailure, ErrorMessage: "Run-authority content does not match its integrity evidence.");
        }
    }

    private static void ValidateForWrite(ExecutionRunAuthority authority)
    {
        if (authority.SchemaVersion != ExecutionRunAuthoritySchema.CurrentVersion ||
            ExecutionRunAuthorityIntegrity.ComputeCanonicalPayloadBytes(authority) > ExecutionRunAuthorityLimits.MaxCanonicalPayloadBytes ||
            !string.Equals(ExecutionRunAuthorityIntegrity.ComputeContentHash(authority), authority.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only a current, internally consistent run authority can be written.", nameof(authority));
        }
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier cannot be empty.", parameterName);
        }
    }

    private static bool SameCheckpoint(RecoveryCheckpointReference left, RecoveryCheckpointReference right) =>
        left.CheckpointId == right.CheckpointId && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
}
