using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Validation;
using Microsoft.Extensions.Logging;

namespace AIUsageMonitor.Infrastructure.Persistence;

/// <summary>Primitive JSONL representation of one typed review-workflow event.</summary>
public sealed class ReviewWorkflowEventRecord
{
    public int SchemaVersion { get; set; } = JsonFileStore.CurrentSchemaVersion;
    public string RecordType { get; set; } = "review-workflow-event";
    public Guid EventId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RootReviewId { get; set; }
    public Guid CurrentReviewId { get; set; }
    public ReviewWorkflowEventKind Kind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? FindingId { get; set; }
    public ReviewFindingAdjudication? Disposition { get; set; }
    public string? AuthorityReference { get; set; }
    public ReviewAuthorityKind? AuthorityKind { get; set; }
    public string? Reason { get; set; }
    public int? AttemptNumber { get; set; }
    public Guid? ExecutionRunId { get; set; }
    public int? ExecutionRunSchemaVersion { get; set; }
    public string? ExecutionRunContentHash { get; set; }
    public Guid? HandoffPackageId { get; set; }
    public int? HandoffPackageSchemaVersion { get; set; }
    public string? HandoffPackageContentHash { get; set; }
    public List<string> EvidenceReferences { get; set; } = [];
    public Guid? ValidationDecisionId { get; set; }
    public int? ValidationDecisionSchemaVersion { get; set; }
    public string? ValidationDecisionContentHash { get; set; }
    public ValidationGateDecisionState? ValidationState { get; set; }
    public Guid? LinkedReviewId { get; set; }

    public static ReviewWorkflowEventRecord FromApplication(ReviewWorkflowEvent value) => new()
    {
        EventId = value.EventId,
        ProjectId = value.ProjectId,
        RootReviewId = value.RootReviewId,
        CurrentReviewId = value.CurrentReviewId,
        Kind = value.Kind,
        OccurredAt = value.OccurredAt,
        FindingId = value.FindingId,
        Disposition = value.Disposition,
        AuthorityReference = value.AuthorityReference,
        AuthorityKind = value.AuthorityKind,
        Reason = value.Reason,
        AttemptNumber = value.AttemptNumber,
        ExecutionRunId = value.ExecutionRunAuthorityReference?.RunId,
        ExecutionRunSchemaVersion = value.ExecutionRunAuthorityReference?.SchemaVersion,
        ExecutionRunContentHash = value.ExecutionRunAuthorityReference?.ContentHash,
        HandoffPackageId = value.HandoffPackageReference?.PackageId,
        HandoffPackageSchemaVersion = value.HandoffPackageReference?.SchemaVersion,
        HandoffPackageContentHash = value.HandoffPackageReference?.ContentHash,
        EvidenceReferences = value.EvidenceReferences.ToList(),
        ValidationDecisionId = value.ValidationDecisionReference?.DecisionId,
        ValidationDecisionSchemaVersion = value.ValidationDecisionReference?.SchemaVersion,
        ValidationDecisionContentHash = value.ValidationDecisionReference?.ContentHash,
        ValidationState = value.ValidationState,
        LinkedReviewId = value.LinkedReviewId
    };

    public ReviewWorkflowEvent ToApplication()
    {
        var run = CreateExecutionReference();
        var handoff = CreateHandoffReference();
        var validation = CreateValidationReference();
        return new ReviewWorkflowEvent(
            EventId,
            ProjectId,
            RootReviewId,
            CurrentReviewId,
            Kind,
            OccurredAt,
            FindingId,
            Disposition,
            AuthorityReference,
            AuthorityKind,
            Reason,
            AttemptNumber,
            run,
            handoff,
            EvidenceReferences ?? [],
            validation,
            ValidationState,
            LinkedReviewId);
    }

    private ExecutionRunAuthorityReference? CreateExecutionReference()
    {
        if (!ExecutionRunId.HasValue && !ExecutionRunSchemaVersion.HasValue && ExecutionRunContentHash is null)
            return null;
        if (!ExecutionRunId.HasValue || !ExecutionRunSchemaVersion.HasValue || string.IsNullOrWhiteSpace(ExecutionRunContentHash))
            throw new ArgumentException("Persisted execution reference is incomplete.");
        return new ExecutionRunAuthorityReference(ExecutionRunId.Value, ExecutionRunSchemaVersion.Value, ExecutionRunContentHash);
    }

    private HandoffPackageReference? CreateHandoffReference()
    {
        if (!HandoffPackageId.HasValue && !HandoffPackageSchemaVersion.HasValue && HandoffPackageContentHash is null)
            return null;
        if (!HandoffPackageId.HasValue || !HandoffPackageSchemaVersion.HasValue || string.IsNullOrWhiteSpace(HandoffPackageContentHash))
            throw new ArgumentException("Persisted handoff reference is incomplete.");
        return new HandoffPackageReference(HandoffPackageId.Value, HandoffPackageSchemaVersion.Value, HandoffPackageContentHash);
    }

    private ValidationGateDecisionReference? CreateValidationReference()
    {
        if (!ValidationDecisionId.HasValue && !ValidationDecisionSchemaVersion.HasValue && ValidationDecisionContentHash is null)
            return null;
        if (!ValidationDecisionId.HasValue || !ValidationDecisionSchemaVersion.HasValue || string.IsNullOrWhiteSpace(ValidationDecisionContentHash))
            throw new ArgumentException("Persisted validation reference is incomplete.");
        return new ValidationGateDecisionReference(ValidationDecisionId.Value, ValidationDecisionSchemaVersion.Value, ValidationDecisionContentHash);
    }
}

public sealed class JsonReviewWorkflowStore : IReviewWorkflowStore
{
    private readonly ApplicationDataPaths _paths;
    private readonly JsonlEventStore<ReviewWorkflowEventRecord> _events;
    private readonly ILogger<JsonReviewWorkflowStore> _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public JsonReviewWorkflowStore(
        ApplicationDataPaths paths,
        JsonlEventStore<ReviewWorkflowEventRecord> events,
        ILogger<JsonReviewWorkflowStore> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ReviewWorkflowStoreWriteResult> AppendAsync(ReviewWorkflowEvent value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await ReadAllAsync(value.ProjectId, cancellationToken).ConfigureAwait(false);
            if (existing.Status != HistoryReadStatus.Success)
                return new(ReviewWorkflowStoreWriteStatus.Unavailable, "Review workflow history is incomplete; append was rejected.");
            if (existing.Records.Any(item => item.EventId == value.EventId))
                return new(ReviewWorkflowStoreWriteStatus.DuplicateEvent, "The immutable workflow event id already exists.");

            await _paths.EnsureProjectDirectoriesAsync(value.ProjectId, cancellationToken).ConfigureAwait(false);
            await _events.AppendAsync(
                    _paths.GetProjectReviewWorkflowDirectory(value.ProjectId),
                    value.OccurredAt,
                    ReviewWorkflowEventRecord.FromApplication(value),
                    cancellationToken)
                .ConfigureAwait(false);
            return new(ReviewWorkflowStoreWriteStatus.Created);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Permission denied while appending review workflow event {EventId}", value.EventId);
            return new(ReviewWorkflowStoreWriteStatus.Unavailable, "Review workflow persistence is unavailable.");
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "I/O failure while appending review workflow event {EventId}", value.EventId);
            return new(ReviewWorkflowStoreWriteStatus.Unavailable, "Review workflow persistence is unavailable.");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<HistoryReadResult<ReviewWorkflowEvent>> ReadAllAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
            return new([], HistoryReadStatus.Unavailable, [new(HistoryReadIssueKind.CorruptRecord, "review-workflow", "Project id is required.")]);
        var raw = await _events.ReadAllWithStatusAsync(
                _paths.GetProjectReviewWorkflowDirectory(projectId),
                static value => value.OccurredAt,
                ReviewWorkflowLimits.MaxLifecycleEvents,
                cancellationToken)
            .ConfigureAwait(false);
        var records = new List<ReviewWorkflowEvent>();
        var issues = raw.Issues.ToList();
        foreach (var record in raw.Records)
        {
            if (record.ProjectId != projectId || !string.Equals(record.RecordType, "review-workflow-event", StringComparison.Ordinal))
            {
                issues.Add(new HistoryReadIssue(
                    HistoryReadIssueKind.CorruptRecord,
                    $"{record.OccurredAt.UtcDateTime:yyyy-MM}.jsonl",
                    "A review workflow event failed project or record-type isolation."));
                continue;
            }
            try
            {
                records.Add(record.ToApplication());
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
            {
                _logger.LogWarning(exception, "Skipping invalid review workflow event {EventId}", record.EventId);
                issues.Add(new HistoryReadIssue(HistoryReadIssueKind.CorruptRecord, $"{record.OccurredAt.UtcDateTime:yyyy-MM}.jsonl", "Invalid review workflow event was skipped."));
            }
        }

        var status = raw.Status;
        if (status == HistoryReadStatus.Success && issues.Count > 0) status = HistoryReadStatus.Partial;
        return new HistoryReadResult<ReviewWorkflowEvent>(
            records.OrderBy(value => value.OccurredAt).ThenBy(value => value.EventId).ToArray(), status, issues);
    }
}
