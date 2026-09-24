namespace AIUsageMonitor.Application.Orchestration;

public enum ExecutionRunAuthorityRepositoryWriteStatus
{
    Created,
    RunConflict,
    InputCheckpointConflict,
    Unavailable
}

public sealed record ExecutionRunAuthorityRepositoryWriteResult(
    ExecutionRunAuthorityRepositoryWriteStatus Status,
    string? ErrorMessage = null)
{
    public bool Succeeded => Status == ExecutionRunAuthorityRepositoryWriteStatus.Created;
}

public enum ExecutionRunAuthorityReadState
{
    Missing,
    Valid,
    UnsupportedFutureVersion,
    MigrationRequired,
    Invalid,
    IntegrityFailure,
    Unavailable
}

public sealed record ExecutionRunAuthorityReadResult(
    ExecutionRunAuthorityReadState State,
    ExecutionRunAuthority? Authority = null,
    string? ErrorMessage = null)
{
    public bool IsValid => State == ExecutionRunAuthorityReadState.Valid && Authority is not null;
}

/// <summary>
/// Create-once, project-isolated persistence for the durable anti-replay authority. Implementers
/// must never overwrite, delete, repair, or silently migrate an existing authority.
/// </summary>
public interface IExecutionRunAuthorityRepository
{
    Task<ExecutionRunAuthorityRepositoryWriteResult> CreateAsync(
        ExecutionRunAuthority authority,
        CancellationToken cancellationToken = default);

    Task<ExecutionRunAuthorityReadResult> GetAsync(
        Guid projectId,
        Guid runId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the one durable execution claim for an immutable Ready checkpoint.</summary>
    Task<ExecutionRunAuthorityReadResult> GetByInputCheckpointAsync(
        Guid projectId,
        RecoveryCheckpointReference inputCheckpointReference,
        CancellationToken cancellationToken = default);
}
