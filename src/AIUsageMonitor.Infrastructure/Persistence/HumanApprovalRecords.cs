using AIUsageMonitor.Application.Approvals;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Planning;
using Microsoft.Extensions.Logging;

namespace AIUsageMonitor.Infrastructure.Persistence;

/// <summary>Flattened JSONL representation of an immutable APO-49 request event.</summary>
public sealed class HumanApprovalEventRecord
{
    public int SchemaVersion { get; set; } = JsonFileStore.CurrentSchemaVersion;
    public string RecordType { get; set; } = "human-approval-event";
    public Guid EventId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RequestId { get; set; }
    public HumanApprovalEventKind Kind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public HumanApprovalActorKind ActorKind { get; set; }
    public string ActorReference { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public HumanApprovalRequestRecord? Request { get; set; }
    public string ContentHash { get; set; } = string.Empty;

    public static HumanApprovalEventRecord FromApplication(HumanApprovalEvent value) => new()
    {
        EventId = value.EventId,
        ProjectId = value.ProjectId,
        RequestId = value.RequestId,
        Kind = value.Kind,
        OccurredAt = value.OccurredAt,
        ActorKind = value.ActorKind,
        ActorReference = value.ActorReference,
        Reason = value.Reason,
        Request = value.Request is null ? null : HumanApprovalRequestRecord.FromApplication(value.Request),
        ContentHash = value.ContentHash
    };

    public HumanApprovalEvent ToApplication()
    {
        var request = Request?.ToApplication();
        return new(
            EventId,
            ProjectId,
            RequestId,
            Kind,
            OccurredAt,
            ActorKind,
            ActorReference,
            Reason,
            request,
            ContentHash);
    }
}

public sealed class HumanApprovalRequestRecord
{
    public int SchemaVersion { get; set; } = HumanApprovalSchema.CurrentVersion;
    public Guid ProjectId { get; set; }
    public Guid RequestId { get; set; }
    public HumanApprovalActionKind ActionKind { get; set; }
    public Guid ContractId { get; set; }
    public int ContractRevision { get; set; }
    public int ContractSchemaVersion { get; set; }
    public string ContractContentHash { get; set; } = string.Empty;
    public HumanApprovalTargetRecord Target { get; set; } = new();
    public HumanApprovalEvidenceRevisionRecord EvidenceRevision { get; set; } = new();
    public string RequesterReference { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string PolicyReference { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;

    public static HumanApprovalRequestRecord FromApplication(HumanApprovalRequest value) => new()
    {
        ProjectId = value.ProjectId,
        RequestId = value.RequestId,
        ActionKind = value.ActionKind,
        ContractId = value.ContractReference.ContractId,
        ContractRevision = value.ContractReference.Revision,
        ContractSchemaVersion = value.ContractReference.SchemaVersion,
        ContractContentHash = value.ContractReference.ContentHash,
        Target = HumanApprovalTargetRecord.FromApplication(value.Target),
        EvidenceRevision = HumanApprovalEvidenceRevisionRecord.FromApplication(value.EvidenceRevision),
        RequesterReference = value.RequesterReference,
        RequestedAt = value.RequestedAt,
        ExpiresAt = value.ExpiresAt,
        Reason = value.Reason,
        PolicyReference = value.PolicyReference,
        ContentHash = value.ContentHash
    };

    public HumanApprovalRequest ToApplication()
    {
        if (SchemaVersion != HumanApprovalSchema.CurrentVersion)
            throw new InvalidOperationException("Unsupported human approval request schema.");
        return new(
            ProjectId,
            RequestId,
            ActionKind,
            new PlanningExecutionContractReference(ContractId, ContractRevision, ContractSchemaVersion, ContractContentHash),
            Target.ToApplication(),
            EvidenceRevision.ToApplication(),
            RequesterReference,
            RequestedAt,
            ExpiresAt,
            Reason,
            PolicyReference,
            ContentHash);
    }
}

public sealed class HumanApprovalTargetRecord
{
    public HumanApprovalActionKind ActionKind { get; set; }
    public string SafeSummary { get; set; } = string.Empty;
    public string? CanonicalRepositoryIdentity { get; set; }
    public string? BaseRef { get; set; }
    public string? BaseSha { get; set; }
    public string? HeadRef { get; set; }
    public string? HeadSha { get; set; }
    public string? OperationFingerprint { get; set; }
    public string ContentHash { get; set; } = string.Empty;

    public static HumanApprovalTargetRecord FromApplication(HumanApprovalTarget value) => new()
    {
        ActionKind = value.ActionKind,
        SafeSummary = value.SafeSummary,
        CanonicalRepositoryIdentity = value.CanonicalRepositoryIdentity,
        BaseRef = value.BaseRef,
        BaseSha = value.BaseSha,
        HeadRef = value.HeadRef,
        HeadSha = value.HeadSha,
        OperationFingerprint = value.OperationFingerprint,
        ContentHash = value.ContentHash
    };

    public HumanApprovalTarget ToApplication() => new(
        ActionKind,
        SafeSummary,
        CanonicalRepositoryIdentity,
        BaseRef,
        BaseSha,
        HeadRef,
        HeadSha,
        OperationFingerprint,
        ContentHash);
}

public sealed class HumanApprovalEvidenceRevisionRecord
{
    public int SchemaVersion { get; set; } = HumanApprovalSchema.CurrentVersion;
    public List<HumanApprovalEvidenceReferenceRecord> References { get; set; } = [];
    public string ContentHash { get; set; } = string.Empty;

    public static HumanApprovalEvidenceRevisionRecord FromApplication(HumanApprovalEvidenceRevision value) => new()
    {
        SchemaVersion = value.SchemaVersion,
        References = value.References.Select(HumanApprovalEvidenceReferenceRecord.FromApplication).ToList(),
        ContentHash = value.ContentHash
    };

    public HumanApprovalEvidenceRevision ToApplication()
    {
        if (References is null)
            throw new ArgumentException("Persisted evidence references are missing.");
        return new(
            References.Select(static value => value.ToApplication()).ToArray(),
            ContentHash,
            SchemaVersion);
    }
}

public sealed class HumanApprovalEvidenceReferenceRecord
{
    public Guid? EvidenceId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public int? SchemaVersion { get; set; }
    public string? ContentHash { get; set; }

    public static HumanApprovalEvidenceReferenceRecord FromApplication(HumanApprovalEvidenceReference value) => new()
    {
        EvidenceId = value.EvidenceId,
        Kind = value.Kind,
        Reference = value.Reference,
        SchemaVersion = value.SchemaVersion,
        ContentHash = value.ContentHash
    };

    public HumanApprovalEvidenceReference ToApplication() =>
        new(Kind, Reference, EvidenceId, SchemaVersion, ContentHash);
}

/// <summary>Project-isolated append-only JSONL authority for APO-49 lifecycle events.</summary>
public sealed class JsonHumanApprovalStore : IHumanApprovalStore, IDisposable
{
    private const string ExpectedRecordType = "human-approval-event";
    private readonly ApplicationDataPaths _paths;
    private readonly JsonlEventStore<HumanApprovalEventRecord> _events;
    private readonly ILogger<JsonHumanApprovalStore> _logger;
    private readonly HandoffRedactionService _redaction = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public JsonHumanApprovalStore(
        ApplicationDataPaths paths,
        JsonlEventStore<HumanApprovalEventRecord> events,
        ILogger<JsonHumanApprovalStore> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HumanApprovalStoreReadResult> ReadAsync(
        Guid projectId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || requestId == Guid.Empty)
            throw new ArgumentException("Project and request identifiers are required.");
        var read = await ReadProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!read.IsUsable || read.Histories.Any(value => value.RequestId == requestId))
            return read.IsUsable && read.Histories.Any(value => value.RequestId == requestId)
                ? new(read.Status, read.Histories.Where(value => value.RequestId == requestId).ToArray(), read.ErrorMessage)
                : read;
        return new(HumanApprovalHistoryReadStatus.Missing, errorMessage: "Approval request was not found.");
    }

    public async Task<HumanApprovalStoreReadResult> ReadProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));

        var directory = _paths.GetProjectApprovalsDirectory(projectId);
        var raw = await _events.ReadAllWithStatusAsync(
                directory,
                static value => value.OccurredAt,
                HumanApprovalLimits.MaxEventsPerProject + 1,
                cancellationToken)
            .ConfigureAwait(false);
        if (raw.Status != AIUsageMonitor.Application.Orchestration.HistoryReadStatus.Success)
        {
            var status = raw.Issues.Any(static issue => issue.Kind == AIUsageMonitor.Application.Orchestration.HistoryReadIssueKind.UnsupportedSchema)
                ? HumanApprovalHistoryReadStatus.Unsupported
                : HumanApprovalHistoryReadStatus.Corrupt;
            return new(status, errorMessage: "Approval history is incomplete or unreadable.");
        }

        var values = new List<HumanApprovalEvent>(raw.Records.Count);
        try
        {
            foreach (var record in raw.Records)
            {
                if (!string.Equals(record.RecordType, ExpectedRecordType, StringComparison.Ordinal) || record.ProjectId != projectId)
                    return new(HumanApprovalHistoryReadStatus.Corrupt, errorMessage: "Approval history failed project or record-type isolation.");
                var value = record.ToApplication();
                values.Add(value);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
        {
            _logger.LogWarning(exception, "Approval history failed integrity validation for project {ProjectId}", projectId);
            return new(HumanApprovalHistoryReadStatus.Corrupt, errorMessage: "Approval history failed content-integrity validation.");
        }

        if (values.Select(static value => value.EventId).Distinct().Count() != values.Count)
            return new(HumanApprovalHistoryReadStatus.Corrupt, errorMessage: "Approval history contains a duplicate event identity.");

        var histories = values
            .GroupBy(static value => value.RequestId)
            .Select(group => new HumanApprovalHistory(
                projectId,
                group.Key,
                group.ToArray()))
            .OrderByDescending(static value => value.Events[0].OccurredAt)
            .ToArray();
        return new(HumanApprovalHistoryReadStatus.Success, histories);
    }

    public async Task<HumanApprovalOperationResult> AppendAsync(
        HumanApprovalEvent value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!SafeToPersist(value))
                return new(HumanApprovalMutationStatus.Unavailable, ErrorMessage: "Approval metadata crossed the secret-redaction boundary.");

            var read = await ReadProjectAsync(value.ProjectId, cancellationToken).ConfigureAwait(false);
            if (!read.IsUsable)
                return new(HumanApprovalMutationStatus.Unavailable, ErrorMessage: read.ErrorMessage);
            var existingEvents = read.Histories.SelectMany(static history => history.Events).ToArray();
            if (existingEvents.Any(existing => existing.EventId == value.EventId))
                return new(HumanApprovalMutationStatus.Duplicate, ErrorMessage: "Approval event identity already exists.");
            if (existingEvents.Length >= HumanApprovalLimits.MaxEventsPerProject)
                return new(HumanApprovalMutationStatus.CapacityExceeded, ErrorMessage: "Approval event capacity was reached before append.");

            var history = read.Histories.SingleOrDefault(history => history.RequestId == value.RequestId);
            if (!ValidateAppend(history, value, out var error))
                return new(HumanApprovalMutationStatus.InvalidRequest, ErrorMessage: error);

            _paths.EnsureProjectDirectories(value.ProjectId);
            await _events.AppendAsync(
                    _paths.GetProjectApprovalsDirectory(value.ProjectId),
                    value.OccurredAt,
                    HumanApprovalEventRecord.FromApplication(value),
                    cancellationToken)
                .ConfigureAwait(false);
            return new(HumanApprovalMutationStatus.Created, value);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "Approval history append failed for project {ProjectId}", value.ProjectId);
            return new(HumanApprovalMutationStatus.Unavailable, ErrorMessage: exception.Message);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private bool SafeToPersist(HumanApprovalEvent value)
    {
        if (value.Request is not null &&
            (!Safe(value.Request.RequesterReference) || !Safe(value.Request.Reason) || !Safe(value.Request.PolicyReference) ||
             !Safe(value.Request.Target.SafeSummary) || !SafeOptional(value.Request.Target.CanonicalRepositoryIdentity) ||
             !SafeOptional(value.Request.Target.BaseRef) || !SafeOptional(value.Request.Target.HeadRef) ||
             value.Request.EvidenceRevision.References.Any(reference => !Safe(reference.Kind) || !Safe(reference.Reference))))
            return false;
        return Safe(value.ActorReference) && SafeOptional(value.Reason);
    }

    private bool Safe(string value) => !_redaction.ValidateIdentityText(value).RequiresRedaction;

    private bool SafeOptional(string? value) => value is null || Safe(value);

    private static bool ValidateAppend(HumanApprovalHistory? history, HumanApprovalEvent value, out string error)
    {
        error = string.Empty;
        if (history is null)
        {
            if (value.Kind != HumanApprovalEventKind.Requested)
            {
                error = "A non-request approval event cannot precede its immutable request.";
                return false;
            }
            return true;
        }

        var events = history.Events;
        if (value.Kind == HumanApprovalEventKind.Requested)
        {
            error = "An immutable approval request identity cannot be requested twice.";
            return false;
        }
        if (events.Count(static item => item.Kind == HumanApprovalEventKind.Requested) != 1)
        {
            error = "Approval history must contain exactly one Requested event.";
            return false;
        }
        var request = events.First(static item => item.Kind == HumanApprovalEventKind.Requested).Request;
        if (request is null)
        {
            error = "Approval history has no valid Requested event.";
            return false;
        }
        if (value.OccurredAt < request.RequestedAt || value.OccurredAt >= request.ExpiresAt)
        {
            error = "An approval event must be within the immutable request time window.";
            return false;
        }
        var terminal = events.Any(static item => item.Kind is HumanApprovalEventKind.Approved or HumanApprovalEventKind.Rejected or HumanApprovalEventKind.Waived);
        if (terminal)
        {
            error = "A terminal human decision cannot be followed by another event.";
            return false;
        }
        if (value.Kind == HumanApprovalEventKind.Escalated && events.Any(static item => item.Kind == HumanApprovalEventKind.Escalated))
        {
            error = "An approval request can have at most one escalation marker.";
            return false;
        }
        return true;
    }

    public void Dispose() => _writeGate.Dispose();
}
