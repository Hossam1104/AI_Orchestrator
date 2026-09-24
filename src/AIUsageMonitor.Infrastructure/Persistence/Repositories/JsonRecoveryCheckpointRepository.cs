using AIUsageMonitor.Application.Orchestration;
using Microsoft.Extensions.Logging;

namespace AIUsageMonitor.Infrastructure.Persistence.Repositories;

/// <summary>GUID-isolated create-once JSON persistence for immutable recovery checkpoints.</summary>
public sealed class JsonRecoveryCheckpointRepository : IRecoveryCheckpointRepository
{
    private const string ExpectedRecordType = "recovery-checkpoint";

    private readonly ApplicationDataPaths _paths;
    private readonly JsonFileStore _files;
    private readonly ILogger<JsonRecoveryCheckpointRepository> _logger;

    public JsonRecoveryCheckpointRepository(
        ApplicationDataPaths paths,
        JsonFileStore files,
        ILogger<JsonRecoveryCheckpointRepository> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RecoveryCheckpointRepositoryWriteResult> CreateAsync(
        RecoveryCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ValidateForWrite(checkpoint);

        var directory = _paths.GetRecoveryCheckpointDirectory(checkpoint.ProjectId, checkpoint.CheckpointId);
        var path = _paths.GetRecoveryCheckpointFile(checkpoint.ProjectId, checkpoint.CheckpointId);
        try
        {
            await _paths.EnsureProjectDirectoriesAsync(checkpoint.ProjectId, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(directory);
            await _files.CreateNewAsync(path, RecoveryCheckpointRecord.FromApplication(checkpoint), cancellationToken)
                .ConfigureAwait(false);
            return new(RecoveryCheckpointRepositoryWriteStatus.Created);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Permission denied while creating recovery checkpoint {CheckpointId}", checkpoint.CheckpointId);
            return new(RecoveryCheckpointRepositoryWriteStatus.Unavailable, "Recovery checkpoint persistence is unavailable.");
        }
        catch (IOException) when (File.Exists(path))
        {
            return new(RecoveryCheckpointRepositoryWriteStatus.CheckpointConflict, "The immutable recovery checkpoint already exists.");
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "I/O failure while creating recovery checkpoint {CheckpointId}", checkpoint.CheckpointId);
            return new(RecoveryCheckpointRepositoryWriteStatus.Unavailable, "Recovery checkpoint persistence is unavailable.");
        }
    }

    public async Task<RecoveryCheckpointReadResult> GetAsync(
        Guid projectId,
        Guid checkpointId,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        ValidateGuid(checkpointId, nameof(checkpointId));

        var path = _paths.GetRecoveryCheckpointFile(projectId, checkpointId);
        var result = await _files.ReadPreservingAsync<RecoveryCheckpointRecord>(path, cancellationToken)
            .ConfigureAwait(false);
        return result.Status switch
        {
            FileReadStatus.Missing =>
                new(RecoveryCheckpointReadState.Missing, ErrorMessage: "Recovery checkpoint is missing."),
            FileReadStatus.Empty =>
                new(RecoveryCheckpointReadState.Invalid, ErrorMessage: "Recovery checkpoint JSON is empty."),
            FileReadStatus.IoFailure or FileReadStatus.PermissionFailure =>
                new(RecoveryCheckpointReadState.Unavailable, ErrorMessage: "Recovery checkpoint is unavailable."),
            FileReadStatus.UnsupportedSchema =>
                new(RecoveryCheckpointReadState.UnsupportedFutureVersion, ErrorMessage: "Recovery checkpoint storage schema is newer than supported."),
            FileReadStatus.Corrupt =>
                new(RecoveryCheckpointReadState.Invalid, ErrorMessage: "Recovery checkpoint JSON is invalid."),
            FileReadStatus.Valid => MapValid(projectId, checkpointId, path, result.Value),
            _ => new(RecoveryCheckpointReadState.Invalid, ErrorMessage: "Recovery checkpoint is invalid.")
        };
    }

    private RecoveryCheckpointReadResult MapValid(
        Guid projectId,
        Guid checkpointId,
        string path,
        RecoveryCheckpointRecord? record)
    {
        if (record is null)
        {
            return new(RecoveryCheckpointReadState.Invalid, ErrorMessage: "Recovery checkpoint payload is missing.");
        }

        if (!string.Equals(record.RecordType, ExpectedRecordType, StringComparison.Ordinal))
        {
            return new(RecoveryCheckpointReadState.Invalid, ErrorMessage: "Recovery checkpoint record type is invalid.");
        }

        if (record.SchemaVersion > RecoveryCheckpointSchema.CurrentVersion)
        {
            return new(RecoveryCheckpointReadState.UnsupportedFutureVersion, ErrorMessage: "Recovery checkpoint schema is newer than supported.");
        }

        if (record.SchemaVersion < RecoveryCheckpointSchema.CurrentVersion)
        {
            return new(RecoveryCheckpointReadState.MigrationRequired, ErrorMessage: "An explicit recovery checkpoint migrator is required.");
        }

        if (record.ProjectId != projectId || record.CheckpointId != checkpointId)
        {
            _logger.LogWarning(
                "Rejected recovery checkpoint identity mismatch for requested project {ProjectId}, checkpoint {CheckpointId}",
                projectId,
                checkpointId);
            return new(RecoveryCheckpointReadState.Invalid, ErrorMessage: "Recovery checkpoint identity does not match its GUID-derived path.");
        }

        if (!RecoveryCheckpointReference.IsSha256(record.ContentHash))
        {
            return new(RecoveryCheckpointReadState.IntegrityFailure, ErrorMessage: "Recovery checkpoint content hash is invalid.");
        }

        if ((record.WorkGraphId is null) != (record.WorkGraphSchemaVersion is null) ||
            (record.WorkGraphId is null) != (record.WorkGraphContentHash is null) ||
            (record.HandoffPackageId is null) != (record.HandoffPackageSchemaVersion is null) ||
            (record.HandoffPackageId is null) != (record.HandoffPackageContentHash is null) ||
            (record.RoutingDecisionId is null) != (record.RoutingDecisionSchemaVersion is null) ||
            (record.RoutingDecisionId is null) != (record.RoutingDecisionContentHash is null) ||
            (record.WorkspacePreparationPlanId is null) != (record.WorkspacePreparationPlanProjectId is null) ||
            (record.WorkspacePreparationPlanId is null) != (record.WorkspacePreparationPlanSchemaVersion is null) ||
            (record.WorkspacePreparationPlanId is null) != (record.WorkspacePreparationPlanContentHash is null))
        {
            return new(RecoveryCheckpointReadState.Invalid, ErrorMessage: "Recovery checkpoint optional authority shape is invalid.");
        }

        try
        {
            var withoutHash = record.ToApplicationForIntegrityValidation();
            if (RecoveryCheckpointIntegrity.ComputeCanonicalPayloadBytes(withoutHash) > RecoveryCheckpointLimits.MaxCanonicalPayloadBytes)
            {
                return new(RecoveryCheckpointReadState.Invalid, ErrorMessage: "Recovery checkpoint exceeds its size bound.");
            }

            var calculatedHash = RecoveryCheckpointIntegrity.ComputeContentHash(withoutHash);
            if (!string.Equals(calculatedHash, record.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return new(RecoveryCheckpointReadState.IntegrityFailure, ErrorMessage: "Recovery checkpoint content hash does not match its payload.");
            }

            return new(RecoveryCheckpointReadState.Valid, record.ToApplication());
        }
        catch (ArgumentException exception) when (exception.Message.Contains("hash", StringComparison.OrdinalIgnoreCase))
        {
            return new(RecoveryCheckpointReadState.IntegrityFailure, ErrorMessage: "Recovery checkpoint content hash is invalid.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
        {
            _logger.LogWarning(exception, "Rejected invalid recovery checkpoint at {Path}", path);
            return new(RecoveryCheckpointReadState.Invalid, ErrorMessage: "Recovery checkpoint is semantically invalid.");
        }
    }

    private static void ValidateForWrite(RecoveryCheckpoint checkpoint)
    {
        if (checkpoint.SchemaVersion != RecoveryCheckpointSchema.CurrentVersion ||
            RecoveryCheckpointIntegrity.ComputeCanonicalPayloadBytes(checkpoint) > RecoveryCheckpointLimits.MaxCanonicalPayloadBytes ||
            !string.Equals(
                RecoveryCheckpointIntegrity.ComputeContentHash(checkpoint),
                checkpoint.ContentHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only a current, internally consistent recovery checkpoint can be written.", nameof(checkpoint));
        }
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier cannot be empty.", parameterName);
        }
    }
}

/// <summary>Two-generation atomic continuation-head persistence with observational reads.</summary>
public sealed class JsonContinuationHeadRepository : IContinuationHeadRepository
{
    private const string ExpectedRecordType = "continuation-head";

    private readonly ApplicationDataPaths _paths;
    private readonly JsonFileStore _files;
    private readonly ILogger<JsonContinuationHeadRepository> _logger;

    public JsonContinuationHeadRepository(
        ApplicationDataPaths paths,
        JsonFileStore files,
        ILogger<JsonContinuationHeadRepository> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ContinuationHeadReadResult> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        var slots = await ReadSlotsAsync(projectId, cancellationToken).ConfigureAwait(false);
        return SelectHead(slots.A, slots.B);
    }

    public Task<ContinuationHeadRepositoryWriteResult> PublishAsync(
        ContinuationHead head,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(head);
        ValidateForWrite(head);
        return _files.ExecuteExclusiveAsync(
            _paths.GetProjectContinuationDirectory(head.ProjectId),
            () => PublishCoreAsync(head, cancellationToken),
            cancellationToken);
    }

    private async Task<ContinuationHeadRepositoryWriteResult> PublishCoreAsync(
        ContinuationHead head,
        CancellationToken cancellationToken)
    {
        var slots = await ReadSlotsAsync(head.ProjectId, cancellationToken).ConfigureAwait(false);

        var current = SelectHead(slots.A, slots.B);
        var expectedGeneration = current.IsValid && current.Head is not null
            ? current.Head.Generation + 1
            : 1;
        if (current.State is not ContinuationHeadReadState.Missing and not ContinuationHeadReadState.Valid ||
            head.Generation != expectedGeneration)
        {
            return new(ContinuationHeadRepositoryWriteStatus.HeadConflict, "The continuation head generation is not the next canonical generation.");
        }

        var targetPath = SelectTargetPath(head.ProjectId, slots.A, slots.B);
        try
        {
            await _paths.EnsureProjectDirectoriesAsync(head.ProjectId, cancellationToken).ConfigureAwait(false);
            await _files.WriteAsync(targetPath, ContinuationHeadRecord.FromApplication(head), cancellationToken)
                .ConfigureAwait(false);
            return new(ContinuationHeadRepositoryWriteStatus.Published);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Permission denied while publishing continuation head for {ProjectId}", head.ProjectId);
            return new(ContinuationHeadRepositoryWriteStatus.Unavailable, "Continuation-head persistence is unavailable.");
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "I/O failure while publishing continuation head for {ProjectId}", head.ProjectId);
            return new(ContinuationHeadRepositoryWriteStatus.Unavailable, "Continuation-head persistence is unavailable.");
        }
    }

    private async Task<(SlotResult A, SlotResult B)> ReadSlotsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var a = await ReadSlotAsync(_paths.GetProjectContinuationHeadSlotAFile(projectId), projectId, cancellationToken).ConfigureAwait(false);
        var b = await ReadSlotAsync(_paths.GetProjectContinuationHeadSlotBFile(projectId), projectId, cancellationToken).ConfigureAwait(false);
        return (a, b);
    }

    private async Task<SlotResult> ReadSlotAsync(string path, Guid projectId, CancellationToken cancellationToken)
    {
        var result = await _files.ReadPreservingAsync<ContinuationHeadRecord>(path, cancellationToken).ConfigureAwait(false);
        return result.Status switch
        {
            FileReadStatus.Missing => new(ContinuationHeadReadState.Missing),
            FileReadStatus.Empty => new(ContinuationHeadReadState.Invalid),
            FileReadStatus.IoFailure or FileReadStatus.PermissionFailure => new(ContinuationHeadReadState.Unavailable),
            FileReadStatus.UnsupportedSchema => new(ContinuationHeadReadState.UnsupportedFutureVersion),
            FileReadStatus.Corrupt => new(ContinuationHeadReadState.Invalid),
            FileReadStatus.Valid => MapSlot(projectId, path, result.Value),
            _ => new(ContinuationHeadReadState.Invalid)
        };
    }

    private SlotResult MapSlot(Guid projectId, string path, ContinuationHeadRecord? record)
    {
        if (record is null)
        {
            return new(ContinuationHeadReadState.Invalid);
        }

        if (!string.Equals(record.RecordType, ExpectedRecordType, StringComparison.Ordinal) || record.ProjectId != projectId)
        {
            return new(ContinuationHeadReadState.Invalid);
        }

        if (record.SchemaVersion > ContinuationHeadSchema.CurrentVersion)
        {
            return new(ContinuationHeadReadState.UnsupportedFutureVersion);
        }

        if (record.SchemaVersion < ContinuationHeadSchema.CurrentVersion)
        {
            return new(ContinuationHeadReadState.MigrationRequired);
        }

        if (!RecoveryCheckpointReference.IsSha256(record.ContentHash))
        {
            return new(ContinuationHeadReadState.IntegrityFailure);
        }

        try
        {
            var withoutHash = record.ToApplicationForIntegrityValidation();
            var calculatedHash = ContinuationHeadIntegrity.ComputeContentHash(withoutHash);
            if (!string.Equals(calculatedHash, record.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return new(ContinuationHeadReadState.IntegrityFailure);
            }

            return new(ContinuationHeadReadState.Valid, record.ToApplication());
        }
        catch (ArgumentException exception) when (exception.Message.Contains("hash", StringComparison.OrdinalIgnoreCase))
        {
            return new(ContinuationHeadReadState.IntegrityFailure);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
        {
            _logger.LogWarning(exception, "Rejected invalid continuation head at {Path}", path);
            return new(ContinuationHeadReadState.Invalid);
        }
    }

    private static ContinuationHeadReadResult SelectHead(SlotResult a, SlotResult b)
    {
        if (a.Head is not null && b.Head is not null)
        {
            if (a.Head.Generation == b.Head.Generation)
            {
                return new(ContinuationHeadReadState.IntegrityFailure, ErrorMessage: "Continuation-head generations are ambiguous.");
            }

            var selected = a.Head.Generation > b.Head.Generation ? a : b;
            return new(ContinuationHeadReadState.Valid, selected.Head);
        }

        if (a.Head is not null || b.Head is not null)
        {
            var selected = a.Head is not null ? a : b;
            var other = a.Head is not null ? b : a;

            if (other.State == ContinuationHeadReadState.Unavailable)
            {
                return new(ContinuationHeadReadState.Unavailable, ErrorMessage: "The other continuation-head slot is unavailable.");
            }

            if (other.State == ContinuationHeadReadState.UnsupportedFutureVersion)
            {
                return new(ContinuationHeadReadState.UnsupportedFutureVersion, ErrorMessage: "The other continuation-head slot uses a newer schema.");
            }

            if (other.State == ContinuationHeadReadState.MigrationRequired)
            {
                return new(ContinuationHeadReadState.MigrationRequired, ErrorMessage: "The other continuation-head slot requires an explicit migration.");
            }

            return new(
                ContinuationHeadReadState.Valid,
                selected.Head,
                FallbackToPreviousGeneration: other.State is not ContinuationHeadReadState.Missing,
                ErrorMessage: other.State is ContinuationHeadReadState.Missing ? null : "The other head slot was rejected; the valid generation was retained.");
        }

        if (a.State == ContinuationHeadReadState.Missing && b.State == ContinuationHeadReadState.Missing)
        {
            return new(ContinuationHeadReadState.Missing, ErrorMessage: "Continuation head is missing.");
        }

        if (a.State == ContinuationHeadReadState.Unavailable || b.State == ContinuationHeadReadState.Unavailable)
        {
            return new(ContinuationHeadReadState.Unavailable, ErrorMessage: "Continuation head is unavailable.");
        }

        if (a.State == ContinuationHeadReadState.UnsupportedFutureVersion || b.State == ContinuationHeadReadState.UnsupportedFutureVersion)
        {
            return new(ContinuationHeadReadState.UnsupportedFutureVersion, ErrorMessage: "Continuation head schema is newer than supported.");
        }

        if (a.State == ContinuationHeadReadState.MigrationRequired || b.State == ContinuationHeadReadState.MigrationRequired)
        {
            return new(ContinuationHeadReadState.MigrationRequired, ErrorMessage: "Continuation head requires an explicit migration.");
        }

        return new(ContinuationHeadReadState.IntegrityFailure, ErrorMessage: "Both continuation-head slots are invalid or fail integrity validation.");
    }

    private string SelectTargetPath(Guid projectId, SlotResult a, SlotResult b)
    {
        if (a.Head is null && b.Head is null)
        {
            return _paths.GetProjectContinuationHeadSlotAFile(projectId);
        }

        if (a.Head is null)
        {
            return _paths.GetProjectContinuationHeadSlotAFile(projectId);
        }

        if (b.Head is null)
        {
            return _paths.GetProjectContinuationHeadSlotBFile(projectId);
        }

        return a.Head.Generation < b.Head.Generation
            ? _paths.GetProjectContinuationHeadSlotAFile(projectId)
            : _paths.GetProjectContinuationHeadSlotBFile(projectId);
    }

    private static void ValidateForWrite(ContinuationHead head)
    {
        if (head.SchemaVersion != ContinuationHeadSchema.CurrentVersion ||
            !string.Equals(ContinuationHeadIntegrity.ComputeContentHash(head), head.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only a current, internally consistent continuation head can be written.", nameof(head));
        }
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Project id cannot be empty.", parameterName);
        }
    }

    private sealed record SlotResult(ContinuationHeadReadState State, ContinuationHead? Head = null);
}
