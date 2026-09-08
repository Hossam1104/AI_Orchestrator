using AIUsageMonitor.Application.Delivery;
using AIUsageMonitor.Application.Trackers;

namespace AIUsageMonitor.Infrastructure.Persistence;

public sealed class SourceControlDeliveryAuditRecord
{
    public string RecordType { get; set; } = "source-control-delivery";
    public int SchemaVersion { get; set; } = 1;
    public Guid CommandId { get; set; }
    public Guid ProjectId { get; set; }
    public string OperationKind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string WorkItemIdentity { get; set; } = string.Empty;
    public string ContractReference { get; set; } = string.Empty;
    public string RepositoryIdentity { get; set; } = string.Empty;
    public string BaseRef { get; set; } = string.Empty;
    public string BaseSha { get; set; } = string.Empty;
    public string HeadRef { get; set; } = string.Empty;
    public string ExpectedHeadSha { get; set; } = string.Empty;
    public string ActorReference { get; set; } = string.Empty;
    public string AuditIdentity { get; set; } = string.Empty;
    public string EvidenceRevision { get; set; } = string.Empty;
    public string RemoteEvidenceFingerprint { get; set; } = string.Empty;
    public string? ActualHeadSha { get; set; }
    public string? PullRequestId { get; set; }
    public string? MergeCommitSha { get; set; }
    public string? ErrorMessage { get; set; }
    public bool MutationSent { get; set; }
    public bool MayHaveModifiedRemote { get; set; }
    public string? CommandContentHash { get; set; }
    public bool RemoteDeliveryVerified { get; set; }
    public string? TrackerPlanIdentity { get; set; }
    public string? TrackerOperationIdentity { get; set; }
    public string? TrackerAuthorityContentHash { get; set; }
    public string? TrackerOutcome { get; set; }
    public string? CredentialReference { get; set; }
    public Guid EventId { get; set; }
    public string EventKind { get; set; } = string.Empty;
    public string? ValidationDecisionReference { get; set; }
    public string? HumanApprovalReference { get; set; }
    public string? PostEvidenceFingerprint { get; set; }
    public string ContentHash { get; set; } = string.Empty;

    public static SourceControlDeliveryAuditRecord FromApplication(SourceControlDeliveryAuditEvent value) => new()
    {
        CommandId = value.CommandId,
        ProjectId = value.ProjectId,
        OperationKind = value.OperationKind.ToString(),
        Status = value.Status.ToString(),
        OccurredAt = value.OccurredAt,
        WorkItemIdentity = value.WorkItemIdentity,
        ContractReference = value.ContractReference,
        RepositoryIdentity = value.RepositoryIdentity,
        BaseRef = value.BaseRef,
        BaseSha = value.BaseSha,
        HeadRef = value.HeadRef,
        ExpectedHeadSha = value.ExpectedHeadSha,
        ActorReference = value.ActorReference,
        AuditIdentity = value.AuditIdentity,
        EvidenceRevision = value.EvidenceRevision,
        RemoteEvidenceFingerprint = value.RemoteEvidenceFingerprint,
        ActualHeadSha = value.ActualHeadSha,
        PullRequestId = value.PullRequestId,
        MergeCommitSha = value.MergeCommitSha,
        ErrorMessage = value.ErrorMessage,
        MutationSent = value.MutationSent,
        MayHaveModifiedRemote = value.MayHaveModifiedRemote
        ,EventId = value.EventId,
        EventKind = value.EventKind.ToString(),
        ValidationDecisionReference = value.ValidationDecisionReference,
        HumanApprovalReference = value.HumanApprovalReference,
        PostEvidenceFingerprint = value.PostEvidenceFingerprint,
        CommandContentHash = value.CommandContentHash,
        RemoteDeliveryVerified = value.RemoteDeliveryVerified,
        TrackerPlanIdentity = value.TrackerPlanIdentity,
        TrackerOperationIdentity = value.TrackerOperationIdentity,
        TrackerAuthorityContentHash = value.TrackerAuthorityContentHash,
        TrackerOutcome = value.TrackerOutcome?.ToString(),
        CredentialReference = value.CredentialReference,
        ContentHash = value.ContentHash
    };

    public bool TryToApplication(out SourceControlDeliveryAuditEvent? value)
    {
        value = null;
        if (RecordType != "source-control-delivery" || SchemaVersion != 1 || EventId == Guid.Empty ||
            !Enum.TryParse<SourceControlDeliveryOperationKind>(OperationKind, out var operation) ||
            !Enum.TryParse<SourceControlDeliveryStatus>(Status, out var status) ||
            CommandId == Guid.Empty || ProjectId == Guid.Empty || OccurredAt == default ||
            !SourceControlDeliveryAuditEvent.IsSha256(CommandContentHash))
            return false;
        var candidate = new SourceControlDeliveryAuditEvent(
            CommandId, ProjectId, operation, status, OccurredAt, WorkItemIdentity, ContractReference,
            RepositoryIdentity, BaseRef, BaseSha, HeadRef, ExpectedHeadSha, ActorReference, AuditIdentity,
            EvidenceRevision, RemoteEvidenceFingerprint, ActualHeadSha, PullRequestId, MergeCommitSha,
            ErrorMessage, MutationSent, MayHaveModifiedRemote);
        value = candidate with
        {
            EventId = EventId,
            CommandContentHash = CommandContentHash,
            RemoteDeliveryVerified = RemoteDeliveryVerified,
            TrackerPlanIdentity = TrackerPlanIdentity,
            TrackerOperationIdentity = TrackerOperationIdentity,
            TrackerAuthorityContentHash = TrackerAuthorityContentHash,
            TrackerOutcome = Enum.TryParse<TrackerMutationOutcome>(TrackerOutcome, out var trackerOutcome) ? trackerOutcome : null,
            CredentialReference = CredentialReference,
            EventKindOverride = string.Equals(EventKind, nameof(SourceControlDeliveryAuditEventKind.Attempted), StringComparison.Ordinal)
                ? SourceControlDeliveryAuditEventKind.Attempted
                : null,
            ValidationDecisionReference = ValidationDecisionReference,
            HumanApprovalReference = HumanApprovalReference,
            PostEvidenceFingerprint = PostEvidenceFingerprint
        };
        return string.Equals(value.ContentHash, ContentHash, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(EventKind, value.EventKind.ToString(), StringComparison.Ordinal);
    }
}

public sealed class JsonSourceControlDeliveryAuditStore : ISourceControlDeliveryAuditStore
{
    private readonly ApplicationDataPaths _paths;
    private readonly JsonlEventStore<SourceControlDeliveryAuditRecord> _events;
    private static readonly SemaphoreSlim AppendGate = new(1, 1);

    public JsonSourceControlDeliveryAuditStore(
        ApplicationDataPaths paths,
        JsonlEventStore<SourceControlDeliveryAuditRecord> events)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public async Task<SourceControlDeliveryAuditReadResult> FindAsync(Guid projectId, Guid commandId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || commandId == Guid.Empty)
            return new(SourceControlDeliveryAuditReadState.Corrupt, ErrorMessage: "Delivery audit identity is invalid.");
        var history = await _events.ReadAllWithStatusAsync(
            _paths.GetProjectDeliveryAuditDirectory(projectId),
            static value => value.OccurredAt,
            SourceControlDeliveryLimits.MaxAuditRecords,
            cancellationToken).ConfigureAwait(false);
        if (history.Status != Application.Orchestration.HistoryReadStatus.Success || history.Issues.Count > 0)
            return new(SourceControlDeliveryAuditReadState.Corrupt, ErrorMessage: "Delivery audit history is incomplete or corrupt.");

        SourceControlDeliveryAuditEvent? latest = null;
        foreach (var record in history.Records)
        {
            if (!record.TryToApplication(out var mapped) || mapped is null || mapped.ProjectId != projectId)
                return new(SourceControlDeliveryAuditReadState.Corrupt, ErrorMessage: "Delivery audit record failed integrity validation.");
            if (mapped.CommandId == commandId && (latest is null || mapped.OccurredAt >= latest.OccurredAt)) latest = mapped;
        }
        return latest is null
            ? new(SourceControlDeliveryAuditReadState.Missing)
            : new(SourceControlDeliveryAuditReadState.Found, latest);
    }

    public async Task AppendAsync(SourceControlDeliveryAuditEvent value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.ProjectId == Guid.Empty || value.CommandId == Guid.Empty)
            throw new ArgumentException("Delivery audit identity is required.", nameof(value));
        if (!SourceControlDeliveryAuditEvent.IsSha256(value.CommandContentHash))
            throw new InvalidOperationException("Delivery audit events require a valid SHA-256 command content hash.");
        await AppendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var history = await _events.ReadAllWithStatusAsync(
                _paths.GetProjectDeliveryAuditDirectory(value.ProjectId),
                static record => record.OccurredAt,
                SourceControlDeliveryLimits.MaxAuditRecords,
                cancellationToken).ConfigureAwait(false);
            if (history.Status != Application.Orchestration.HistoryReadStatus.Success || history.Issues.Count > 0)
                throw new InvalidOperationException("Delivery audit history is corrupt or unavailable; append was rejected.");
            if (history.Records.Count >= SourceControlDeliveryLimits.MaxAuditRecords)
                throw new InvalidOperationException("Delivery audit capacity has been exhausted.");
            if (history.Records.Any(record => record.EventId == value.EventId))
                throw new InvalidOperationException("Duplicate delivery audit event identity was rejected.");
            await _paths.EnsureProjectDirectoriesAsync(value.ProjectId, cancellationToken).ConfigureAwait(false);
            await _events.AppendAsync(
                _paths.GetProjectDeliveryAuditDirectory(value.ProjectId),
                value.OccurredAt,
                SourceControlDeliveryAuditRecord.FromApplication(value),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            AppendGate.Release();
        }
    }

    public async Task<SourceControlDeliveryAttemptClaim> TryBeginAttemptAsync(
        SourceControlDeliveryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ProjectId == Guid.Empty || command.CommandId == Guid.Empty ||
            !SourceControlDeliveryAuditEvent.IsSha256(command.CommandContentHash))
            return new(SourceControlDeliveryAttemptClaimState.Corrupt, ErrorMessage: "The command identity or content hash is invalid.");

        await AppendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var history = await _events.ReadAllWithStatusAsync(
                _paths.GetProjectDeliveryAuditDirectory(command.ProjectId),
                static record => record.OccurredAt,
                SourceControlDeliveryLimits.MaxAuditRecords,
                cancellationToken).ConfigureAwait(false);
            if (history.Status != Application.Orchestration.HistoryReadStatus.Success || history.Issues.Count > 0)
                return new(SourceControlDeliveryAttemptClaimState.Corrupt, ErrorMessage: "Delivery audit history is corrupt or unavailable; the command was not claimed.");

            SourceControlDeliveryAuditEvent? existing = null;
            foreach (var record in history.Records)
            {
                if (!record.TryToApplication(out var mapped) || mapped is null || mapped.ProjectId != command.ProjectId)
                    return new(SourceControlDeliveryAttemptClaimState.Corrupt, ErrorMessage: "Delivery audit history contains an invalid record; the command was not claimed.");
                if (mapped.CommandId == command.CommandId && (existing is null || mapped.OccurredAt >= existing.OccurredAt))
                    existing = mapped;
            }

            if (existing is not null)
                return existing.MatchesCommand(command)
                    ? new(SourceControlDeliveryAttemptClaimState.Existing, existing)
                    : new(SourceControlDeliveryAttemptClaimState.ConflictingIntent, existing, "The command identity conflicts with existing delivery audit evidence.");
            if (history.Records.Count >= SourceControlDeliveryLimits.MaxAuditRecords)
                return new(SourceControlDeliveryAttemptClaimState.CapacityExceeded, ErrorMessage: "Delivery audit capacity has been exhausted; no Attempted event was appended.");

            var attempted = new SourceControlDeliveryAuditEvent(
                command.CommandId, command.ProjectId, command.OperationKind, SourceControlDeliveryStatus.ReconciliationRequired,
                DateTimeOffset.UtcNow, command.WorkItemIdentity, command.ContractReference.ToString(),
                command.Target.CanonicalRepositoryIdentity, command.Target.BaseRef, command.Target.BaseSha,
                command.Target.HeadRef, command.Target.HeadSha, command.ActorReference, command.AuditIdentity,
                command.EvidenceRevision, command.Evidence.RemoteEvidenceFingerprint,
                PullRequestId: command.Target.PullRequestId, MutationSent: false,
                CommandContentHash: command.CommandContentHash,
                CredentialReference: command.CredentialReference,
                TrackerPlanIdentity: SourceControlDeliveryIntent.TrackerPlanIdentity(command.TrackerPlan),
                TrackerOperationIdentity: SourceControlDeliveryIntent.TrackerOperationIdentity(command.TrackerOperation),
                TrackerAuthorityContentHash: command.TrackerAuthority?.ContentHash)
            {
                EventKindOverride = SourceControlDeliveryAuditEventKind.Attempted,
                ValidationDecisionReference = command.Evidence.ValidationDecisionReference?.ToString(),
                HumanApprovalReference = command.Evidence.HumanApprovalRequestId?.ToString("D")
            };
            await _paths.EnsureProjectDirectoriesAsync(command.ProjectId, cancellationToken).ConfigureAwait(false);
            await _events.AppendAsync(
                _paths.GetProjectDeliveryAuditDirectory(command.ProjectId),
                attempted.OccurredAt,
                SourceControlDeliveryAuditRecord.FromApplication(attempted),
                cancellationToken).ConfigureAwait(false);
            return new(SourceControlDeliveryAttemptClaimState.Claimed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(SourceControlDeliveryAttemptClaimState.Unavailable, ErrorMessage: exception.Message);
        }
        finally
        {
            AppendGate.Release();
        }
    }
}
