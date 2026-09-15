using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Time;

namespace AIUsageMonitor.Application.Projects;

public sealed class ProjectOnboardingService : IProjectOnboardingService
{
    private readonly IProjectRegistryService _projects;
    private readonly ILocalRepositoryInspector _inspector;
    private readonly IAgentRepository _agents;
    private readonly IDefaultAgentCatalog _catalog;
    private readonly IAgentProjectOverrideRepository _overrides;
    private readonly IAgentRegistryService _agentRegistry;
    private readonly IProjectContextReferenceRepository _contexts;
    private readonly IClock _clock;
    private readonly IProjectFolderPreferenceService? _folderPreferences;

    public ProjectOnboardingService(
        IProjectRegistryService projects,
        ILocalRepositoryInspector inspector,
        IAgentRepository agents,
        IDefaultAgentCatalog catalog,
        IAgentProjectOverrideRepository overrides,
        IAgentRegistryService agentRegistry,
        IProjectContextReferenceRepository contexts,
        IClock clock,
        IProjectFolderPreferenceService? folderPreferences = null)
    {
        _folderPreferences = folderPreferences;
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<LocalRepositoryInspection> InspectRepositoryAsync(
        string localPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            throw new ArgumentException("Local path is required.", nameof(localPath));
        }

        return _inspector.InspectAsync(localPath.Trim(), cancellationToken: cancellationToken);
    }

    public async Task<ProjectOnboardingResult> CompleteAsync(
        ProjectOnboardingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Project? createdProject = null;

        try
        {
            ValidateRequest(request);
            var existing = (await _projects.GetProjectsAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(project => ProjectPathComparer.EqualsCanonical(project.LocalPath, request.LocalPath));
            if (existing is not null)
            {
                return ProjectOnboardingResult.AlreadyRegistered(existing);
            }

            var defaults = _catalog.GetDefaults();
            var enabledAgentIds = (request.EnabledAgentIds ?? defaults.Select(agent => agent.Id)).ToHashSet();

            await EnsureDefaultAgentsAsync(defaults, cancellationToken).ConfigureAwait(false);

            var edit = BuildProjectEdit(request);
            createdProject = await _projects.CreateProjectAsync(edit, cancellationToken).ConfigureAwait(false);

            foreach (var defaultAgent in defaults)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!enabledAgentIds.Contains(defaultAgent.Id))
                {
                    // An onboarding choice can only restrict a global definition. It never grants
                    // a disabled global agent or introduces a role/mode absent from the catalog.
                    await _overrides.UpsertAsync(
                        new AgentProjectOverride(createdProject.Id, defaultAgent.Id, enabledOverride: false),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            var context = await BuildContextAsync(
                    createdProject,
                    request,
                    defaults,
                    cancellationToken)
                .ConfigureAwait(false);
            await _contexts.UpsertAsync(context, cancellationToken).ConfigureAwait(false);

            // Only a fully completed registration counts as a successful use of a folder. An
            // already-registered, partial, or failed onboarding must not move the picker default.
            await RecordSuccessfulFolderAsync(createdProject.LocalPath, cancellationToken).ConfigureAwait(false);
            return ProjectOnboardingResult.Success(createdProject, context);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (createdProject is not null)
            {
                return ProjectOnboardingResult.PartialProjectCreated(
                    createdProject,
                    "The project was created, but onboarding was cancelled before its context was completed.");
            }

            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var message = exception is ArgumentException
                ? exception.Message
                : "Project onboarding could not be completed safely.";
            return createdProject is null
                ? ProjectOnboardingResult.FailedBeforeProjectCreation(message)
                : ProjectOnboardingResult.PartialProjectCreated(createdProject, message);
        }
    }

    private async Task RecordSuccessfulFolderAsync(string localPath, CancellationToken cancellationToken)
    {
        if (_folderPreferences is null)
        {
            return;
        }

        try
        {
            await _folderPreferences
                .RecordSuccessfulProjectFolderAsync(localPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The project is registered and its context is written; a picker convenience
            // preference must never downgrade that proven outcome to a partial result.
        }
    }

    private async Task EnsureDefaultAgentsAsync(
        IReadOnlyList<AgentDefinition> defaults,
        CancellationToken cancellationToken)
    {
        foreach (var defaultAgent in defaults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = await _agents.GetByIdAsync(defaultAgent.Id, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                await _agents.UpsertAsync(defaultAgent, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<ProjectContextReference> BuildContextAsync(
        Project project,
        ProjectOnboardingRequest request,
        IReadOnlyList<AgentDefinition> defaults,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var effectiveAgents = await _agentRegistry
            .GetEffectiveAgentsAsync(project.Id, cancellationToken)
            .ConfigureAwait(false);
        var effectiveById = effectiveAgents.ToDictionary(agent => agent.Id);
        var modelReferences = new List<ProjectModelRoleReference>(defaults.Count);
        foreach (var defaultAgent in defaults)
        {
            if (!effectiveById.TryGetValue(defaultAgent.Id, out var effective))
            {
                throw new InvalidOperationException("A seeded default agent could not be resolved.");
            }

            modelReferences.Add(new ProjectModelRoleReference(
                effective.Id,
                effective.Name,
                effective.RoleCapabilities,
                effective.Enabled,
                effective.Availability,
                effective.AuthenticationState,
                effective.EntitlementState));
        }

        var repository = request.SkipRepository
            ? ProjectRepositoryContextReference.Skipped(project.Id, project.LocalPath)
            : ProjectRepositoryContextReference.FromInspection(project.Id, request.RepositoryInspection!);
        var tracker = request.SkipTracker
            ? new ProjectTrackerContextReference(TrackerReferenceState.Skipped)
            : new ProjectTrackerContextReference(
                TrackerReferenceState.ConfiguredUnverified,
                request.TrackerType,
                request.TrackerReference);

        return new ProjectContextReference(
            project.Id,
            Guid.NewGuid(),
            ProjectContextContract.CurrentVersion,
            now,
            now,
            repository,
            tracker,
            modelReferences,
            new ProjectCurrentWorkReference(CurrentWorkState.NotSelected),
            project.GovernanceReferences,
            project.RoutingPolicyReference,
            project.SafetyPolicyReference,
            ProjectNextSafeAction.ReadyForPlanning);
    }

    private static ProjectEdit BuildProjectEdit(ProjectOnboardingRequest request)
    {
        if (request.SkipRepository)
        {
            return new ProjectEdit
            {
                Name = request.Name,
                LocalPath = request.LocalPath,
                Status = ProjectStatus.Active,
                TrackerType = request.SkipTracker ? null : request.TrackerType,
                TrackerId = request.SkipTracker ? null : request.TrackerReference,
                TrackerMetadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["integrationState"] = request.SkipTracker ? "Skipped" : "ConfiguredUnverified"
                },
                GovernanceReferences = request.RepositoryInspection?.Workspace?.PresentGovernanceFiles
            };
        }

        var inspection = request.RepositoryInspection!;
        var defaultBranch = string.IsNullOrWhiteSpace(request.RepositoryDefaultBranch)
            ? inspection.BranchName
            : request.RepositoryDefaultBranch.Trim();
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["integrationState"] = "VerifiedLocal",
            ["verificationStatus"] = inspection.Status.ToString(),
            ["repositoryRoot"] = inspection.RepositoryRoot,
            ["localPathIsRepositoryRoot"] = inspection.LocalPathIsRepositoryRoot?.ToString(),
            ["branchCapturedAt"] = inspection.CapturedAt.ToString("O")
        };

        if (inspection.Workspace is { } workspace)
        {
            metadata["workspaceDisplayName"] = workspace.DisplayName;
            metadata["workspaceReadable"] = workspace.IsReadable.ToString();
            metadata["workspaceRemoteProvider"] = workspace.RemoteProvider;
            metadata["workspaceGovernanceFiles"] = string.Join(", ", workspace.PresentGovernanceFiles);
            metadata["workspaceProjectFiles"] = string.Join(", ", workspace.ProjectFiles);
        }

        for (var index = 0; index < inspection.Remotes.Count; index++)
        {
            var remote = inspection.Remotes[index];
            metadata[$"remote.{index}.{remote.Name}"] = remote.SanitizedUrl;
        }

        return new ProjectEdit
        {
            Name = request.Name,
            LocalPath = request.LocalPath,
            Status = ProjectStatus.Active,
            RepositoryProvider = InferRepositoryProvider(inspection.Remotes),
            RepositoryUrl = inspection.Remotes.FirstOrDefault(remote =>
                string.Equals(remote.Name, "origin", StringComparison.OrdinalIgnoreCase))?.SanitizedUrl,
            DefaultBranch = defaultBranch,
            RepositoryMetadata = metadata,
            GovernanceReferences = inspection.Workspace?.PresentGovernanceFiles,
            TrackerType = request.SkipTracker ? null : request.TrackerType,
            TrackerId = request.SkipTracker ? null : request.TrackerReference,
            TrackerMetadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["integrationState"] = request.SkipTracker ? "Skipped" : "ConfiguredUnverified"
            }
        };
    }

    private static string InferRepositoryProvider(IReadOnlyList<RepositoryRemote> remotes)
    {
        var urls = remotes.Select(remote => remote.SanitizedUrl).ToArray();
        if (urls.Any(url => url.Contains("github.com", StringComparison.OrdinalIgnoreCase)))
        {
            return "GitHub";
        }

        if (urls.Any(url =>
                url.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("ssh.dev.azure.com", StringComparison.OrdinalIgnoreCase)))
        {
            return "Azure Repos";
        }

        return urls.Length == 0 ? "Local Git" : "Other / Unknown";
    }

    private static void ValidateRequest(ProjectOnboardingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Project name is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.LocalPath))
        {
            throw new ArgumentException("Local path is required.", nameof(request));
        }

        if (!request.SkipRepository)
        {
            var inspection = request.RepositoryInspection
                ?? throw new ArgumentException("Repository inspection is required or must be explicitly skipped.", nameof(request));
            if (inspection.Status is not (RepositoryVerificationStatus.AvailableClean or RepositoryVerificationStatus.AvailableDirty))
            {
                throw new ArgumentException("Only a verified local repository can be accepted; choose skip for other states.", nameof(request));
            }

            if (!string.Equals(
                    inspection.RegisteredLocalPath.Trim(),
                    request.LocalPath.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Repository inspection does not belong to the selected local workspace.", nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.RepositoryDefaultBranch) &&
                string.IsNullOrWhiteSpace(inspection.BranchName))
            {
                throw new ArgumentException("A usable repository branch is required, or continue without repository integration.", nameof(request));
            }
        }

        if (!request.SkipTracker &&
            (string.IsNullOrWhiteSpace(request.TrackerType) || string.IsNullOrWhiteSpace(request.TrackerReference)))
        {
            throw new ArgumentException("A configured tracker requires a bounded type and reference.", nameof(request));
        }
    }
}
