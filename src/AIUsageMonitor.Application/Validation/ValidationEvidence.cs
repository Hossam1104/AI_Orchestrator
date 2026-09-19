using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageMonitor.Application.Handoffs;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Planning;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Time;
using AIUsageMonitor.Application.Workspaces;

namespace AIUsageMonitor.Application.Validation;

public static class ValidationSchema
{
    public const int CurrentVersion = 1;
}

public static class ValidationLimits
{
    public const int MaxRequirements = 64;
    public const int MaxEvidenceItems = 128;
    public const int MaxSupportingEvidenceReferences = 32;
    public const int MaxDiagnosticLength = 2_000;
    public const int MaxCanonicalPayloadBytes = 128 * 1024;
    public const int MaxIdentityLength = 2_000;
    public static readonly TimeSpan MaxCommandTimeout = TimeSpan.FromHours(24);
}

public enum ValidationEvidenceKind
{
    Build,
    Test,
    LocalRepository,
    RemoteRepository,
    RemoteCi,
    Tracker,
    Security,
    Runtime,
    Other
}

public enum ValidationEvidenceState
{
    Available,
    Missing,
    Partial,
    Stale,
    Unavailable,
    Unsupported,
    AuthenticationRequired,
    PermissionDenied,
    RateLimited,
    Invalid,
    Cancelled,
    TimedOut,
    RedactionRejected,
    ConfigurationConflict
}

public enum ValidationOutcome
{
    Passed,
    Failed,
    Unknown,
    NotApplicable
}

public enum ValidationCoverageScope
{
    Targeted,
    Full
}

public enum ValidationBaselineRelation
{
    Standalone,
    Baseline,
    Regression
}

public enum ValidationGateDecisionState
{
    Satisfied,
    Failed,
    Pending,
    Stale,
    Blocked
}

public enum ValidationRequirementDecisionState
{
    Satisfied,
    Failed,
    Missing,
    Stale,
    Blocked,
    NotApplicable
}

public static class ValidationReasonCodes
{
    public const string Satisfied = "Satisfied";
    public const string RequiredEvidenceMissing = "RequiredEvidenceMissing";
    public const string EvidenceFailed = "EvidenceFailed";
    public const string EvidenceNotUsable = "EvidenceNotUsable";
    public const string EvidenceStale = "EvidenceStale";
    public const string EvidenceBeforeExecution = "EvidenceBeforeExecutionAuthority";
    public const string TargetedEvidenceForFullRequirement = "TargetedEvidenceCannotSatisfyFullRequirement";
    public const string BaselineMissing = "BaselineMissingOrInvalid";
    public const string IdentityMismatch = "EvidenceIdentityMismatch";
    public const string RepositoryMismatch = "RepositoryIdentityMismatch";
    public const string TrackerMismatch = "TrackerIdentityMismatch";
    public const string SecurityBoundaryInvalid = "SecurityBoundaryInvalid";
    public const string SecurityEvidenceSetMismatch = "SecurityEvidenceSetMismatch";
    public const string AuthorityMismatch = "ValidationAuthorityMismatch";
    public const string EvidenceTimestampInFuture = "EvidenceTimestampInFuture";
    public const string EvidenceBeforeValidationEpoch = "EvidenceBeforeValidationEpoch";
    public const string ProviderEvidenceStale = "ProviderEvidenceStale";
    public const string RemoteCiCommitIdentityMissing = "RemoteCiCommitIdentityMissing";
    public const string RemoteCiCommitConflict = "RemoteCiCommitConflict";
}

public sealed class ValidationPlanReference
{
    public ValidationPlanReference(Guid projectId, Guid planId, int revision, int schemaVersion, string contentHash)
    {
        if (projectId == Guid.Empty || planId == Guid.Empty)
            throw new ArgumentException("Project and validation-plan identifiers are required.");
        if (revision <= 0 || schemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        if (!IsSha256(contentHash))
            throw new ArgumentException("Validation-plan content hash must be SHA-256 evidence.", nameof(contentHash));

        ProjectId = projectId;
        PlanId = planId;
        Revision = revision;
        SchemaVersion = schemaVersion;
        ContentHash = contentHash.ToLowerInvariant();
    }

    public Guid ProjectId { get; }
    public Guid PlanId { get; }
    public int Revision { get; }
    public int SchemaVersion { get; }
    public string ContentHash { get; }

    public override string ToString() =>
        $"validation-plan:{ProjectId:D}/{PlanId:D}/revision:{Revision}/sha256:{ContentHash}";

    public static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public sealed class ValidationEvidenceReference
{
    public ValidationEvidenceReference(Guid evidenceId, int schemaVersion, string contentHash)
    {
        if (evidenceId == Guid.Empty)
            throw new ArgumentException("Evidence id cannot be empty.", nameof(evidenceId));
        if (schemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (!ValidationPlanReference.IsSha256(contentHash))
            throw new ArgumentException("Evidence content hash must be SHA-256 evidence.", nameof(contentHash));

        EvidenceId = evidenceId;
        SchemaVersion = schemaVersion;
        ContentHash = contentHash.ToLowerInvariant();
    }

    public Guid EvidenceId { get; }
    public int SchemaVersion { get; }
    public string ContentHash { get; }

    public override string ToString() =>
        $"validation-evidence:{EvidenceId:D}/schema:{SchemaVersion}/sha256:{ContentHash}";
}

public sealed class ValidationBaselineBinding
{
    public ValidationBaselineBinding(
        ValidationPlanReference planReference,
        ValidationEvidenceReference evidenceReference,
        string validationDefinitionId)
    {
        PlanReference = planReference ?? throw new ArgumentNullException(nameof(planReference));
        EvidenceReference = evidenceReference ?? throw new ArgumentNullException(nameof(evidenceReference));
        ValidationDefinitionId = string.IsNullOrWhiteSpace(validationDefinitionId) || validationDefinitionId.Trim().Length > 200
            ? throw new ArgumentException("A bounded validation-definition identity is required.", nameof(validationDefinitionId))
            : validationDefinitionId.Trim();
    }

    public ValidationPlanReference PlanReference { get; }
    public ValidationEvidenceReference EvidenceReference { get; }
    public string ValidationDefinitionId { get; }
}

public sealed class ValidationGateDecisionReference
{
    public ValidationGateDecisionReference(Guid decisionId, int schemaVersion, string contentHash)
    {
        if (decisionId == Guid.Empty)
            throw new ArgumentException("Decision id cannot be empty.", nameof(decisionId));
        if (schemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (!ValidationPlanReference.IsSha256(contentHash))
            throw new ArgumentException("Decision content hash must be SHA-256 evidence.", nameof(contentHash));

        DecisionId = decisionId;
        SchemaVersion = schemaVersion;
        ContentHash = contentHash.ToLowerInvariant();
    }

    public Guid DecisionId { get; }
    public int SchemaVersion { get; }
    public string ContentHash { get; }

    public override string ToString() =>
        $"validation-decision:{DecisionId:D}/schema:{SchemaVersion}/sha256:{ContentHash}";
}

public sealed class ValidationRequirement
{
    public ValidationRequirement(
        string requirementId,
        ValidationEvidenceKind evidenceKind,
        bool required,
        ValidationCoverageScope coverage,
        ValidationBaselineRelation baselineRelation,
        string collectorIdentifier,
        TimeSpan? maxAge = null,
        bool allowFullEvidenceForTargeted = false,
        string? targetPath = null,
        string? testFilter = null,
        TimeSpan? timeout = null,
        string? expectedLocalHeadCommitSha = null,
        string? expectedBranchName = null,
        bool? requireCleanWorktree = null,
        string? expectedRepositoryIdentity = null,
        string? expectedRemoteCommitId = null,
        string? expectedTrackerProjectId = null,
        string? expectedTrackerWorkItemKey = null,
        string? expectedTrackerStatus = null,
        ValidationEvidenceState? expectedState = null,
        ValidationOutcome? expectedOutcome = null,
        string? requestedBranch = null,
        int? pullRequestNumber = null,
        string? validationDefinitionId = null,
        ValidationBaselineBinding? baselineBinding = null)
    {
        RequirementId = RequiredText(requirementId, nameof(requirementId), 200);
        if (!Enum.IsDefined(evidenceKind) || !Enum.IsDefined(coverage) || !Enum.IsDefined(baselineRelation))
            throw new ArgumentException("Validation requirement enum values are invalid.");
        CollectorIdentifier = RequiredText(collectorIdentifier, nameof(collectorIdentifier), 200);
        if (maxAge is not null && maxAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxAge));
        if ((timeout is not null && timeout <= TimeSpan.Zero) || timeout > ValidationLimits.MaxCommandTimeout)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (pullRequestNumber is <= 0)
            throw new ArgumentOutOfRangeException(nameof(pullRequestNumber));
        if (expectedState is not null && !Enum.IsDefined(expectedState.Value))
            throw new ArgumentException("Expected evidence state is invalid.", nameof(expectedState));
        if (expectedOutcome is not null && !Enum.IsDefined(expectedOutcome.Value))
            throw new ArgumentException("Expected validation outcome is invalid.", nameof(expectedOutcome));

        Required = required;
        EvidenceKind = evidenceKind;
        Coverage = coverage;
        BaselineRelation = baselineRelation;
        MaxAge = maxAge;
        AllowFullEvidenceForTargeted = allowFullEvidenceForTargeted;
        TargetPath = Optional(targetPath, nameof(targetPath), ValidationLimits.MaxIdentityLength);
        TestFilter = Optional(testFilter, nameof(testFilter), ValidationLimits.MaxIdentityLength);
        Timeout = timeout;
        ExpectedLocalHeadCommitSha = Optional(expectedLocalHeadCommitSha, nameof(expectedLocalHeadCommitSha), 200);
        ExpectedBranchName = Optional(expectedBranchName, nameof(expectedBranchName), 500);
        RequireCleanWorktree = requireCleanWorktree;
        ExpectedRepositoryIdentity = Optional(expectedRepositoryIdentity, nameof(expectedRepositoryIdentity), ValidationLimits.MaxIdentityLength);
        ExpectedRemoteCommitId = Optional(expectedRemoteCommitId, nameof(expectedRemoteCommitId), 500);
        ExpectedTrackerProjectId = Optional(expectedTrackerProjectId, nameof(expectedTrackerProjectId), 500);
        ExpectedTrackerWorkItemKey = Optional(expectedTrackerWorkItemKey, nameof(expectedTrackerWorkItemKey), 500);
        ExpectedTrackerStatus = Optional(expectedTrackerStatus, nameof(expectedTrackerStatus), 500);
        ExpectedState = expectedState;
        ExpectedOutcome = expectedOutcome;
        RequestedBranch = Optional(requestedBranch, nameof(requestedBranch), 500);
        PullRequestNumber = pullRequestNumber;
        if (baselineRelation == ValidationBaselineRelation.Regression && baselineBinding is null)
            throw new ArgumentException("Regression requirements must bind an exact immutable baseline.", nameof(baselineBinding));
        if (baselineRelation != ValidationBaselineRelation.Regression && baselineBinding is not null)
            throw new ArgumentException("Only regression requirements may carry a baseline binding.", nameof(baselineBinding));
        ValidationDefinitionId = Optional(validationDefinitionId, nameof(validationDefinitionId), 200) ??
            ValidationDefinitionIdentity.Compute(this);
        BaselineBinding = baselineBinding;
    }

    public string RequirementId { get; }
    public ValidationEvidenceKind EvidenceKind { get; }
    public bool Required { get; }
    public ValidationCoverageScope Coverage { get; }
    public ValidationBaselineRelation BaselineRelation { get; }
    public string CollectorIdentifier { get; }
    public TimeSpan? MaxAge { get; }
    public bool AllowFullEvidenceForTargeted { get; }
    public string? TargetPath { get; }
    public string? TestFilter { get; }
    public TimeSpan? Timeout { get; }
    public string? ExpectedLocalHeadCommitSha { get; }
    public string? ExpectedBranchName { get; }
    public bool? RequireCleanWorktree { get; }
    public string? ExpectedRepositoryIdentity { get; }
    public string? ExpectedRemoteCommitId { get; }
    public string? ExpectedTrackerProjectId { get; }
    public string? ExpectedTrackerWorkItemKey { get; }
    public string? ExpectedTrackerStatus { get; }
    public ValidationEvidenceState? ExpectedState { get; }
    public ValidationOutcome? ExpectedOutcome { get; }
    public string? RequestedBranch { get; }
    public int? PullRequestNumber { get; }
    public string ValidationDefinitionId { get; }
    public ValidationBaselineBinding? BaselineBinding { get; }

    private static string RequiredText(string value, string parameterName, int max) =>
        Optional(value, parameterName, max) ?? throw new ArgumentException("A bounded value is required.", parameterName);

    private static string? Optional(string? value, string parameterName, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= max
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {max} characters.", parameterName);
    }
}

public static class ValidationDefinitionIdentity
{
    public static string Compute(ValidationRequirement value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var payload = new
        {
            value.EvidenceKind, value.Coverage, value.CollectorIdentifier, maxAgeSeconds = value.MaxAge?.TotalSeconds,
            value.AllowFullEvidenceForTargeted, value.TargetPath, value.TestFilter, timeoutSeconds = value.Timeout?.TotalSeconds,
            value.ExpectedBranchName, value.RequireCleanWorktree, value.ExpectedRepositoryIdentity,
            value.ExpectedTrackerProjectId, value.ExpectedTrackerWorkItemKey, value.ExpectedTrackerStatus,
            value.ExpectedState, value.ExpectedOutcome, value.RequestedBranch, value.PullRequestNumber
        };
        return "validation-definition:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))).ToLowerInvariant();
    }
}

public static class ValidationRequirementCompatibility
{
    public static bool AreCompatible(ValidationRequirement left, ValidationRequirement right) =>
        string.Equals(left.ValidationDefinitionId, right.ValidationDefinitionId, StringComparison.Ordinal) &&
        left.EvidenceKind == right.EvidenceKind && left.Coverage == right.Coverage &&
        left.AllowFullEvidenceForTargeted == right.AllowFullEvidenceForTargeted &&
        string.Equals(left.CollectorIdentifier, right.CollectorIdentifier, StringComparison.Ordinal) &&
        left.MaxAge == right.MaxAge && string.Equals(left.TargetPath, right.TargetPath, StringComparison.Ordinal) &&
        string.Equals(left.TestFilter, right.TestFilter, StringComparison.Ordinal) && left.Timeout == right.Timeout &&
        string.Equals(left.ExpectedBranchName, right.ExpectedBranchName, StringComparison.Ordinal) &&
        left.RequireCleanWorktree == right.RequireCleanWorktree &&
        string.Equals(left.ExpectedRepositoryIdentity, right.ExpectedRepositoryIdentity, StringComparison.Ordinal) &&
        string.Equals(left.ExpectedTrackerProjectId, right.ExpectedTrackerProjectId, StringComparison.Ordinal) &&
        string.Equals(left.ExpectedTrackerWorkItemKey, right.ExpectedTrackerWorkItemKey, StringComparison.Ordinal) &&
        string.Equals(left.ExpectedTrackerStatus, right.ExpectedTrackerStatus, StringComparison.Ordinal) &&
        left.ExpectedState == right.ExpectedState && left.ExpectedOutcome == right.ExpectedOutcome &&
        string.Equals(left.RequestedBranch, right.RequestedBranch, StringComparison.Ordinal) &&
        left.PullRequestNumber == right.PullRequestNumber;
}

public sealed class ValidationPlan
{
    public ValidationPlan(
        Guid projectId,
        Guid planId,
        int revision,
        DateTimeOffset createdAt,
        ExecutionRunAuthorityReference executionRunAuthorityReference,
        PlanningExecutionContractReference planningContractReference,
        WorkGraphReference workGraphReference,
        Guid workGraphNodeId,
        Guid workspaceId,
        string workspacePath,
        string workspaceReceiptContentHash,
        RecoveryCheckpointReference currentRecoveryCheckpointReference,
        IReadOnlyList<ValidationRequirement> requirements,
        HandoffPackageReference? handoffPackageReference = null,
        int schemaVersion = ValidationSchema.CurrentVersion,
        string? contentHash = null,
        DateTimeOffset? evidenceNotBefore = null)
    {
        if (projectId == Guid.Empty || planId == Guid.Empty || workspaceId == Guid.Empty)
            throw new ArgumentException("Project, plan, and workspace identifiers are required.");
        if (revision <= 0 || schemaVersion != ValidationSchema.CurrentVersion)
            throw new ArgumentException("Only the current validation-plan schema is supported.");
        if (createdAt == default)
            throw new ArgumentException("Validation-plan creation time is required.", nameof(createdAt));
        if (evidenceNotBefore is { } suppliedNotBefore && (suppliedNotBefore == default || suppliedNotBefore > createdAt))
            throw new ArgumentException("Validation evidence lower boundary is invalid.", nameof(evidenceNotBefore));
        ExecutionRunAuthorityReference = executionRunAuthorityReference ?? throw new ArgumentNullException(nameof(executionRunAuthorityReference));
        PlanningContractReference = planningContractReference ?? throw new ArgumentNullException(nameof(planningContractReference));
        WorkGraphReference = workGraphReference ?? throw new ArgumentNullException(nameof(workGraphReference));
        if (workGraphNodeId == Guid.Empty)
            throw new ArgumentException("A work-graph node is required.", nameof(workGraphNodeId));
        WorkspacePath = Required(workspacePath, nameof(workspacePath), ValidationLimits.MaxIdentityLength);
        if (!Path.IsPathFullyQualified(WorkspacePath))
            throw new ArgumentException("Validation workspace path must be absolute.", nameof(workspacePath));
        if (!ValidationPlanReference.IsSha256(workspaceReceiptContentHash))
            throw new ArgumentException("Workspace receipt content hash must be SHA-256 evidence.", nameof(workspaceReceiptContentHash));
        CurrentRecoveryCheckpointReference = currentRecoveryCheckpointReference ?? throw new ArgumentNullException(nameof(currentRecoveryCheckpointReference));
        var values = requirements?.ToArray() ?? throw new ArgumentNullException(nameof(requirements));
        if (values.Length == 0 || values.Length > ValidationLimits.MaxRequirements || values.Any(static value => value is null) ||
            !values.Any(static value => value.Required) ||
            values.GroupBy(static value => value.RequirementId, StringComparer.Ordinal).Any(static group => group.Count() > 1))
            throw new ArgumentException("Validation requirements are empty, all optional, duplicated, or exceed the supported bound.", nameof(requirements));

        ProjectId = projectId;
        PlanId = planId;
        Revision = revision;
        SchemaVersion = schemaVersion;
        CreatedAt = createdAt;
        EvidenceNotBefore = evidenceNotBefore ?? createdAt;
        WorkGraphNodeId = workGraphNodeId;
        WorkspaceId = workspaceId;
        WorkspaceReceiptContentHash = workspaceReceiptContentHash.ToLowerInvariant();
        Requirements = values.OrderBy(static value => value.RequirementId, StringComparer.Ordinal).ToArray();
        HandoffPackageReference = handoffPackageReference;
        ContentHash = string.Empty;
        var calculatedHash = ValidationIntegrity.ComputePlanHash(this);
        if (contentHash is not null && (!ValidationPlanReference.IsSha256(contentHash) || !string.Equals(contentHash, calculatedHash, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Validation-plan content hash does not match its payload.", nameof(contentHash));
        ContentHash = calculatedHash;
        if (ValidationIntegrity.ComputePlanPayloadBytes(this) > ValidationLimits.MaxCanonicalPayloadBytes)
            throw new ArgumentException("Validation-plan payload exceeds its supported bound.", nameof(requirements));
        Reference = new ValidationPlanReference(ProjectId, PlanId, Revision, SchemaVersion, ContentHash);
    }

    public Guid ProjectId { get; }
    public Guid PlanId { get; }
    public int Revision { get; }
    public int SchemaVersion { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset EvidenceNotBefore { get; }
    public ExecutionRunAuthorityReference ExecutionRunAuthorityReference { get; }
    public PlanningExecutionContractReference PlanningContractReference { get; }
    public WorkGraphReference WorkGraphReference { get; }
    public Guid WorkGraphNodeId { get; }
    public HandoffPackageReference? HandoffPackageReference { get; }
    public Guid WorkspaceId { get; }
    public string WorkspacePath { get; }
    public string WorkspaceReceiptContentHash { get; }
    public RecoveryCheckpointReference CurrentRecoveryCheckpointReference { get; }
    public IReadOnlyList<ValidationRequirement> Requirements { get; }
    public string ContentHash { get; private set; }
    public ValidationPlanReference Reference { get; private set; }

    private static string Required(string value, string parameterName, int max) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) :
        value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"The value cannot exceed {max} characters.", parameterName);
}

public sealed record ValidationAuthorityBindingResult(bool IsValid, DateTimeOffset NotBefore, string? ErrorMessage = null);

public static class ValidationAuthorityBindingValidator
{
    public static ValidationAuthorityBindingResult Validate(
        ValidationPlan plan,
        ExecutionRunAuthority authority,
        WorkspacePreparationReceipt receipt,
        RecoveryCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(checkpoint);

        var notBefore = new[] { plan.CreatedAt, authority.CreatedAt, receipt.PreparedAt, checkpoint.CreatedAt }.Max();
        var valid = authority.ProjectId == plan.ProjectId &&
            Same(authority.Reference, plan.ExecutionRunAuthorityReference) &&
            Same(authority.PlanningContractReference, plan.PlanningContractReference) &&
            Same(authority.WorkGraphReference, plan.WorkGraphReference) &&
            authority.WorkGraphNodeId == plan.WorkGraphNodeId &&
            Same(authority.HandoffPackageReference, plan.HandoffPackageReference) &&
            !Same(authority.InputRecoveryCheckpointReference, checkpoint.Reference) &&
            authority.WorkspaceId == plan.WorkspaceId &&
            string.Equals(authority.WorkspacePath, plan.WorkspacePath, StringComparison.Ordinal) &&
            string.Equals(authority.WorkspaceReceiptContentHash, plan.WorkspaceReceiptContentHash, StringComparison.OrdinalIgnoreCase) &&
            receipt.ProjectId == plan.ProjectId && receipt.WorkspaceId == plan.WorkspaceId &&
            string.Equals(receipt.WorkspacePath, plan.WorkspacePath, StringComparison.Ordinal) &&
            string.Equals(receipt.ContentHash, plan.WorkspaceReceiptContentHash, StringComparison.OrdinalIgnoreCase) &&
            checkpoint.ProjectId == plan.ProjectId &&
            Same(checkpoint.Reference, plan.CurrentRecoveryCheckpointReference) &&
            checkpoint.PreviousCheckpointReference is not null &&
            SameCheckpointAuthorities(checkpoint, plan) &&
            checkpoint.LifecycleState == RecoveryCheckpointLifecycleState.Ready &&
            checkpoint.NextSafeAction == RecoveryNextSafeAction.RunValidation &&
            MatchesReference(checkpoint) &&
            plan.EvidenceNotBefore == notBefore;

        return valid
            ? new(true, notBefore)
            : new(false, notBefore, ValidationReasonCodes.AuthorityMismatch);
    }

    public static async Task<ValidationAuthorityBindingResult> ValidateAsync(
        ValidationPlan plan,
        ExecutionRunAuthority authority,
        WorkspacePreparationReceipt receipt,
        RecoveryCheckpoint checkpoint,
        IRecoveryCheckpointRepository checkpoints,
        IContinuationHeadRepository heads,
        CancellationToken cancellationToken = default)
    {
        var basic = Validate(plan, authority, receipt, checkpoint);
        if (!basic.IsValid || Same(authority.InputRecoveryCheckpointReference, checkpoint.Reference))
            return basic.IsValid ? new(false, basic.NotBefore, ValidationReasonCodes.AuthorityMismatch) : basic;

        var head = await heads.GetAsync(plan.ProjectId, cancellationToken).ConfigureAwait(false);
        if (!head.IsValid || head.Head is null || head.Head.ProjectId != plan.ProjectId ||
            !Same(head.Head.LatestCheckpointReference, checkpoint.Reference))
            return new(false, basic.NotBefore, ValidationReasonCodes.AuthorityMismatch);

        var preRunReference = checkpoint.PreviousCheckpointReference;
        if (preRunReference is null)
            return new(false, basic.NotBefore, ValidationReasonCodes.AuthorityMismatch);

        var preRunRead = await checkpoints.GetAsync(plan.ProjectId, preRunReference.CheckpointId, cancellationToken).ConfigureAwait(false);
        if (!preRunRead.IsValid || preRunRead.Checkpoint is null ||
            !Same(preRunRead.Checkpoint.Reference, preRunReference) || !MatchesReference(preRunRead.Checkpoint) ||
            !SameCheckpointAuthorities(preRunRead.Checkpoint, plan) ||
            !SameNullable(preRunRead.Checkpoint.PreviousCheckpointReference, authority.InputRecoveryCheckpointReference))
            return new(false, basic.NotBefore, ValidationReasonCodes.AuthorityMismatch);

        var inputRead = await checkpoints.GetAsync(plan.ProjectId, authority.InputRecoveryCheckpointReference.CheckpointId, cancellationToken).ConfigureAwait(false);
        if (!inputRead.IsValid || inputRead.Checkpoint is null ||
            !Same(inputRead.Checkpoint.Reference, authority.InputRecoveryCheckpointReference) || !MatchesReference(inputRead.Checkpoint) ||
            !SameCheckpointAuthorities(inputRead.Checkpoint, plan))
            return new(false, basic.NotBefore, ValidationReasonCodes.AuthorityMismatch);

        return HasExactExecutionEvidenceTopology(inputRead.Checkpoint, preRunRead.Checkpoint, checkpoint, authority)
            ? basic
            : new(false, basic.NotBefore, ValidationReasonCodes.AuthorityMismatch);
    }

    private static bool SameCheckpointAuthorities(RecoveryCheckpoint checkpoint, ValidationPlan plan) =>
        checkpoint.ProjectId == plan.ProjectId &&
        Same(checkpoint.PlanningContractReference, plan.PlanningContractReference) &&
        Same(checkpoint.WorkGraphReference, plan.WorkGraphReference) &&
        checkpoint.WorkGraphNodeId == plan.WorkGraphNodeId &&
        Same(checkpoint.HandoffPackageReference, plan.HandoffPackageReference);

    private static bool MatchesReference(RecoveryCheckpoint checkpoint) =>
        string.Equals(RecoveryCheckpointIntegrity.ComputeContentHash(checkpoint), checkpoint.Reference.ContentHash, StringComparison.OrdinalIgnoreCase) &&
        checkpoint.Reference.CheckpointId == checkpoint.CheckpointId && checkpoint.Reference.SchemaVersion == checkpoint.SchemaVersion;

    private static bool HasExactExecutionEvidenceTopology(
        RecoveryCheckpoint input,
        RecoveryCheckpoint preRun,
        RecoveryCheckpoint terminal,
        ExecutionRunAuthority authority)
    {
        var inputEvidence = ExecutionEvidence(input, authority.ProjectId);
        var preRunEvidence = ExecutionEvidence(preRun, authority.ProjectId);
        var terminalEvidence = ExecutionEvidence(terminal, authority.ProjectId);
        if (inputEvidence is null || preRunEvidence is null || terminalEvidence is null)
            return false;

        var target = CreateExecutionEvidence(authority);
        if (input.EvidenceReferences.Any(value => value.EvidenceId == target.EvidenceId) ||
            !preRunEvidence.Any(value => SameEvidence(value, target)) ||
            !terminalEvidence.Any(value => SameEvidence(value, target)))
            return false;

        var introduced = preRunEvidence.Where(value => inputEvidence.All(existing => !SameEvidence(existing, value))).ToArray();
        return introduced.Length == 1 && SameEvidence(introduced[0], target) && SameExecutionEvidenceSet(preRunEvidence, terminalEvidence);
    }

    private static IReadOnlyList<RecoveryEvidenceReference>? ExecutionEvidence(RecoveryCheckpoint checkpoint, Guid projectId) =>
        checkpoint.EvidenceReferences.Count <= RecoveryCheckpointLimits.MaxEvidenceReferences
            ? checkpoint.EvidenceReferences.Where(value => IsExecutionEvidence(value, projectId)).ToArray()
            : null;

    private static bool IsExecutionEvidence(RecoveryEvidenceReference value, Guid projectId)
    {
        const string prefix = "execution-run:";
        if (value.Kind != RecoveryEvidenceKind.Other || !value.Reference.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var parts = value.Reference[prefix.Length..].Split('/');
        return parts.Length == 3 && Guid.TryParseExact(parts[0], "D", out var evidenceProjectId) && evidenceProjectId == projectId &&
            Guid.TryParseExact(parts[1], "D", out var runId) && runId == value.EvidenceId &&
            RecoveryCheckpointReference.IsSha256(parts[2]) && string.Equals(value.ContentHash, parts[2], StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameExecutionEvidenceSet(IReadOnlyList<RecoveryEvidenceReference> left, IReadOnlyList<RecoveryEvidenceReference> right) =>
        left.Count == right.Count && left.All(value => right.Any(candidate => SameEvidence(value, candidate)));

    private static RecoveryEvidenceReference CreateExecutionEvidence(ExecutionRunAuthority authority) =>
        new(authority.RunId, RecoveryEvidenceKind.Other,
            $"execution-run:{authority.ProjectId:D}/{authority.RunId:D}/{authority.ContentHash}", authority.CreatedAt,
            RecoveryEvidenceFreshness.PointInTime, contentHash: authority.ContentHash);

    private static bool SameEvidence(RecoveryEvidenceReference left, RecoveryEvidenceReference right) =>
        left.EvidenceId == right.EvidenceId && left.Kind == right.Kind &&
        string.Equals(left.Reference, right.Reference, StringComparison.Ordinal) && left.ObservedAt == right.ObservedAt &&
        left.Freshness == right.Freshness && left.ValidUntil == right.ValidUntil &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool Same(ExecutionRunAuthorityReference left, ExecutionRunAuthorityReference right) =>
        left.RunId == right.RunId && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool Same(PlanningExecutionContractReference left, PlanningExecutionContractReference right) =>
        left.ContractId == right.ContractId && left.Revision == right.Revision && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool Same(WorkGraphReference? left, WorkGraphReference? right) =>
        left is null && right is null || left is not null && right is not null && left.GraphId == right.GraphId &&
        left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool Same(HandoffPackageReference? left, HandoffPackageReference? right) =>
        left is null && right is null || left is not null && right is not null && left.PackageId == right.PackageId &&
        left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool Same(RecoveryCheckpointReference left, RecoveryCheckpointReference right) =>
        left.CheckpointId == right.CheckpointId && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool SameNullable(RecoveryCheckpointReference? left, RecoveryCheckpointReference? right) =>
        left is null && right is null || left is not null && right is not null && Same(left, right);
}

public sealed record ValidationEvidenceSnapshot(string Hash, IReadOnlyList<ValidationEvidenceReference> References)
{
    public static bool TryCreate(IEnumerable<ValidationEvidence> evidence, out ValidationEvidenceSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var references = evidence
            .Where(value => value.Kind != ValidationEvidenceKind.Security)
            .Select(value => value.Reference)
            .OrderBy(value => value.EvidenceId)
            .ToArray();
        if (references.Length > ValidationLimits.MaxEvidenceItems || references.Select(value => value.EvidenceId).Distinct().Count() != references.Length)
        {
            snapshot = null;
            return false;
        }

        snapshot = FromReferences(references);
        return true;
    }

    public static ValidationEvidenceSnapshot FromReferences(IReadOnlyList<ValidationEvidenceReference> references)
    {
        var values = references?.ToArray() ?? throw new ArgumentNullException(nameof(references));
        if (values.Length > ValidationLimits.MaxEvidenceItems || values.Any(static value => value is null) || values.Select(value => value.EvidenceId).Distinct().Count() != values.Length)
            throw new ArgumentException("Security evidence snapshot references are invalid or exceed their bound.", nameof(references));
        var ordered = values.OrderBy(value => value.EvidenceId).ToArray();
        var payload = ordered.Select(value => new { value.EvidenceId, value.SchemaVersion, value.ContentHash }).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))).ToLowerInvariant();
        return new(hash, ordered);
    }

    public bool Matches(ValidationEvidence value) =>
        value.ValidatedEvidenceSetHash is not null &&
        string.Equals(value.ValidatedEvidenceSetHash, Hash, StringComparison.OrdinalIgnoreCase) &&
        value.ValidatedEvidenceReferences.Count == References.Count &&
        value.ValidatedEvidenceReferences.Zip(References).All(pair => Same(pair.First, pair.Second));

    private static bool Same(ValidationEvidenceReference left, ValidationEvidenceReference right) =>
        left.EvidenceId == right.EvidenceId && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
}

public sealed class ValidationEvidence
{
    public ValidationEvidence(
        Guid projectId,
        Guid evidenceId,
        ValidationPlanReference planReference,
        string requirementId,
        Guid runId,
        ExecutionRunAuthorityReference executionRunAuthorityReference,
        PlanningExecutionContractReference planningContractReference,
        WorkGraphReference workGraphReference,
        Guid workGraphNodeId,
        RecoveryCheckpointReference currentRecoveryCheckpointReference,
        Guid workspaceId,
        string workspacePath,
        string workspaceReceiptContentHash,
        string collectorIdentifier,
        ValidationEvidenceKind kind,
        ValidationEvidenceState state,
        ValidationOutcome outcome,
        ValidationCoverageScope coverage,
        ValidationBaselineRelation baselineRelation,
        DateTimeOffset capturedAt,
        bool independentlyCaptured = true,
        bool securityBoundaryValid = true,
        ValidationEvidenceReference? baselineEvidenceReference = null,
        string? targetIdentity = null,
        string? localHeadCommitSha = null,
        string? branchName = null,
        bool? localIsClean = null,
        string? repositoryIdentity = null,
        string? remoteCommitId = null,
        string? trackerProjectId = null,
        string? trackerWorkItemKey = null,
        string? trackerStatus = null,
        int stdoutBytes = 0,
        int stderrBytes = 0,
        bool outputTruncated = false,
        string? diagnosticSummary = null,
        string? reasonCode = null,
        int schemaVersion = ValidationSchema.CurrentVersion,
        string? contentHash = null,
        string? validationDefinitionId = null,
        string? validatedEvidenceSetHash = null,
        IReadOnlyList<ValidationEvidenceReference>? validatedEvidenceReferences = null)
    {
        if (projectId == Guid.Empty || evidenceId == Guid.Empty || runId == Guid.Empty || workspaceId == Guid.Empty)
            throw new ArgumentException("Project, evidence, run, and workspace identifiers are required.");
        if (schemaVersion != ValidationSchema.CurrentVersion)
            throw new ArgumentException("Only the current validation-evidence schema is supported.", nameof(schemaVersion));
        if (planReference is null || planReference.ProjectId != projectId)
            throw new ArgumentException("Validation evidence must bind to the same project as its plan.", nameof(planReference));
        if (executionRunAuthorityReference is null || executionRunAuthorityReference.RunId != runId)
            throw new ArgumentException("Validation evidence run identity does not match its authority reference.", nameof(executionRunAuthorityReference));
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(state) || !Enum.IsDefined(outcome) || !Enum.IsDefined(coverage) || !Enum.IsDefined(baselineRelation))
            throw new ArgumentException("Validation evidence enum values are invalid.");
        if (capturedAt == default || stdoutBytes < 0 || stderrBytes < 0)
            throw new ArgumentException("Validation evidence timestamp and output counts are invalid.");
        if (!ValidationPlanReference.IsSha256(workspaceReceiptContentHash))
            throw new ArgumentException("Workspace receipt content hash must be SHA-256 evidence.", nameof(workspaceReceiptContentHash));

        ProjectId = projectId;
        EvidenceId = evidenceId;
        SchemaVersion = schemaVersion;
        PlanReference = planReference;
        RequirementId = Required(requirementId, nameof(requirementId), 200);
        ValidationDefinitionId = Required(validationDefinitionId ?? "legacy", nameof(validationDefinitionId), 200);
        RunId = runId;
        ExecutionRunAuthorityReference = executionRunAuthorityReference;
        PlanningContractReference = planningContractReference ?? throw new ArgumentNullException(nameof(planningContractReference));
        WorkGraphReference = workGraphReference ?? throw new ArgumentNullException(nameof(workGraphReference));
        WorkGraphNodeId = workGraphNodeId;
        CurrentRecoveryCheckpointReference = currentRecoveryCheckpointReference ?? throw new ArgumentNullException(nameof(currentRecoveryCheckpointReference));
        if (workGraphNodeId == Guid.Empty)
            throw new ArgumentException("A work-graph node is required.", nameof(workGraphNodeId));
        WorkspaceId = workspaceId;
        WorkspacePath = Required(workspacePath, nameof(workspacePath), ValidationLimits.MaxIdentityLength);
        WorkspaceReceiptContentHash = workspaceReceiptContentHash.ToLowerInvariant();
        CollectorIdentifier = Required(collectorIdentifier, nameof(collectorIdentifier), 200);
        Kind = kind;
        State = state;
        Outcome = outcome;
        Coverage = coverage;
        BaselineRelation = baselineRelation;
        CapturedAt = capturedAt;
        IndependentlyCaptured = independentlyCaptured;
        SecurityBoundaryValid = securityBoundaryValid;
        BaselineEvidenceReference = baselineEvidenceReference;
        TargetIdentity = Optional(targetIdentity);
        LocalHeadCommitSha = Optional(localHeadCommitSha);
        BranchName = Optional(branchName);
        LocalIsClean = localIsClean;
        RepositoryIdentity = Optional(repositoryIdentity);
        RemoteCommitId = Optional(remoteCommitId);
        TrackerProjectId = Optional(trackerProjectId);
        TrackerWorkItemKey = Optional(trackerWorkItemKey);
        TrackerStatus = Optional(trackerStatus);
        StdoutBytes = stdoutBytes;
        StderrBytes = stderrBytes;
        OutputTruncated = outputTruncated;
        DiagnosticSummary = Optional(diagnosticSummary, ValidationLimits.MaxDiagnosticLength);
        ReasonCode = Optional(reasonCode, 200);
        var snapshotReferences = validatedEvidenceReferences?.ToArray() ?? Array.Empty<ValidationEvidenceReference>();
        if (kind != ValidationEvidenceKind.Security && (validatedEvidenceSetHash is not null || snapshotReferences.Length > 0))
            throw new ArgumentException("Only Security evidence may carry an evidence-set snapshot.");
        if (kind == ValidationEvidenceKind.Security && snapshotReferences.Length > ValidationLimits.MaxEvidenceItems)
            throw new ArgumentException("Security evidence snapshot exceeds its supported bound.", nameof(validatedEvidenceReferences));
        if (validatedEvidenceSetHash is not null && !ValidationPlanReference.IsSha256(validatedEvidenceSetHash))
            throw new ArgumentException("Security evidence snapshot hash must be SHA-256 evidence.", nameof(validatedEvidenceSetHash));
        if (kind == ValidationEvidenceKind.Security && (snapshotReferences.Length > 0 || validatedEvidenceSetHash is not null))
        {
            var snapshot = ValidationEvidenceSnapshot.FromReferences(snapshotReferences);
            if (validatedEvidenceSetHash is not null && !string.Equals(validatedEvidenceSetHash, snapshot.Hash, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Security evidence snapshot hash does not match its references.", nameof(validatedEvidenceSetHash));
            validatedEvidenceSetHash = snapshot.Hash;
            snapshotReferences = snapshot.References.ToArray();
        }
        ValidatedEvidenceSetHash = validatedEvidenceSetHash?.ToLowerInvariant();
        ValidatedEvidenceReferences = snapshotReferences.OrderBy(value => value.EvidenceId).ToArray();
        ContentHash = string.Empty;
        var calculatedHash = ValidationIntegrity.ComputeEvidenceHash(this);
        if (contentHash is not null && (!ValidationPlanReference.IsSha256(contentHash) || !string.Equals(contentHash, calculatedHash, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Validation-evidence content hash does not match its payload.", nameof(contentHash));
        ContentHash = calculatedHash;
        if (ValidationIntegrity.ComputeEvidencePayloadBytes(this) > ValidationLimits.MaxCanonicalPayloadBytes)
            throw new ArgumentException("Validation-evidence payload exceeds its supported bound.", nameof(diagnosticSummary));
        Reference = new ValidationEvidenceReference(EvidenceId, SchemaVersion, ContentHash);
    }

    public Guid ProjectId { get; }
    public Guid EvidenceId { get; }
    public int SchemaVersion { get; }
    public ValidationPlanReference PlanReference { get; }
    public string RequirementId { get; }
    public string ValidationDefinitionId { get; }
    public Guid RunId { get; }
    public ExecutionRunAuthorityReference ExecutionRunAuthorityReference { get; }
    public PlanningExecutionContractReference PlanningContractReference { get; }
    public WorkGraphReference WorkGraphReference { get; }
    public Guid WorkGraphNodeId { get; }
    public RecoveryCheckpointReference CurrentRecoveryCheckpointReference { get; }
    public Guid WorkspaceId { get; }
    public string WorkspacePath { get; }
    public string WorkspaceReceiptContentHash { get; }
    public string CollectorIdentifier { get; }
    public ValidationEvidenceKind Kind { get; }
    public ValidationEvidenceState State { get; }
    public ValidationOutcome Outcome { get; }
    public ValidationCoverageScope Coverage { get; }
    public ValidationBaselineRelation BaselineRelation { get; }
    public DateTimeOffset CapturedAt { get; }
    public bool IndependentlyCaptured { get; }
    public bool SecurityBoundaryValid { get; }
    public ValidationEvidenceReference? BaselineEvidenceReference { get; }
    public string? TargetIdentity { get; }
    public string? LocalHeadCommitSha { get; }
    public string? BranchName { get; }
    public bool? LocalIsClean { get; }
    public string? RepositoryIdentity { get; }
    public string? RemoteCommitId { get; }
    public string? TrackerProjectId { get; }
    public string? TrackerWorkItemKey { get; }
    public string? TrackerStatus { get; }
    public int StdoutBytes { get; }
    public int StderrBytes { get; }
    public bool OutputTruncated { get; }
    public string? DiagnosticSummary { get; }
    public string? ReasonCode { get; }
    public string? ValidatedEvidenceSetHash { get; }
    public IReadOnlyList<ValidationEvidenceReference> ValidatedEvidenceReferences { get; }
    public string ContentHash { get; private set; }
    public ValidationEvidenceReference Reference { get; private set; }

    private static string Required(string value, string parameterName, int max) =>
        Optional(value, max) ?? throw new ArgumentException("A bounded value is required.", parameterName);

    private static string? Optional(string? value, int max = ValidationLimits.MaxIdentityLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Any(static character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            throw new ArgumentException("Validation evidence text contains unsupported control characters.");
        return normalized.Length <= max ? normalized : throw new ArgumentException("Validation evidence text exceeds its supported bound.");
    }
}

public sealed class ValidationRequirementDecision
{
    public ValidationRequirementDecision(
        string requirementId,
        ValidationRequirementDecisionState state,
        IReadOnlyList<ValidationEvidenceReference>? supportingEvidence,
        ValidationEvidenceState? observedState,
        ValidationOutcome? observedOutcome,
        bool? fresh,
        string reasonCode,
        string explanation)
    {
        RequirementId = Required(requirementId, nameof(requirementId), 200);
        if (!Enum.IsDefined(state)) throw new ArgumentException("Requirement decision state is invalid.", nameof(state));
        var refs = supportingEvidence?.ToArray() ?? Array.Empty<ValidationEvidenceReference>();
        if (refs.Length > ValidationLimits.MaxSupportingEvidenceReferences || refs.Any(static value => value is null) || refs.Select(static value => value.EvidenceId).Distinct().Count() != refs.Length)
            throw new ArgumentException("Requirement supporting evidence is invalid or exceeds its bound.", nameof(supportingEvidence));
        State = state;
        SupportingEvidence = refs.OrderBy(static value => value.EvidenceId).ToArray();
        ObservedState = observedState;
        ObservedOutcome = observedOutcome;
        Fresh = fresh;
        ReasonCode = Required(reasonCode, nameof(reasonCode), 200);
        Explanation = Required(explanation, nameof(explanation), ValidationLimits.MaxDiagnosticLength);
    }

    public string RequirementId { get; }
    public ValidationRequirementDecisionState State { get; }
    public IReadOnlyList<ValidationEvidenceReference> SupportingEvidence { get; }
    public ValidationEvidenceState? ObservedState { get; }
    public ValidationOutcome? ObservedOutcome { get; }
    public bool? Fresh { get; }
    public string ReasonCode { get; }
    public string Explanation { get; }

    private static string Required(string value, string parameterName, int max) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A bounded value is required.", parameterName) :
        value.Trim().Length <= max ? value.Trim() : throw new ArgumentException("Decision text exceeds its supported bound.", parameterName);
}

public sealed class ValidationGateDecision
{
    public ValidationGateDecision(
        Guid projectId,
        Guid decisionId,
        ValidationPlanReference planReference,
        ExecutionRunAuthorityReference executionRunAuthorityReference,
        RecoveryCheckpointReference currentRecoveryCheckpointReference,
        DateTimeOffset decidedAt,
        ValidationGateDecisionState state,
        IReadOnlyList<ValidationRequirementDecision> requirementDecisions,
        IReadOnlyList<ValidationEvidenceReference>? supportingEvidence = null,
        string? reasonCode = null,
        string? explanation = null,
        int schemaVersion = ValidationSchema.CurrentVersion,
        string? contentHash = null)
    {
        if (projectId == Guid.Empty || decisionId == Guid.Empty || planReference is null || planReference.ProjectId != projectId)
            throw new ArgumentException("Decision identity is invalid.");
        if (executionRunAuthorityReference is null || currentRecoveryCheckpointReference is null || decidedAt == default)
            throw new ArgumentException("Decision authorities and timestamp are required.");
        if (!Enum.IsDefined(state) || schemaVersion != ValidationSchema.CurrentVersion)
            throw new ArgumentException("Decision state or schema is invalid.");
        var decisions = requirementDecisions?.ToArray() ?? throw new ArgumentNullException(nameof(requirementDecisions));
        if (decisions.Length > ValidationLimits.MaxRequirements || decisions.Any(static value => value is null))
            throw new ArgumentException("Requirement decisions exceed their bound.", nameof(requirementDecisions));
        var refs = supportingEvidence?.ToArray() ?? Array.Empty<ValidationEvidenceReference>();
        if (refs.Length > ValidationLimits.MaxEvidenceItems || refs.Any(static value => value is null))
            throw new ArgumentException("Decision evidence references exceed their bound.", nameof(supportingEvidence));

        ProjectId = projectId;
        DecisionId = decisionId;
        SchemaVersion = schemaVersion;
        PlanReference = planReference;
        ExecutionRunAuthorityReference = executionRunAuthorityReference;
        CurrentRecoveryCheckpointReference = currentRecoveryCheckpointReference;
        DecidedAt = decidedAt;
        State = state;
        RequirementDecisions = decisions.OrderBy(static value => value.RequirementId, StringComparer.Ordinal).ToArray();
        SupportingEvidence = refs.OrderBy(static value => value.EvidenceId).ToArray();
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? null : reasonCode.Trim();
        Explanation = string.IsNullOrWhiteSpace(explanation) ? null : explanation.Trim();
        ContentHash = string.Empty;
        var calculatedHash = ValidationIntegrity.ComputeDecisionHash(this);
        if (contentHash is not null && (!ValidationPlanReference.IsSha256(contentHash) || !string.Equals(contentHash, calculatedHash, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Validation-decision content hash does not match its payload.", nameof(contentHash));
        ContentHash = calculatedHash;
        if (ValidationIntegrity.ComputeDecisionPayloadBytes(this) > ValidationLimits.MaxCanonicalPayloadBytes)
            throw new ArgumentException("Validation-decision payload exceeds its supported bound.", nameof(requirementDecisions));
        Reference = new ValidationGateDecisionReference(DecisionId, SchemaVersion, ContentHash);
    }

    public Guid ProjectId { get; }
    public Guid DecisionId { get; }
    public int SchemaVersion { get; }
    public ValidationPlanReference PlanReference { get; }
    public ExecutionRunAuthorityReference ExecutionRunAuthorityReference { get; }
    public RecoveryCheckpointReference CurrentRecoveryCheckpointReference { get; }
    public DateTimeOffset DecidedAt { get; }
    public ValidationGateDecisionState State { get; }
    public IReadOnlyList<ValidationRequirementDecision> RequirementDecisions { get; }
    public IReadOnlyList<ValidationEvidenceReference> SupportingEvidence { get; }
    public string? ReasonCode { get; }
    public string? Explanation { get; }
    public string ContentHash { get; private set; }
    public ValidationGateDecisionReference Reference { get; private set; }
}

public static class ValidationIntegrity
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string ComputePlanHash(ValidationPlan value) => Hash(CreatePlanPayload(value));
    public static int ComputePlanPayloadBytes(ValidationPlan value) => Bytes(CreatePlanPayload(value));
    public static string ComputeEvidenceHash(ValidationEvidence value) => Hash(CreateEvidencePayload(value));
    public static int ComputeEvidencePayloadBytes(ValidationEvidence value) => Bytes(CreateEvidencePayload(value));
    public static string ComputeDecisionHash(ValidationGateDecision value) => Hash(CreateDecisionPayload(value));
    public static int ComputeDecisionPayloadBytes(ValidationGateDecision value) => Bytes(CreateDecisionPayload(value));

    internal static object CreatePlanPayload(ValidationPlan value) => new
    {
        value.ProjectId, value.PlanId, value.Revision, value.SchemaVersion, value.CreatedAt, value.EvidenceNotBefore,
        executionRunAuthorityReference = new { value.ExecutionRunAuthorityReference.RunId, value.ExecutionRunAuthorityReference.SchemaVersion, value.ExecutionRunAuthorityReference.ContentHash },
        planningContractReference = new { value.PlanningContractReference.ContractId, value.PlanningContractReference.Revision, value.PlanningContractReference.SchemaVersion, value.PlanningContractReference.ContentHash },
        workGraphReference = value.WorkGraphReference is null ? null : new { value.WorkGraphReference.GraphId, value.WorkGraphReference.SchemaVersion, value.WorkGraphReference.ContentHash },
        value.WorkGraphNodeId,
        handoffPackageReference = value.HandoffPackageReference is null ? null : new { value.HandoffPackageReference.PackageId, value.HandoffPackageReference.SchemaVersion, value.HandoffPackageReference.ContentHash },
        value.WorkspaceId, value.WorkspacePath, value.WorkspaceReceiptContentHash,
        currentRecoveryCheckpointReference = new { value.CurrentRecoveryCheckpointReference.CheckpointId, value.CurrentRecoveryCheckpointReference.SchemaVersion, value.CurrentRecoveryCheckpointReference.ContentHash },
        requirements = value.Requirements.Select(CreateRequirementPayload).ToArray()
    };

    internal static object CreateEvidencePayload(ValidationEvidence value) =>
        value.Kind == ValidationEvidenceKind.Security && (value.ValidatedEvidenceSetHash is not null || value.ValidatedEvidenceReferences.Count > 0)
            ? new
            {
                value.ProjectId, value.EvidenceId, value.SchemaVersion, value.PlanReference, value.RequirementId, value.ValidationDefinitionId, value.RunId,
                executionRunAuthorityReference = new { value.ExecutionRunAuthorityReference.RunId, value.ExecutionRunAuthorityReference.SchemaVersion, value.ExecutionRunAuthorityReference.ContentHash },
                planningContractReference = new { value.PlanningContractReference.ContractId, value.PlanningContractReference.Revision, value.PlanningContractReference.SchemaVersion, value.PlanningContractReference.ContentHash },
                workGraphReference = value.WorkGraphReference is null ? null : new { value.WorkGraphReference.GraphId, value.WorkGraphReference.SchemaVersion, value.WorkGraphReference.ContentHash },
                value.WorkGraphNodeId,
                currentRecoveryCheckpointReference = new { value.CurrentRecoveryCheckpointReference.CheckpointId, value.CurrentRecoveryCheckpointReference.SchemaVersion, value.CurrentRecoveryCheckpointReference.ContentHash },
                value.WorkspaceId, value.WorkspacePath, value.WorkspaceReceiptContentHash, value.CollectorIdentifier, value.Kind, value.State,
                value.Outcome, value.Coverage, value.BaselineRelation, value.CapturedAt, value.IndependentlyCaptured, value.SecurityBoundaryValid,
                baselineEvidenceReference = value.BaselineEvidenceReference is null ? null : new { value.BaselineEvidenceReference.EvidenceId, value.BaselineEvidenceReference.SchemaVersion, value.BaselineEvidenceReference.ContentHash },
                value.TargetIdentity, value.LocalHeadCommitSha, value.BranchName, value.LocalIsClean, value.RepositoryIdentity, value.RemoteCommitId,
                value.TrackerProjectId, value.TrackerWorkItemKey, value.TrackerStatus, value.StdoutBytes, value.StderrBytes, value.OutputTruncated,
                value.DiagnosticSummary, value.ReasonCode, value.ValidatedEvidenceSetHash,
                validatedEvidenceReferences = value.ValidatedEvidenceReferences.Select(reference => new { reference.EvidenceId, reference.SchemaVersion, reference.ContentHash }).ToArray()
            }
            : new
            {
                value.ProjectId, value.EvidenceId, value.SchemaVersion, value.PlanReference, value.RequirementId, value.ValidationDefinitionId, value.RunId,
                executionRunAuthorityReference = new { value.ExecutionRunAuthorityReference.RunId, value.ExecutionRunAuthorityReference.SchemaVersion, value.ExecutionRunAuthorityReference.ContentHash },
                planningContractReference = new { value.PlanningContractReference.ContractId, value.PlanningContractReference.Revision, value.PlanningContractReference.SchemaVersion, value.PlanningContractReference.ContentHash },
                workGraphReference = value.WorkGraphReference is null ? null : new { value.WorkGraphReference.GraphId, value.WorkGraphReference.SchemaVersion, value.WorkGraphReference.ContentHash },
                value.WorkGraphNodeId,
                currentRecoveryCheckpointReference = new { value.CurrentRecoveryCheckpointReference.CheckpointId, value.CurrentRecoveryCheckpointReference.SchemaVersion, value.CurrentRecoveryCheckpointReference.ContentHash },
                value.WorkspaceId, value.WorkspacePath, value.WorkspaceReceiptContentHash, value.CollectorIdentifier, value.Kind, value.State,
                value.Outcome, value.Coverage, value.BaselineRelation, value.CapturedAt, value.IndependentlyCaptured, value.SecurityBoundaryValid,
                baselineEvidenceReference = value.BaselineEvidenceReference is null ? null : new { value.BaselineEvidenceReference.EvidenceId, value.BaselineEvidenceReference.SchemaVersion, value.BaselineEvidenceReference.ContentHash },
                value.TargetIdentity, value.LocalHeadCommitSha, value.BranchName, value.LocalIsClean, value.RepositoryIdentity, value.RemoteCommitId,
                value.TrackerProjectId, value.TrackerWorkItemKey, value.TrackerStatus, value.StdoutBytes, value.StderrBytes, value.OutputTruncated,
                value.DiagnosticSummary, value.ReasonCode
            };

    internal static object CreateDecisionPayload(ValidationGateDecision value) => new
    {
        value.ProjectId, value.DecisionId, value.SchemaVersion, value.PlanReference,
        executionRunAuthorityReference = new { value.ExecutionRunAuthorityReference.RunId, value.ExecutionRunAuthorityReference.SchemaVersion, value.ExecutionRunAuthorityReference.ContentHash },
        currentRecoveryCheckpointReference = new { value.CurrentRecoveryCheckpointReference.CheckpointId, value.CurrentRecoveryCheckpointReference.SchemaVersion, value.CurrentRecoveryCheckpointReference.ContentHash },
        value.DecidedAt, value.State,
        requirementDecisions = value.RequirementDecisions.Select(decision => new
        {
            decision.RequirementId, decision.State,
            supportingEvidence = decision.SupportingEvidence.Select(reference => new { reference.EvidenceId, reference.SchemaVersion, reference.ContentHash }).ToArray(),
            decision.ObservedState, decision.ObservedOutcome, decision.Fresh, decision.ReasonCode, decision.Explanation
        }).ToArray(),
        supportingEvidence = value.SupportingEvidence.Select(reference => new { reference.EvidenceId, reference.SchemaVersion, reference.ContentHash }).ToArray(),
        value.ReasonCode, value.Explanation
    };

    private static object CreateRequirementPayload(ValidationRequirement value) => new
    {
        value.RequirementId, value.ValidationDefinitionId, value.EvidenceKind, value.Required, value.Coverage, value.BaselineRelation, value.CollectorIdentifier,
        maxAgeSeconds = value.MaxAge?.TotalSeconds, value.AllowFullEvidenceForTargeted, value.TargetPath, value.TestFilter,
        timeoutSeconds = value.Timeout?.TotalSeconds, value.ExpectedLocalHeadCommitSha, value.ExpectedBranchName, value.RequireCleanWorktree,
        value.ExpectedRepositoryIdentity, value.ExpectedRemoteCommitId, value.ExpectedTrackerProjectId, value.ExpectedTrackerWorkItemKey,
        value.ExpectedTrackerStatus, value.ExpectedState, value.ExpectedOutcome, value.RequestedBranch, value.PullRequestNumber,
        baselineBinding = value.BaselineBinding is null ? null : new
        {
            planReference = new { value.BaselineBinding.PlanReference.ProjectId, value.BaselineBinding.PlanReference.PlanId, value.BaselineBinding.PlanReference.Revision, value.BaselineBinding.PlanReference.SchemaVersion, value.BaselineBinding.PlanReference.ContentHash },
            evidenceReference = new { value.BaselineBinding.EvidenceReference.EvidenceId, value.BaselineBinding.EvidenceReference.SchemaVersion, value.BaselineBinding.EvidenceReference.ContentHash },
            value.BaselineBinding.ValidationDefinitionId
        }
    };

    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Options)))).ToLowerInvariant();
    private static int Bytes(object value) => JsonSerializer.SerializeToUtf8Bytes(value, Options).Length;
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public sealed class ValidationCollectionContext
{
    public ValidationCollectionContext(
        ValidationPlan plan,
        ValidationRequirement requirement,
        Project project,
        ExecutionRunAuthority authority,
        WorkspacePreparationReceipt workspaceReceipt,
        RecoveryCheckpoint currentCheckpoint,
        IReadOnlyList<ValidationEvidence> existingEvidence,
        ValidationEvidenceReference? baselineEvidenceReference = null)
    {
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        Project = project ?? throw new ArgumentNullException(nameof(project));
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        WorkspaceReceipt = workspaceReceipt ?? throw new ArgumentNullException(nameof(workspaceReceipt));
        CurrentCheckpoint = currentCheckpoint ?? throw new ArgumentNullException(nameof(currentCheckpoint));
        ExistingEvidence = existingEvidence?.ToArray() ?? throw new ArgumentNullException(nameof(existingEvidence));
        BaselineEvidenceReference = baselineEvidenceReference;
    }

    public ValidationPlan Plan { get; }
    public ValidationRequirement Requirement { get; }
    public Project Project { get; }
    public ExecutionRunAuthority Authority { get; }
    public WorkspacePreparationReceipt WorkspaceReceipt { get; }
    public RecoveryCheckpoint CurrentCheckpoint { get; }
    public IReadOnlyList<ValidationEvidence> ExistingEvidence { get; }
    public ValidationEvidenceReference? BaselineEvidenceReference { get; }
}

public sealed class ValidationEvidenceCollectorDescriptor
{
    public ValidationEvidenceCollectorDescriptor(string identifier, IReadOnlyList<ValidationEvidenceKind> supportedKinds, bool executesLocalCommand, bool supportsCancellation)
    {
        Identifier = string.IsNullOrWhiteSpace(identifier) ? throw new ArgumentException("Collector identifier is required.", nameof(identifier)) : identifier.Trim();
        var kinds = supportedKinds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(supportedKinds));
        if (kinds.Length == 0 || kinds.Any(static value => !Enum.IsDefined(value))) throw new ArgumentException("Collector evidence kinds are invalid.", nameof(supportedKinds));
        SupportedKinds = kinds.OrderBy(static value => value).ToArray();
        ExecutesLocalCommand = executesLocalCommand;
        SupportsCancellation = supportsCancellation;
    }

    public string Identifier { get; }
    public IReadOnlyList<ValidationEvidenceKind> SupportedKinds { get; }
    public bool ExecutesLocalCommand { get; }
    public bool SupportsCancellation { get; }
}

public interface IValidationEvidenceCollector
{
    ValidationEvidenceCollectorDescriptor Descriptor { get; }
    Task<ValidationEvidence> CaptureAsync(ValidationCollectionContext context, CancellationToken cancellationToken = default);
}

public enum ValidationCollectorResolutionStatus
{
    Resolved,
    Unsupported,
    ConfigurationConflict
}

public sealed record ValidationCollectorResolution(ValidationCollectorResolutionStatus Status, IValidationEvidenceCollector? Collector = null, string? ErrorMessage = null)
{
    public bool Succeeded => Status == ValidationCollectorResolutionStatus.Resolved && Collector is not null;
}

public interface IValidationEvidenceCollectorResolver
{
    ValidationCollectorResolution Resolve(string collectorIdentifier, ValidationEvidenceKind kind);
}

public sealed class ValidationEvidenceCollectorResolver : IValidationEvidenceCollectorResolver
{
    private readonly IReadOnlyList<IValidationEvidenceCollector> _collectors;

    public ValidationEvidenceCollectorResolver(IEnumerable<IValidationEvidenceCollector> collectors)
    {
        ArgumentNullException.ThrowIfNull(collectors);
        _collectors = collectors.ToArray();
    }

    public ValidationCollectorResolution Resolve(string collectorIdentifier, ValidationEvidenceKind kind)
    {
        if (string.IsNullOrWhiteSpace(collectorIdentifier) || !Enum.IsDefined(kind))
            return new(ValidationCollectorResolutionStatus.Unsupported, ErrorMessage: "The validation collector identity is invalid.");
        var matches = _collectors.Where(collector => collector is not null &&
            string.Equals(collector.Descriptor.Identifier, collectorIdentifier.Trim(), StringComparison.Ordinal) &&
            collector.Descriptor.SupportedKinds.Contains(kind)).ToArray();
        return matches.Length switch
        {
            0 => new(ValidationCollectorResolutionStatus.Unsupported, ErrorMessage: "No exact validation collector is registered."),
            1 => new(ValidationCollectorResolutionStatus.Resolved, matches[0]),
            _ => new(ValidationCollectorResolutionStatus.ConfigurationConflict, ErrorMessage: "More than one exact validation collector is registered.")
        };
    }
}

public enum ValidationPlanRepositoryWriteStatus { Created, PlanConflict, Unavailable }
public sealed record ValidationPlanRepositoryWriteResult(ValidationPlanRepositoryWriteStatus Status, string? ErrorMessage = null)
{
    public bool Succeeded => Status == ValidationPlanRepositoryWriteStatus.Created;
}

public enum ValidationPlanReadState { Missing, Valid, UnsupportedFutureVersion, MigrationRequired, Invalid, IntegrityFailure, Unavailable }
public sealed record ValidationPlanReadResult(ValidationPlanReadState State, ValidationPlan? Plan = null, string? ErrorMessage = null)
{
    public bool IsValid => State == ValidationPlanReadState.Valid && Plan is not null;
}

public interface IValidationPlanRepository
{
    Task<ValidationPlanRepositoryWriteResult> CreateAsync(ValidationPlan plan, CancellationToken cancellationToken = default);
    Task<ValidationPlanReadResult> GetAsync(Guid projectId, ValidationPlanReference reference, CancellationToken cancellationToken = default);
}

public enum ValidationEvidenceRepositoryWriteStatus { Created, EvidenceConflict, Unavailable }
public sealed record ValidationEvidenceRepositoryWriteResult(ValidationEvidenceRepositoryWriteStatus Status, string? ErrorMessage = null)
{
    public bool Succeeded => Status == ValidationEvidenceRepositoryWriteStatus.Created;
}

public enum ValidationEvidenceReadState { Missing, Valid, UnsupportedFutureVersion, MigrationRequired, Invalid, IntegrityFailure, Unavailable }
public sealed record ValidationEvidenceReadResult(ValidationEvidenceReadState State, ValidationEvidence? Evidence = null, string? ErrorMessage = null)
{
    public bool IsValid => State == ValidationEvidenceReadState.Valid && Evidence is not null;
}

public interface IValidationEvidenceRepository
{
    Task<ValidationEvidenceRepositoryWriteResult> CreateAsync(ValidationEvidence evidence, CancellationToken cancellationToken = default);
    Task<ValidationEvidenceReadResult> GetAsync(Guid projectId, ValidationPlanReference planReference, ValidationEvidenceReference evidenceReference, CancellationToken cancellationToken = default);
    Task<ValidationEvidenceSetReadResult> GetForPlanAsync(Guid projectId, ValidationPlanReference planReference, CancellationToken cancellationToken = default);
}

public enum ValidationEvidenceSetReadState { Valid, UnsupportedFutureVersion, MigrationRequired, Invalid, IntegrityFailure, CapacityExceeded, Unavailable }
public sealed record ValidationEvidenceSetReadResult(ValidationEvidenceSetReadState State, IReadOnlyList<ValidationEvidence>? Evidence = null, string? ErrorMessage = null)
{
    public bool IsComplete => State == ValidationEvidenceSetReadState.Valid && Evidence is not null;
}

public enum ValidationDecisionRepositoryWriteStatus { Created, DecisionConflict, Unavailable }
public sealed record ValidationDecisionRepositoryWriteResult(ValidationDecisionRepositoryWriteStatus Status, string? ErrorMessage = null)
{
    public bool Succeeded => Status == ValidationDecisionRepositoryWriteStatus.Created;
}

public enum ValidationDecisionReadState { Missing, Valid, UnsupportedFutureVersion, MigrationRequired, Invalid, IntegrityFailure, Unavailable }
public sealed record ValidationDecisionReadResult(ValidationDecisionReadState State, ValidationGateDecision? Decision = null, string? ErrorMessage = null)
{
    public bool IsValid => State == ValidationDecisionReadState.Valid && Decision is not null;
}

public interface IValidationGateDecisionRepository
{
    Task<ValidationDecisionRepositoryWriteResult> CreateAsync(ValidationGateDecision decision, CancellationToken cancellationToken = default);
    Task<ValidationDecisionReadResult> GetAsync(Guid projectId, Guid decisionId, CancellationToken cancellationToken = default);
}

public sealed record ValidationCaptureRequest(Guid ProjectId, ValidationPlanReference PlanReference, string RequirementId, RecoveryCheckpointReference CurrentRecoveryCheckpointReference);
public sealed record ValidationCaptureResult(ValidationEvidence? Evidence, string? ErrorMessage = null)
{
    public bool Succeeded => Evidence is not null && ErrorMessage is null;
}

public interface IValidationEvidenceService
{
    Task<ValidationPlanRepositoryWriteResult> CreatePlanAsync(ValidationPlan plan, CancellationToken cancellationToken = default);
    Task<ValidationCaptureResult> CaptureAsync(ValidationCaptureRequest request, CancellationToken cancellationToken = default);
}

public sealed record ValidationGateRequest(Guid ProjectId, ValidationPlanReference PlanReference, RecoveryCheckpointReference CurrentRecoveryCheckpointReference);
public sealed record ValidationGateEvaluationResult(ValidationGateDecision? Decision, RecoveryCheckpointCreationResult? Recovery = null, string? ErrorMessage = null)
{
    public bool Succeeded => Decision is not null && ErrorMessage is null;
}

public interface IValidationGateService
{
    Task<ValidationGateEvaluationResult> EvaluateAsync(ValidationGateRequest request, CancellationToken cancellationToken = default);
}

public static class ValidationGateEvaluator
{
    public static ValidationGateDecision Evaluate(
        ValidationPlan plan,
        IReadOnlyList<ValidationEvidence> evidence,
        DateTimeOffset now,
        Guid? decisionId = null,
        IReadOnlyList<ValidationEvidence>? baselineEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evidence);
        if (now == default) throw new ArgumentException("Evaluation time is required.", nameof(now));

        var decisions = new List<ValidationRequirementDecision>(plan.Requirements.Count);
        var supporting = new List<ValidationEvidenceReference>();
        var snapshotExists = ValidationEvidenceSnapshot.TryCreate(evidence, out var currentSnapshot);
        foreach (var requirement in plan.Requirements)
        {
            var matches = evidence.Where(value => MatchesIdentity(plan, requirement, value)).ToArray();
            if (matches.Length == 0)
            {
                decisions.Add(new(requirement.RequirementId,
                    requirement.Required ? ValidationRequirementDecisionState.Missing : ValidationRequirementDecisionState.NotApplicable,
                    null, null, null, null, ValidationReasonCodes.RequiredEvidenceMissing,
                    requirement.Required ? "Required independent validation evidence is missing." : "Optional validation evidence is not present."));
                continue;
            }

            var stale = matches.FirstOrDefault(value => !Freshness(requirement, plan, value, now).IsFresh);
            if (stale is not null)
            {
                var freshness = Freshness(requirement, plan, stale, now);
                decisions.Add(Decision(requirement, stale, ValidationRequirementDecisionState.Stale, freshness.Reason, "Independent validation evidence is outside the accepted freshness boundary.", false));
                supporting.Add(stale.Reference);
                continue;
            }

            var failed = matches.FirstOrDefault(value => value.State == ValidationEvidenceState.Available && value.Outcome == ValidationOutcome.Failed);
            if (failed is not null)
            {
                decisions.Add(Decision(requirement, failed, ValidationRequirementDecisionState.Failed, ValidationReasonCodes.EvidenceFailed, "Independent validation evidence reported a failure.", true));
                supporting.Add(failed.Reference);
                continue;
            }

            var unusable = matches.FirstOrDefault(value => !IsUsable(requirement, plan, value, baselineEvidence, now, snapshotExists ? currentSnapshot : null, out _));
            if (unusable is not null)
            {
                var unusableReason = ValidationReasonCodes.EvidenceNotUsable;
                IsUsable(requirement, plan, unusable, baselineEvidence, now, snapshotExists ? currentSnapshot : null, out unusableReason);
                decisions.Add(Decision(requirement, unusable, ValidationRequirementDecisionState.Blocked, unusableReason, "Independent validation evidence cannot satisfy the requirement.", false));
                supporting.Add(unusable.Reference);
                continue;
            }

            var passed = matches.FirstOrDefault(value => IsUsable(requirement, plan, value, baselineEvidence, now, snapshotExists ? currentSnapshot : null, out _));
            if (passed is not null)
            {
                decisions.Add(Decision(requirement, passed, ValidationRequirementDecisionState.Satisfied, ValidationReasonCodes.Satisfied, "Independent validation evidence satisfies the requirement.", true));
                supporting.Add(passed.Reference);
            }
        }

        var required = decisions.Zip(plan.Requirements, static (decision, requirement) => (decision, requirement))
            .Where(static value => value.requirement.Required)
            .Select(static value => value.decision)
            .ToArray();
        var state = required.Any(static value => value.State == ValidationRequirementDecisionState.Failed)
            ? ValidationGateDecisionState.Failed
            : required.Any(static value => value.State == ValidationRequirementDecisionState.Stale)
                ? ValidationGateDecisionState.Stale
                : required.Any(static value => value.State == ValidationRequirementDecisionState.Blocked)
                    ? ValidationGateDecisionState.Blocked
                    : required.Any(static value => value.State == ValidationRequirementDecisionState.Missing)
                        ? ValidationGateDecisionState.Pending
                        : ValidationGateDecisionState.Satisfied;
        var reason = state switch
        {
            ValidationGateDecisionState.Satisfied => ValidationReasonCodes.Satisfied,
            ValidationGateDecisionState.Failed => ValidationReasonCodes.EvidenceFailed,
            ValidationGateDecisionState.Stale => ValidationReasonCodes.EvidenceStale,
            ValidationGateDecisionState.Blocked => ValidationReasonCodes.EvidenceNotUsable,
            _ => ValidationReasonCodes.RequiredEvidenceMissing
        };
        var explanation = state == ValidationGateDecisionState.Satisfied
            ? "All required independent validation evidence satisfies the configured policy."
            : "The validation gate remains unsatisfied because one or more required independent evidence conditions are not satisfied.";
        return new(plan.ProjectId, decisionId ?? Guid.NewGuid(), plan.Reference, plan.ExecutionRunAuthorityReference,
            plan.CurrentRecoveryCheckpointReference, now, state, decisions, supporting.DistinctBy(static value => value.EvidenceId).ToArray(), reason, explanation);
    }

    private static ValidationRequirementDecision Decision(ValidationRequirement requirement, ValidationEvidence evidence, ValidationRequirementDecisionState state, string reason, string explanation, bool fresh) =>
        new(requirement.RequirementId, requirement.Required ? state : ValidationRequirementDecisionState.NotApplicable,
            new[] { evidence.Reference }, evidence.State, evidence.Outcome, fresh, reason, explanation);

    private static bool MatchesIdentity(ValidationPlan plan, ValidationRequirement requirement, ValidationEvidence value) =>
        MatchesEvidenceIdentity(plan, requirement, value) && value.BaselineRelation == requirement.BaselineRelation;

    private static bool MatchesEvidenceIdentity(ValidationPlan plan, ValidationRequirement requirement, ValidationEvidence value) =>
        value.ProjectId == plan.ProjectId && Same(value.PlanReference, plan.Reference) &&
        value.RunId == plan.ExecutionRunAuthorityReference.RunId && Same(value.ExecutionRunAuthorityReference, plan.ExecutionRunAuthorityReference) &&
        Same(value.PlanningContractReference, plan.PlanningContractReference) && Same(value.CurrentRecoveryCheckpointReference, plan.CurrentRecoveryCheckpointReference) &&
        Same(value.WorkGraphReference, plan.WorkGraphReference) && value.WorkGraphNodeId == plan.WorkGraphNodeId &&
        value.WorkspaceId == plan.WorkspaceId && string.Equals(value.WorkspacePath, plan.WorkspacePath, StringComparison.Ordinal) &&
        string.Equals(value.WorkspaceReceiptContentHash, plan.WorkspaceReceiptContentHash, StringComparison.OrdinalIgnoreCase) &&
        value.RequirementId == requirement.RequirementId && value.ValidationDefinitionId == requirement.ValidationDefinitionId && value.Kind == requirement.EvidenceKind &&
        string.Equals(value.CollectorIdentifier, requirement.CollectorIdentifier, StringComparison.Ordinal) && value.IndependentlyCaptured;

    private static bool IsUsable(ValidationRequirement requirement, ValidationPlan plan, ValidationEvidence value, IReadOnlyList<ValidationEvidence>? baselineEvidence, DateTimeOffset now, ValidationEvidenceSnapshot? currentSnapshot, out string reason)
    {
        reason = ValidationReasonCodes.EvidenceNotUsable;
        if (value.State != ValidationEvidenceState.Available || value.Outcome != ValidationOutcome.Passed) return false;
        if (!value.SecurityBoundaryValid) { reason = ValidationReasonCodes.SecurityBoundaryInvalid; return false; }
        if (requirement.EvidenceKind == ValidationEvidenceKind.Security && (currentSnapshot is null || !currentSnapshot.Matches(value)))
        {
            reason = ValidationReasonCodes.SecurityEvidenceSetMismatch;
            return false;
        }
        var freshness = Freshness(requirement, plan, value, now);
        if (!freshness.IsFresh) { reason = freshness.Reason; return false; }
        if (requirement.Coverage == ValidationCoverageScope.Full && value.Coverage != ValidationCoverageScope.Full)
        {
            reason = ValidationReasonCodes.TargetedEvidenceForFullRequirement;
            return false;
        }
        if (requirement.Coverage == ValidationCoverageScope.Targeted && value.Coverage == ValidationCoverageScope.Full && !requirement.AllowFullEvidenceForTargeted)
        {
            reason = ValidationReasonCodes.TargetedEvidenceForFullRequirement;
            return false;
        }
        if (value.BaselineRelation != requirement.BaselineRelation)
        {
            reason = ValidationReasonCodes.BaselineMissing;
            return false;
        }
        if (requirement.BaselineRelation == ValidationBaselineRelation.Regression &&
            (requirement.BaselineBinding is null || value.BaselineEvidenceReference is null ||
             !Same(value.BaselineEvidenceReference, requirement.BaselineBinding.EvidenceReference) ||
             baselineEvidence is null || !baselineEvidence.Any(candidate =>
                 candidate.ProjectId == requirement.BaselineBinding.PlanReference.ProjectId &&
                 Same(candidate.PlanReference, requirement.BaselineBinding.PlanReference) &&
                 Same(candidate.Reference, requirement.BaselineBinding.EvidenceReference) &&
                 candidate.ValidationDefinitionId == requirement.ValidationDefinitionId &&
                 candidate.Kind == requirement.EvidenceKind && candidate.CollectorIdentifier == requirement.CollectorIdentifier &&
                 candidate.Coverage == requirement.Coverage &&
                 candidate.BaselineRelation == ValidationBaselineRelation.Baseline &&
                 candidate.State == ValidationEvidenceState.Available && candidate.Outcome == ValidationOutcome.Passed &&
                 candidate.IndependentlyCaptured && candidate.SecurityBoundaryValid)))
        {
            reason = ValidationReasonCodes.BaselineMissing;
            return false;
        }
        if (requirement.ExpectedState is not null && value.State != requirement.ExpectedState) return false;
        if (requirement.ExpectedOutcome is not null && value.Outcome != requirement.ExpectedOutcome) return false;
        if (requirement.ExpectedLocalHeadCommitSha is not null && !string.Equals(requirement.ExpectedLocalHeadCommitSha, value.LocalHeadCommitSha, StringComparison.OrdinalIgnoreCase)) { reason = ValidationReasonCodes.RepositoryMismatch; return false; }
        if (requirement.ExpectedBranchName is not null && !string.Equals(requirement.ExpectedBranchName, value.BranchName, StringComparison.Ordinal)) { reason = ValidationReasonCodes.RepositoryMismatch; return false; }
        if (requirement.RequireCleanWorktree == true && value.LocalIsClean != true) { reason = ValidationReasonCodes.RepositoryMismatch; return false; }
        if (requirement.ExpectedRepositoryIdentity is not null && !string.Equals(requirement.ExpectedRepositoryIdentity, value.RepositoryIdentity, StringComparison.Ordinal)) { reason = ValidationReasonCodes.RepositoryMismatch; return false; }
        if (requirement.ExpectedRemoteCommitId is not null && !string.Equals(requirement.ExpectedRemoteCommitId, value.RemoteCommitId, StringComparison.OrdinalIgnoreCase)) { reason = ValidationReasonCodes.RepositoryMismatch; return false; }
        if (requirement.ExpectedTrackerProjectId is not null && !string.Equals(requirement.ExpectedTrackerProjectId, value.TrackerProjectId, StringComparison.OrdinalIgnoreCase)) { reason = ValidationReasonCodes.TrackerMismatch; return false; }
        if (requirement.ExpectedTrackerWorkItemKey is not null && !string.Equals(requirement.ExpectedTrackerWorkItemKey, value.TrackerWorkItemKey, StringComparison.OrdinalIgnoreCase)) { reason = ValidationReasonCodes.TrackerMismatch; return false; }
        if (requirement.ExpectedTrackerStatus is not null && !string.Equals(requirement.ExpectedTrackerStatus, value.TrackerStatus, StringComparison.OrdinalIgnoreCase)) { reason = ValidationReasonCodes.TrackerMismatch; return false; }
        return true;
    }

    private static FreshnessResult Freshness(ValidationRequirement requirement, ValidationPlan plan, ValidationEvidence evidence, DateTimeOffset now)
    {
        if (evidence.CapturedAt > now)
            return new(false, ValidationReasonCodes.EvidenceTimestampInFuture);
        if (evidence.CapturedAt < plan.EvidenceNotBefore)
            return new(false, ValidationReasonCodes.EvidenceBeforeValidationEpoch);
        if (evidence.State == ValidationEvidenceState.Stale)
            return new(false, ValidationReasonCodes.ProviderEvidenceStale);
        if (requirement.MaxAge is not null && evidence.CapturedAt < now - requirement.MaxAge.Value)
            return new(false, ValidationReasonCodes.EvidenceStale);
        return new(true, ValidationReasonCodes.Satisfied);
    }

    private static bool Same(ExecutionRunAuthorityReference left, ExecutionRunAuthorityReference right) => left.RunId == right.RunId && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    private static bool Same(PlanningExecutionContractReference left, PlanningExecutionContractReference right) => left.ContractId == right.ContractId && left.Revision == right.Revision && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    private static bool Same(RecoveryCheckpointReference left, RecoveryCheckpointReference right) => left.CheckpointId == right.CheckpointId && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    private static bool Same(WorkGraphReference? left, WorkGraphReference? right) => left is null && right is null || left is not null && right is not null && left.GraphId == right.GraphId && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    private static bool Same(HandoffPackageReference? left, HandoffPackageReference? right) => left is null && right is null || left is not null && right is not null && left.PackageId == right.PackageId && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    private static bool Same(ValidationPlanReference left, ValidationPlanReference right) => left.ProjectId == right.ProjectId && left.PlanId == right.PlanId && left.Revision == right.Revision && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    private static bool Same(ValidationEvidenceReference left, ValidationEvidenceReference right) => left.EvidenceId == right.EvidenceId && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    private readonly record struct FreshnessResult(bool IsFresh, string Reason);
}

public sealed record ValidationBaselineResolution(bool IsValid, ValidationEvidence? Evidence = null, string? ErrorMessage = null);

public static class ValidationBaselineResolver
{
    public static async Task<ValidationBaselineResolution> ResolveAsync(
        ValidationPlan currentPlan,
        ValidationRequirement requirement,
        IValidationPlanRepository plans,
        IValidationEvidenceRepository evidenceRepository,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (requirement.BaselineRelation != ValidationBaselineRelation.Regression || requirement.BaselineBinding is null)
            return new(false, ErrorMessage: ValidationReasonCodes.BaselineMissing);
        var binding = requirement.BaselineBinding;
        if (binding.PlanReference.ProjectId != currentPlan.ProjectId ||
            !string.Equals(binding.ValidationDefinitionId, requirement.ValidationDefinitionId, StringComparison.Ordinal))
            return new(false, ErrorMessage: ValidationReasonCodes.BaselineMissing);

        var baselinePlanRead = await plans.GetAsync(currentPlan.ProjectId, binding.PlanReference, cancellationToken).ConfigureAwait(false);
        if (!baselinePlanRead.IsValid || baselinePlanRead.Plan is null)
            return new(false, ErrorMessage: "The explicitly bound baseline plan is unavailable or invalid.");
        var baselinePlan = baselinePlanRead.Plan;
        var baselineRead = await evidenceRepository.GetAsync(currentPlan.ProjectId, binding.PlanReference, binding.EvidenceReference, cancellationToken).ConfigureAwait(false);
        if (!baselineRead.IsValid || baselineRead.Evidence is null)
            return new(false, ErrorMessage: "The explicitly bound baseline evidence is unavailable or invalid.");
        var baseline = baselineRead.Evidence;
        var baselineRequirement = baselinePlan.Requirements.FirstOrDefault(value => value.RequirementId == baseline.RequirementId);
        var valid = Same(baselinePlan.Reference, binding.PlanReference) &&
            Same(baseline.PlanReference, binding.PlanReference) && Same(baseline.Reference, binding.EvidenceReference) && baseline.ProjectId == currentPlan.ProjectId &&
            baseline.ValidationDefinitionId == requirement.ValidationDefinitionId && baselineRequirement is not null &&
            baselineRequirement.BaselineRelation == ValidationBaselineRelation.Baseline &&
            ValidationRequirementCompatibility.AreCompatible(requirement, baselineRequirement) &&
            baseline.BaselineRelation == ValidationBaselineRelation.Baseline &&
            baseline.State == ValidationEvidenceState.Available && baseline.Outcome == ValidationOutcome.Passed &&
            baseline.IndependentlyCaptured && baseline.SecurityBoundaryValid && baseline.CapturedAt <= now;
        return valid ? new(true, baseline) : new(false, ErrorMessage: "The explicitly bound baseline does not match the current validation definition.");
    }

    private static bool Same(ValidationPlanReference left, ValidationPlanReference right) =>
        left.ProjectId == right.ProjectId && left.PlanId == right.PlanId && left.Revision == right.Revision &&
        left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static bool Same(ValidationEvidenceReference left, ValidationEvidenceReference right) =>
        left.EvidenceId == right.EvidenceId && left.SchemaVersion == right.SchemaVersion &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
}

public sealed class ValidationEvidenceService : IValidationEvidenceService
{
    private readonly IValidationPlanRepository _plans;
    private readonly IValidationEvidenceRepository _evidence;
    private readonly IProjectRepository _projects;
    private readonly IExecutionRunAuthorityRepository _authorities;
    private readonly IWorkspacePreparationReceiptRepository _receipts;
    private readonly IRecoveryCheckpointRepository _checkpoints;
    private readonly IContinuationHeadRepository _heads;
    private readonly IValidationEvidenceCollectorResolver _collectors;
    private readonly IClock _clock;

    public ValidationEvidenceService(IValidationPlanRepository plans, IValidationEvidenceRepository evidence, IProjectRepository projects, IExecutionRunAuthorityRepository authorities, IWorkspacePreparationReceiptRepository receipts, IRecoveryCheckpointRepository checkpoints, IContinuationHeadRepository heads, IValidationEvidenceCollectorResolver collectors, IClock clock)
    {
        _plans = plans ?? throw new ArgumentNullException(nameof(plans)); _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence)); _projects = projects ?? throw new ArgumentNullException(nameof(projects)); _authorities = authorities ?? throw new ArgumentNullException(nameof(authorities)); _receipts = receipts ?? throw new ArgumentNullException(nameof(receipts)); _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints)); _heads = heads ?? throw new ArgumentNullException(nameof(heads)); _collectors = collectors ?? throw new ArgumentNullException(nameof(collectors)); _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ValidationPlanRepositoryWriteResult> CreatePlanAsync(ValidationPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var now = _clock.UtcNow;
        if (plan.CreatedAt > now) return new(ValidationPlanRepositoryWriteStatus.PlanConflict, "Validation plan is dated in the future.");
        var authorities = await _authorities.GetAsync(plan.ProjectId, plan.ExecutionRunAuthorityReference.RunId, cancellationToken).ConfigureAwait(false);
        var receipt = await _receipts.GetAsync(plan.ProjectId, plan.WorkspaceId, cancellationToken).ConfigureAwait(false);
        var checkpoint = await _checkpoints.GetAsync(plan.ProjectId, plan.CurrentRecoveryCheckpointReference.CheckpointId, cancellationToken).ConfigureAwait(false);
        if (!authorities.IsValid || authorities.Authority is null || receipt.State != WorkspacePreparationReceiptReadState.Valid || receipt.Receipt is null || !checkpoint.IsValid || checkpoint.Checkpoint is null)
            return new(ValidationPlanRepositoryWriteStatus.Unavailable, "Validation authorities are missing or invalid.");
        var binding = await ValidationAuthorityBindingValidator.ValidateAsync(plan, authorities.Authority, receipt.Receipt, checkpoint.Checkpoint, _checkpoints, _heads, cancellationToken).ConfigureAwait(false);
        if (!binding.IsValid)
            return new(ValidationPlanRepositoryWriteStatus.PlanConflict, "Validation plan authority binding is invalid.");
        foreach (var requirement in plan.Requirements.Where(value => value.BaselineRelation == ValidationBaselineRelation.Regression))
        {
            var baseline = await ValidationBaselineResolver.ResolveAsync(plan, requirement, _plans, _evidence, now, cancellationToken).ConfigureAwait(false);
            if (!baseline.IsValid) return new(ValidationPlanRepositoryWriteStatus.PlanConflict, baseline.ErrorMessage);
        }
        return await _plans.CreateAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ValidationCaptureResult> CaptureAsync(ValidationCaptureRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProjectId != request.PlanReference.ProjectId) return new(null, "The validation project does not match the exact plan reference.");
        var planRead = await _plans.GetAsync(request.ProjectId, request.PlanReference, cancellationToken).ConfigureAwait(false);
        if (!planRead.IsValid || planRead.Plan is null) return new(null, "The exact validation plan is unavailable.");
        var plan = planRead.Plan;
        var now = _clock.UtcNow;
        if (plan.CreatedAt > now || plan.EvidenceNotBefore > now) return new(null, "The validation plan is dated in the future.");
        var requirement = plan.Requirements.FirstOrDefault(value => value.RequirementId == request.RequirementId);
        if (requirement is null || !Same(plan.CurrentRecoveryCheckpointReference, request.CurrentRecoveryCheckpointReference)) return new(null, "The validation requirement or current checkpoint does not match the plan.");
        var project = await _projects.GetByIdAsync(plan.ProjectId, cancellationToken).ConfigureAwait(false);
        var authority = await _authorities.GetAsync(plan.ProjectId, plan.ExecutionRunAuthorityReference.RunId, cancellationToken).ConfigureAwait(false);
        var receipt = await _receipts.GetAsync(plan.ProjectId, plan.WorkspaceId, cancellationToken).ConfigureAwait(false);
        var checkpoint = await _checkpoints.GetAsync(plan.ProjectId, plan.CurrentRecoveryCheckpointReference.CheckpointId, cancellationToken).ConfigureAwait(false);
        if (project is null || !authority.IsValid || authority.Authority is null || receipt.State != WorkspacePreparationReceiptReadState.Valid || receipt.Receipt is null || !checkpoint.IsValid || checkpoint.Checkpoint is null)
            return new(null, "Validation authorities are missing or invalid.");
        var binding = await ValidationAuthorityBindingValidator.ValidateAsync(plan, authority.Authority, receipt.Receipt, checkpoint.Checkpoint, _checkpoints, _heads, cancellationToken).ConfigureAwait(false);
        if (!binding.IsValid)
            return new(null, "Validation authority identity or validation checkpoint does not match the exact plan.");
        var existingRead = await _evidence.GetForPlanAsync(plan.ProjectId, plan.Reference, cancellationToken).ConfigureAwait(false);
        if (!existingRead.IsComplete) return new(null, existingRead.ErrorMessage ?? "Validation evidence set is incomplete or untrusted.");
        var existing = existingRead.Evidence!;
        if (existing.Any(value => value.RequirementId == requirement.RequirementId && value.Kind == requirement.EvidenceKind && value.CollectorIdentifier == requirement.CollectorIdentifier))
            return new(null, "This immutable validation plan already has evidence for the requested requirement.");
        if (requirement.EvidenceKind != ValidationEvidenceKind.Security && existing.Any(value => value.Kind == ValidationEvidenceKind.Security))
            return new(null, "No non-Security evidence may be captured after the Security boundary.");
        if (requirement.EvidenceKind == ValidationEvidenceKind.Security && !HasRequiredNonSecurityEvidence(plan, existing))
            return new(null, "Security evidence requires a complete immutable result for every required non-Security requirement.");
        ValidationEvidence? baselineEvidence = null;
        if (requirement.BaselineRelation == ValidationBaselineRelation.Regression)
        {
            var baseline = await ValidationBaselineResolver.ResolveAsync(plan, requirement, _plans, _evidence, now, cancellationToken).ConfigureAwait(false);
            if (!baseline.IsValid) return new(null, baseline.ErrorMessage);
            baselineEvidence = baseline.Evidence;
        }
        var resolution = _collectors.Resolve(requirement.CollectorIdentifier, requirement.EvidenceKind);
        if (!resolution.Succeeded || resolution.Collector is null) return new(null, resolution.ErrorMessage ?? "The validation collector is unsupported.");
        var value = await resolution.Collector.CaptureAsync(new ValidationCollectionContext(plan, requirement, project, authority.Authority, receipt.Receipt, checkpoint.Checkpoint, existing, baselineEvidence?.Reference), cancellationToken).ConfigureAwait(false);
        if (!Same(value.PlanReference, plan.Reference) || value.RequirementId != requirement.RequirementId || value.ValidationDefinitionId != requirement.ValidationDefinitionId || value.BaselineRelation != requirement.BaselineRelation || (requirement.BaselineBinding is not null && !Same(value.BaselineEvidenceReference, requirement.BaselineBinding.EvidenceReference)))
            return new(null, "The collector returned evidence outside the exact immutable validation binding.");
        if (requirement.EvidenceKind == ValidationEvidenceKind.Security &&
            (!ValidationEvidenceSnapshot.TryCreate(existing, out var snapshot) || snapshot is null || !snapshot.Matches(value)))
            return new(null, "The Security evidence snapshot does not match the exact pre-Security evidence set.");
        var write = await _evidence.CreateAsync(value, cancellationToken).ConfigureAwait(false);
        return write.Succeeded ? new(value) : new(null, write.ErrorMessage ?? "Validation evidence persistence failed.");
    }

    private static bool HasRequiredNonSecurityEvidence(ValidationPlan plan, IReadOnlyList<ValidationEvidence> evidence) =>
        plan.Requirements.Where(value => value.Required && value.EvidenceKind != ValidationEvidenceKind.Security)
            .All(requirement => evidence.Any(value =>
                value.ProjectId == plan.ProjectId && Same(value.PlanReference, plan.Reference) &&
                value.RunId == plan.ExecutionRunAuthorityReference.RunId &&
                Same(value.ExecutionRunAuthorityReference, plan.ExecutionRunAuthorityReference) &&
                Same(value.PlanningContractReference, plan.PlanningContractReference) &&
                Same(value.CurrentRecoveryCheckpointReference, plan.CurrentRecoveryCheckpointReference) &&
                Same(value.WorkGraphReference, plan.WorkGraphReference) && value.WorkGraphNodeId == plan.WorkGraphNodeId &&
                value.WorkspaceId == plan.WorkspaceId && string.Equals(value.WorkspacePath, plan.WorkspacePath, StringComparison.Ordinal) &&
                string.Equals(value.WorkspaceReceiptContentHash, plan.WorkspaceReceiptContentHash, StringComparison.OrdinalIgnoreCase) &&
                value.RequirementId == requirement.RequirementId && value.ValidationDefinitionId == requirement.ValidationDefinitionId &&
                value.Kind == requirement.EvidenceKind && string.Equals(value.CollectorIdentifier, requirement.CollectorIdentifier, StringComparison.Ordinal) &&
                value.BaselineRelation == requirement.BaselineRelation && value.IndependentlyCaptured));

    private static bool Same(ValidationPlanReference left, ValidationPlanReference right) => left.ProjectId == right.ProjectId && left.PlanId == right.PlanId && left.Revision == right.Revision && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    private static bool Same(ValidationEvidenceReference? left, ValidationEvidenceReference? right) => left is null && right is null || left is not null && right is not null && left.EvidenceId == right.EvidenceId && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    private static bool Same(ExecutionRunAuthorityReference left, ExecutionRunAuthorityReference right) => left.RunId == right.RunId && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    private static bool Same(PlanningExecutionContractReference left, PlanningExecutionContractReference right) => left.ContractId == right.ContractId && left.Revision == right.Revision && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    private static bool Same(WorkGraphReference? left, WorkGraphReference? right) => left is null && right is null || left is not null && right is not null && left.GraphId == right.GraphId && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    private static bool Same(HandoffPackageReference? left, HandoffPackageReference? right) => left is null && right is null || left is not null && right is not null && left.PackageId == right.PackageId && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    private static bool Same(RecoveryCheckpointReference left, RecoveryCheckpointReference right) => left.CheckpointId == right.CheckpointId && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
}

public sealed class ValidationGateService : IValidationGateService
{
    private readonly IValidationPlanRepository _plans;
    private readonly IValidationEvidenceRepository _evidence;
    private readonly IValidationGateDecisionRepository _decisions;
    private readonly IExecutionRunAuthorityRepository _authorities;
    private readonly IWorkspacePreparationReceiptRepository _receipts;
    private readonly IRecoveryCheckpointRepository _checkpoints;
    private readonly IContinuationHeadRepository _heads;
    private readonly IRecoveryCheckpointService _recovery;
    private readonly IClock _clock;

    public ValidationGateService(IValidationPlanRepository plans, IValidationEvidenceRepository evidence, IValidationGateDecisionRepository decisions, IExecutionRunAuthorityRepository authorities, IWorkspacePreparationReceiptRepository receipts, IRecoveryCheckpointRepository checkpoints, IContinuationHeadRepository heads, IRecoveryCheckpointService recovery, IClock clock)
    {
        _plans = plans ?? throw new ArgumentNullException(nameof(plans)); _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence)); _decisions = decisions ?? throw new ArgumentNullException(nameof(decisions)); _authorities = authorities ?? throw new ArgumentNullException(nameof(authorities)); _receipts = receipts ?? throw new ArgumentNullException(nameof(receipts)); _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints)); _heads = heads ?? throw new ArgumentNullException(nameof(heads)); _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery)); _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ValidationGateEvaluationResult> EvaluateAsync(ValidationGateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProjectId != request.PlanReference.ProjectId) return new(null, ErrorMessage: "The validation project does not match the exact plan reference.");
        var planRead = await _plans.GetAsync(request.ProjectId, request.PlanReference, cancellationToken).ConfigureAwait(false);
        if (!planRead.IsValid || planRead.Plan is null) return new(null, ErrorMessage: "The exact validation plan is unavailable.");
        var plan = planRead.Plan;
        var now = _clock.UtcNow;
        if (plan.CreatedAt > now || plan.EvidenceNotBefore > now) return new(null, ErrorMessage: "The validation plan is dated in the future.");
        if (!Same(plan.CurrentRecoveryCheckpointReference, request.CurrentRecoveryCheckpointReference)) return new(null, ErrorMessage: "The current recovery checkpoint does not match the validation plan.");
        var authority = await _authorities.GetAsync(plan.ProjectId, plan.ExecutionRunAuthorityReference.RunId, cancellationToken).ConfigureAwait(false);
        var receipt = await _receipts.GetAsync(plan.ProjectId, plan.WorkspaceId, cancellationToken).ConfigureAwait(false);
        var checkpoint = await _checkpoints.GetAsync(plan.ProjectId, plan.CurrentRecoveryCheckpointReference.CheckpointId, cancellationToken).ConfigureAwait(false);
        if (!authority.IsValid || authority.Authority is null || receipt.State != WorkspacePreparationReceiptReadState.Valid || receipt.Receipt is null || !checkpoint.IsValid || checkpoint.Checkpoint is null)
            return new(null, ErrorMessage: "The exact validation authority or validation checkpoint is unavailable.");
        var binding = await ValidationAuthorityBindingValidator.ValidateAsync(plan, authority.Authority, receipt.Receipt, checkpoint.Checkpoint, _checkpoints, _heads, cancellationToken).ConfigureAwait(false);
        if (!binding.IsValid)
            return new(null, ErrorMessage: "The exact validation authority or validation checkpoint is unavailable.");
        var evidenceRead = await _evidence.GetForPlanAsync(plan.ProjectId, plan.Reference, cancellationToken).ConfigureAwait(false);
        if (!evidenceRead.IsComplete) return new(null, ErrorMessage: evidenceRead.ErrorMessage ?? "Validation evidence set is incomplete or untrusted.");
        var evidence = evidenceRead.Evidence!;
        var baselines = new List<ValidationEvidence>();
        foreach (var requirement in plan.Requirements.Where(value => value.BaselineRelation == ValidationBaselineRelation.Regression))
        {
            var baseline = await ValidationBaselineResolver.ResolveAsync(plan, requirement, _plans, _evidence, now, cancellationToken).ConfigureAwait(false);
            if (!baseline.IsValid) return new(null, ErrorMessage: baseline.ErrorMessage);
            baselines.Add(baseline.Evidence!);
        }
        var decision = ValidationGateEvaluator.Evaluate(plan, evidence, now, baselineEvidence: baselines);
        var refs = checkpoint.Checkpoint.EvidenceReferences.ToList();
        foreach (var value in evidence.Where(value => decision.SupportingEvidence.Any(reference => reference.EvidenceId == value.EvidenceId)))
        {
            if (refs.All(reference => reference.EvidenceId != value.EvidenceId))
            {
                var requirementDecision = decision.RequirementDecisions.FirstOrDefault(item => item.RequirementId == value.RequirementId);
                var freshness = requirementDecision?.ReasonCode == ValidationReasonCodes.EvidenceTimestampInFuture || requirementDecision?.ReasonCode == ValidationReasonCodes.EvidenceBeforeValidationEpoch
                    ? RecoveryEvidenceFreshness.Unknown
                    : requirementDecision?.Fresh == true && value.State == ValidationEvidenceState.Available
                        ? RecoveryEvidenceFreshness.Verified
                        : RecoveryEvidenceFreshness.Stale;
                refs.Add(new RecoveryEvidenceReference(value.EvidenceId, RecoveryEvidenceKind.Validation, value.Reference.ToString(), value.CapturedAt, freshness, contentHash: value.ContentHash));
            }
        }
        var gates = checkpoint.Checkpoint.GateSnapshots.Where(value => value.Kind != RecoveryGateKind.Validation).ToList();
        gates.Add(new RecoveryGateSnapshot(RecoveryGateKind.Validation, decision.State switch { ValidationGateDecisionState.Satisfied => RecoveryGateState.Satisfied, ValidationGateDecisionState.Failed => RecoveryGateState.Failed, _ => RecoveryGateState.Pending }, decision.SupportingEvidence.Select(value => value.EvidenceId).ToArray()));
        var blockers = checkpoint.Checkpoint.Blockers.Where(value => !value.BlockerId.StartsWith("validation-gate-", StringComparison.Ordinal)).ToList();
        var lifecycle = decision.State switch { ValidationGateDecisionState.Satisfied => RecoveryCheckpointLifecycleState.Ready, ValidationGateDecisionState.Failed => RecoveryCheckpointLifecycleState.Blocked, _ => RecoveryCheckpointLifecycleState.Waiting };
        var action = decision.State == ValidationGateDecisionState.Satisfied ? RecoveryNextSafeAction.ContinueFromCheckpoint : decision.State == ValidationGateDecisionState.Failed ? RecoveryNextSafeAction.ResolveBlocker : RecoveryNextSafeAction.RunValidation;
        if (decision.State != ValidationGateDecisionState.Satisfied) blockers.Add(new RecoveryBlocker($"validation-gate-{decision.DecisionId:N}", RecoveryBlockerKind.ValidationGate, decision.Explanation ?? "Validation gate is unsatisfied.", decision.Reference.ToString()));

        if (decision.State == ValidationGateDecisionState.Satisfied &&
            !RecoveryCheckpointLimits.TryValidateEvidenceReferences(refs, out var capacityError))
        {
            return new(null, ErrorMessage: capacityError ?? "The validation recovery checkpoint cannot represent its evidence references.");
        }

        var write = await _decisions.CreateAsync(decision, cancellationToken).ConfigureAwait(false);
        if (!write.Succeeded) return new(decision, ErrorMessage: write.ErrorMessage ?? "Validation-decision persistence failed.");

        var recovery = await _recovery.CreateAsync(new RecoveryCheckpointCreationRequest(plan.ProjectId, Guid.NewGuid(), lifecycle, plan.PlanningContractReference, refs, gates, blockers, action, decision.Explanation, _clock.UtcNow, plan.WorkGraphReference, plan.WorkGraphNodeId, plan.HandoffPackageReference, previousCheckpointReference: checkpoint.Checkpoint.Reference, selectedAgentRoleReferences: checkpoint.Checkpoint.SelectedAgentRoleReferences), cancellationToken).ConfigureAwait(false);
        return new(decision, recovery, recovery.Succeeded ? null : recovery.ErrorMessage);
    }

    private static bool Same(ValidationPlanReference left, ValidationPlanReference right) => left.ProjectId == right.ProjectId && left.PlanId == right.PlanId && left.Revision == right.Revision && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
    private static bool Same(RecoveryCheckpointReference left, RecoveryCheckpointReference right) => left.CheckpointId == right.CheckpointId && left.SchemaVersion == right.SchemaVersion && string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
}
