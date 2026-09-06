using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Time;

namespace AIUsageMonitor.Application.Approvals;

public sealed class HumanApprovalService : IHumanApprovalService, IDisposable
{
    private readonly IHumanApprovalStore _store;
    private readonly IHumanOwnerAuthority _ownerAuthority;
    private readonly IHandoffRedactionService _redaction;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public HumanApprovalService(
        IHumanApprovalStore store,
        IHumanOwnerAuthority ownerAuthority,
        IHandoffRedactionService redaction,
        IClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _ownerAuthority = ownerAuthority ?? throw new ArgumentNullException(nameof(ownerAuthority));
        _redaction = redaction ?? throw new ArgumentNullException(nameof(redaction));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<HumanApprovalOperationResult> RequestAsync(
        HumanApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ValidateSafeRequest(request);
            var now = _clock.UtcNow;
            if (request.RequestedAt > now)
                return Invalid("An approval request cannot be dated in the future.");
            if (now >= request.ExpiresAt)
                return new(HumanApprovalMutationStatus.Expired, ErrorMessage: "The approval request is already expired.");

            var read = await _store.ReadAsync(request.ProjectId, request.RequestId, cancellationToken).ConfigureAwait(false);
            if (!read.IsUsable)
                return FromReadFailure(read);
            if (read.Histories.Count > 0)
                return new(HumanApprovalMutationStatus.Duplicate, ErrorMessage: "The approval request identity already exists.");

            var requested = new HumanApprovalEvent(
                Guid.NewGuid(),
                request.ProjectId,
                request.RequestId,
                HumanApprovalEventKind.Requested,
                request.RequestedAt,
                HumanApprovalActorKind.Requester,
                request.RequesterReference,
                request: request);
            return await _store.AppendAsync(requested, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<HumanApprovalOperationResult> EscalateAsync(
        Guid projectId,
        Guid requestId,
        string escalationReference,
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (projectId == Guid.Empty || requestId == Guid.Empty)
                return Invalid("Project and request identifiers are required.");
            ValidateSafeText(escalationReference, nameof(escalationReference));
            var read = await _store.ReadAsync(projectId, requestId, cancellationToken).ConfigureAwait(false);
            if (!read.IsUsable)
                return FromReadFailure(read);
            if (!TryValidateHistory(read.Histories, out var history, out var request, out var terminal, out var escalated, out var error))
                return InvalidHistory(error);
            if (terminal is not null)
                return new(HumanApprovalMutationStatus.AlreadyTerminal, ErrorMessage: "A terminal decision already exists for this request.");
            if (escalated is not null)
                return new(HumanApprovalMutationStatus.Duplicate, ErrorMessage: "An escalation marker already exists for this request.");
            if (_clock.UtcNow >= request.ExpiresAt)
                return new(HumanApprovalMutationStatus.Expired, ErrorMessage: "The approval request has expired.");

            var value = new HumanApprovalEvent(
                Guid.NewGuid(),
                projectId,
                requestId,
                HumanApprovalEventKind.Escalated,
                _clock.UtcNow,
                HumanApprovalActorKind.Automation,
                escalationReference,
                reason: "Owner attention was explicitly requested.");
            return await _store.AppendAsync(value, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public Task<HumanApprovalOperationResult> ApproveAsync(
        HumanApprovalDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        DecideAsync(request, HumanApprovalEventKind.Approved, cancellationToken);

    public Task<HumanApprovalOperationResult> RejectAsync(
        HumanApprovalDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        DecideAsync(request, HumanApprovalEventKind.Rejected, cancellationToken);

    public Task<HumanApprovalOperationResult> WaiveAsync(
        HumanApprovalDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        DecideAsync(request, HumanApprovalEventKind.Waived, cancellationToken);

    public async Task<HumanApprovalEvaluation> EvaluateAsync(
        HumanApprovalEvaluationContext context,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request id cannot be empty.", nameof(requestId));

        var read = await _store.ReadAsync(context.ProjectId, requestId, cancellationToken).ConfigureAwait(false);
        if (!read.IsUsable)
        {
            return new(
                context.ProjectId,
                requestId,
                HumanApprovalState.Stale,
                false,
                HumanApprovalReasonCode.InvalidHistory,
                HumanApprovalNextAction.CreateFreshApprovalRequest,
                false,
                reason: read.ErrorMessage ?? "Approval history is unavailable or incomplete.");
        }

        if (read.Histories.Count == 0)
        {
            return new(
                context.ProjectId,
                requestId,
                HumanApprovalState.Stale,
                false,
                HumanApprovalReasonCode.RequestNotFound,
                HumanApprovalNextAction.RequestApproval,
                true,
                reason: "The approval request does not exist.");
        }

        if (!TryValidateHistory(read.Histories, out _, out var request, out var terminal, out var escalated, out var error))
        {
            return new(
                context.ProjectId,
                requestId,
                HumanApprovalState.Stale,
                false,
                HumanApprovalReasonCode.InvalidHistory,
                HumanApprovalNextAction.CreateFreshApprovalRequest,
                false,
                reason: error);
        }

        return EvaluateValidHistory(context, request, terminal, escalated, _clock.UtcNow);
    }

    public async Task<HumanApprovalInboxReadResult> ReadInboxAsync(
        Guid projectId,
        IReadOnlyDictionary<Guid, HumanApprovalEvaluationContext>? currentContexts = null,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project id cannot be empty.", nameof(projectId));

        var read = await _store.ReadProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!read.IsUsable)
            return new(read.Status, errorMessage: read.ErrorMessage);

        var items = new List<HumanApprovalInboxItem>(read.Histories.Count);
        foreach (var history in read.Histories)
        {
            if (!TryValidateHistory([history], out _, out var request, out var terminal, out var escalated, out var error))
                return new(HumanApprovalHistoryReadStatus.Corrupt, errorMessage: error);

            var contextKnown = false;
            HumanApprovalEvaluationContext? currentContext = null;
            if (currentContexts is not null && currentContexts.TryGetValue(request.RequestId, out var suppliedContext))
            {
                contextKnown = true;
                currentContext = suppliedContext;
            }

            currentContext ??= new HumanApprovalEvaluationContext(
                    request.ProjectId,
                    request.ContractReference,
                    request.Target,
                    request.EvidenceRevision);
            var evaluation = EvaluateValidHistory(currentContext, request, terminal, escalated, _clock.UtcNow);
            var decision = terminal;
            items.Add(new HumanApprovalInboxItem
            {
                ProjectId = request.ProjectId,
                RequestId = request.RequestId,
                ActionKind = request.ActionKind,
                RequestedAt = request.RequestedAt,
                ExpiresAt = request.ExpiresAt,
                EffectiveState = evaluation.EffectiveState,
                OwnerAttentionRequired = evaluation.OwnerAttentionRequired,
                RequesterReference = request.RequesterReference,
                DecisionActorReference = decision?.ActorReference,
                DecisionTimestamp = decision?.OccurredAt,
                SafeTargetSummary = request.Target.SafeSummary,
                TargetFingerprint = request.Target.ContentHash,
                EvidenceRevisionHash = request.EvidenceRevision.ContentHash,
                CurrentContextKnown = contextKnown,
                IsStale = evaluation.ReasonCode is HumanApprovalReasonCode.StaleContract or HumanApprovalReasonCode.StaleTarget or HumanApprovalReasonCode.StaleEvidence,
                NextRequiredAction = evaluation.NextAction,
                SatisfyingApprovalReference = evaluation.SatisfyingReference
            });
        }

        return new(HumanApprovalHistoryReadStatus.Success, items.OrderByDescending(static item => item.RequestedAt).ToArray());
    }

    private async Task<HumanApprovalOperationResult> DecideAsync(
        HumanApprovalDecisionRequest decision,
        HumanApprovalEventKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ValidateSafeText(decision.Reason, nameof(decision.Reason));
            if (!_ownerAuthority.IsAuthorized(decision.OwnerAuthority))
                return new(HumanApprovalMutationStatus.Unauthorized, ErrorMessage: "The supplied authority is not the configured human owner.");

            var read = await _store.ReadAsync(decision.ProjectId, decision.RequestId, cancellationToken).ConfigureAwait(false);
            if (!read.IsUsable)
                return FromReadFailure(read);
            if (!TryValidateHistory(read.Histories, out _, out var request, out var terminal, out _, out var error))
                return InvalidHistory(error);
            if (terminal is not null)
                return new(HumanApprovalMutationStatus.AlreadyTerminal, ErrorMessage: "A request may have only one terminal human decision.");

            var bindingReason = CompareBinding(request, decision.CurrentContext);
            if (bindingReason is not null)
                return new(HumanApprovalMutationStatus.Stale, ErrorMessage: bindingReason);

            var now = _clock.UtcNow;
            if (now < request.RequestedAt)
                return Invalid("A decision cannot precede the request timestamp.");
            if (now >= request.ExpiresAt)
                return new(HumanApprovalMutationStatus.Expired, ErrorMessage: "The approval request has expired.");

            var value = new HumanApprovalEvent(
                Guid.NewGuid(),
                request.ProjectId,
                request.RequestId,
                kind,
                now,
                HumanApprovalActorKind.HumanOwner,
                decision.OwnerAuthority.OwnerReference,
                decision.Reason);
            return await _store.AppendAsync(value, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private static HumanApprovalEvaluation EvaluateValidHistory(
        HumanApprovalEvaluationContext context,
        HumanApprovalRequest request,
        HumanApprovalEvent? terminal,
        HumanApprovalEvent? escalated,
        DateTimeOffset now)
    {
        var bindingReason = CompareBinding(request, context);
        if (now >= request.ExpiresAt)
        {
            return new(
                request.ProjectId,
                request.RequestId,
                HumanApprovalState.Expired,
                false,
                HumanApprovalReasonCode.Expired,
                HumanApprovalNextAction.CreateFreshApprovalRequest,
                false,
                request,
                reason: "The approval request is expired.");
        }

        if (bindingReason is not null)
        {
            var code = bindingReason.StartsWith("contract", StringComparison.Ordinal)
                ? HumanApprovalReasonCode.StaleContract
                : bindingReason.StartsWith("target", StringComparison.Ordinal)
                    ? HumanApprovalReasonCode.StaleTarget
                    : HumanApprovalReasonCode.StaleEvidence;
            return new(
                request.ProjectId,
                request.RequestId,
                HumanApprovalState.Stale,
                false,
                code,
                HumanApprovalNextAction.CreateFreshApprovalRequest,
                true,
                request,
                reason: bindingReason);
        }

        if (terminal is not null)
        {
            var approved = terminal.Kind == HumanApprovalEventKind.Approved;
            var waived = terminal.Kind == HumanApprovalEventKind.Waived;
            return new(
                request.ProjectId,
                request.RequestId,
                approved ? HumanApprovalState.Approved : waived ? HumanApprovalState.Waived : HumanApprovalState.Rejected,
                approved || waived,
                approved ? HumanApprovalReasonCode.ExactApproved : waived ? HumanApprovalReasonCode.ExactWaived : HumanApprovalReasonCode.Rejected,
                approved || waived ? HumanApprovalNextAction.ProceedWithAuthorizedAction : HumanApprovalNextAction.ResolveRejection,
                false,
                request,
                approved || waived ? terminal.Reference : null,
                terminal.Reason);
        }

        return new(
            request.ProjectId,
            request.RequestId,
            escalated is null ? HumanApprovalState.Pending : HumanApprovalState.Escalated,
            false,
            escalated is null ? HumanApprovalReasonCode.Pending : HumanApprovalReasonCode.Escalated,
            HumanApprovalNextAction.AwaitOwnerDecision,
            true,
            request,
            reason: escalated is null ? "Owner approval is pending." : "Owner attention was escalated.");
    }

    private static bool TryValidateHistory(
        IReadOnlyList<HumanApprovalHistory> histories,
        out HumanApprovalHistory? history,
        out HumanApprovalRequest request,
        out HumanApprovalEvent? terminal,
        out HumanApprovalEvent? escalated,
        out string error)
    {
        history = null;
        request = null!;
        terminal = null;
        escalated = null;
        error = "Approval history is invalid.";
        if (histories.Count != 1 || histories[0].Events.Count == 0)
            return false;

        var candidateHistory = histories[0];
        history = candidateHistory;
        if (candidateHistory.ProjectId == Guid.Empty || candidateHistory.RequestId == Guid.Empty ||
            candidateHistory.Events.Any(value => value is null || value.ProjectId != candidateHistory.ProjectId || value.RequestId != candidateHistory.RequestId))
            return false;
        if (candidateHistory.Events.Select(static value => value.EventId).Distinct().Count() != candidateHistory.Events.Count)
        {
            error = "Approval history contains a duplicate event identity.";
            return false;
        }

        // JSONL order is the append order within the authoritative history. Preserve it so two
        // events with the same timestamp cannot be reordered by a non-authoritative tie-breaker.
        var events = candidateHistory.Events.ToArray();
        var requested = events.Where(static value => value.Kind == HumanApprovalEventKind.Requested).ToArray();
        if (requested.Length != 1 || events[0].Kind != HumanApprovalEventKind.Requested || requested[0].Request is null)
        {
            error = "Approval history must contain exactly one first Requested event.";
            return false;
        }

        var requestedRequest = requested[0].Request;
        if (requestedRequest is null)
        {
            error = "The Requested event has no immutable request payload.";
            return false;
        }

        request = requestedRequest;
        var requestedAt = requestedRequest.RequestedAt;
        var expiresAt = requestedRequest.ExpiresAt;
        if (requestedRequest.ProjectId != candidateHistory.ProjectId || requestedRequest.RequestId != candidateHistory.RequestId ||
            requested[0].OccurredAt != requestedAt ||
            events.Any(value => value.OccurredAt < requestedAt || value.OccurredAt >= expiresAt))
        {
            error = "Approval history contains an event outside the immutable request time window.";
            return false;
        }

        if (events.Zip(events.Skip(1), static (left, right) => left.OccurredAt <= right.OccurredAt).Any(static valid => !valid))
        {
            error = "Approval history event order is invalid.";
            return false;
        }

        if (events.Count(value => value.Kind is HumanApprovalEventKind.Approved or HumanApprovalEventKind.Rejected or HumanApprovalEventKind.Waived) > 1)
        {
            error = "Approval history contains conflicting terminal decisions.";
            return false;
        }
        var terminalEvent = events.FirstOrDefault(value => value.Kind is HumanApprovalEventKind.Approved or HumanApprovalEventKind.Rejected or HumanApprovalEventKind.Waived);
        terminal = terminalEvent;

        if (events.Count(static value => value.Kind == HumanApprovalEventKind.Escalated) > 1)
        {
            error = "Approval history contains duplicate escalation markers.";
            return false;
        }
        escalated = events.FirstOrDefault(static value => value.Kind == HumanApprovalEventKind.Escalated);

        var terminalIndex = terminalEvent is null ? -1 : Array.IndexOf(events, terminalEvent);
        if (terminalIndex >= 0 && events.Skip(terminalIndex + 1).Any(static _ => true))
        {
            error = "Approval history contains events after a terminal decision.";
            return false;
        }

        if (events.Any(value => value.Kind is not HumanApprovalEventKind.Requested and not HumanApprovalEventKind.Escalated and not HumanApprovalEventKind.Approved and not HumanApprovalEventKind.Rejected and not HumanApprovalEventKind.Waived))
        {
            error = "Approval history contains an unknown event kind.";
            return false;
        }

        return true;
    }

    private void ValidateSafeRequest(HumanApprovalRequest request)
    {
        ValidateSafeText(request.RequesterReference, nameof(request.RequesterReference));
        ValidateSafeText(request.Reason, nameof(request.Reason));
        ValidateSafeText(request.PolicyReference, nameof(request.PolicyReference));
        ValidateSafeText(request.Target.SafeSummary, nameof(request.Target.SafeSummary));
        ValidateOptionalSafeText(request.Target.CanonicalRepositoryIdentity, nameof(request.Target.CanonicalRepositoryIdentity));
        ValidateOptionalSafeText(request.Target.BaseRef, nameof(request.Target.BaseRef));
        ValidateOptionalSafeText(request.Target.HeadRef, nameof(request.Target.HeadRef));
        foreach (var evidence in request.EvidenceRevision.References)
        {
            ValidateSafeText(evidence.Kind, nameof(evidence.Kind));
            ValidateSafeText(evidence.Reference, nameof(evidence.Reference));
        }
    }

    private void ValidateSafeText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A bounded value is required.", parameterName);
        if (value.Length > HumanApprovalLimits.MaxTextLength)
            throw new ArgumentException($"The value cannot exceed {HumanApprovalLimits.MaxTextLength} characters.", parameterName);
        var inspection = _redaction.ValidateIdentityText(value);
        if (inspection.RequiresRedaction)
            throw new ArgumentException("Approval metadata crossed the secret-redaction boundary.", parameterName);
    }

    private void ValidateOptionalSafeText(string? value, string parameterName)
    {
        if (value is not null)
            ValidateSafeText(value, parameterName);
    }

    private static string? CompareBinding(HumanApprovalRequest request, HumanApprovalEvaluationContext context)
    {
        if (context.ProjectId != request.ProjectId)
            return "target project identity is stale.";
        if (!SameContract(request.ContractReference, context.ContractReference))
            return "contract binding is stale.";
        if (request.Target.ActionKind != context.Target.ActionKind ||
            !string.Equals(request.Target.ContentHash, context.Target.ContentHash, StringComparison.Ordinal))
            return "target binding is stale.";
        if (!string.Equals(request.EvidenceRevision.ContentHash, context.EvidenceRevision.ContentHash, StringComparison.Ordinal) ||
            request.EvidenceRevision.SchemaVersion != context.EvidenceRevision.SchemaVersion)
            return "evidence binding is stale.";
        return null;
    }

    private static bool SameContract(
        PlanningExecutionContractReference left,
        PlanningExecutionContractReference right) =>
        left.ContractId == right.ContractId &&
        left.Revision == right.Revision &&
        left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.Ordinal);

    private static HumanApprovalOperationResult FromReadFailure(HumanApprovalStoreReadResult result) =>
        result.Status == HumanApprovalHistoryReadStatus.Missing
            ? new(HumanApprovalMutationStatus.NotFound, ErrorMessage: result.ErrorMessage ?? "Approval request was not found.")
            : new(HumanApprovalMutationStatus.Unavailable, ErrorMessage: result.ErrorMessage ?? "Approval history is unavailable.");

    private static HumanApprovalOperationResult Invalid(string message) =>
        new(HumanApprovalMutationStatus.InvalidRequest, ErrorMessage: message);

    private static HumanApprovalOperationResult InvalidHistory(string message) =>
        new(HumanApprovalMutationStatus.Unavailable, ErrorMessage: message);

    public void Dispose() => _mutationGate.Dispose();
}
