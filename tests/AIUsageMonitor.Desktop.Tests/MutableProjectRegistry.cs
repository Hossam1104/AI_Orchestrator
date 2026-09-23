using AIUsageMonitor.Application.Projects;

namespace AIUsageMonitor.Desktop.Tests;

/// <summary>
/// IProjectRegistryService test double whose backing list can grow after construction, so a test
/// can simulate a project being registered through the same registry boundary the product uses
/// while a consuming view model already holds an earlier snapshot. The fixed-list fakes elsewhere
/// in this suite cannot express that same-process synchronization scenario.
/// </summary>
internal sealed class MutableProjectRegistry : IProjectRegistryService
{
    private readonly List<Project> _projects;

    public MutableProjectRegistry(params Project[] projects) => _projects = [.. projects];

    public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Project>>([.. _projects]);

    public Task<Project> CreateProjectAsync(ProjectEdit edit, CancellationToken cancellationToken = default)
    {
        var project = new Project(
            Guid.NewGuid(),
            edit.Name,
            edit.LocalPath,
            edit.DefaultBranch,
            edit.Status,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            edit.RepositoryProvider,
            edit.RepositoryUrl,
            edit.RepositoryId,
            edit.RepositoryMetadata,
            edit.TrackerType,
            edit.TrackerId,
            edit.TrackerMetadata,
            edit.GovernanceReferences,
            edit.RoutingPolicyReference,
            edit.SafetyPolicyReference);
        _projects.Add(project);
        return Task.FromResult(project);
    }

    public Task<Project> UpdateProjectAsync(Guid projectId, ProjectEdit edit, CancellationToken cancellationToken = default)
    {
        var index = _projects.FindIndex(p => p.Id == projectId);
        if (index < 0)
        {
            throw new KeyNotFoundException("The project no longer exists.");
        }

        var existing = _projects[index];
        var updated = new Project(
            existing.Id,
            edit.Name,
            edit.LocalPath,
            edit.DefaultBranch,
            edit.Status,
            existing.CreatedAt,
            DateTimeOffset.UtcNow,
            edit.RepositoryProvider,
            edit.RepositoryUrl,
            edit.RepositoryId,
            edit.RepositoryMetadata ?? existing.RepositoryMetadata,
            edit.TrackerType,
            edit.TrackerId,
            edit.TrackerMetadata ?? existing.TrackerMetadata,
            edit.GovernanceReferences,
            edit.RoutingPolicyReference,
            edit.SafetyPolicyReference);
        _projects[index] = updated;
        return Task.FromResult(updated);
    }

    public void Remove(Guid projectId) => _projects.RemoveAll(p => p.Id == projectId);
}
