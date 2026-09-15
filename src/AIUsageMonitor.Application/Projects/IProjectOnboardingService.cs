namespace AIUsageMonitor.Application.Projects;

public sealed class ProjectOnboardingRequest
{
    public string Name { get; init; } = string.Empty;
    public string LocalPath { get; init; } = string.Empty;
    public bool SkipRepository { get; init; }
    public LocalRepositoryInspection? RepositoryInspection { get; init; }
    public string? RepositoryDefaultBranch { get; init; }
    public bool SkipTracker { get; init; } = true;
    public string? TrackerType { get; init; }
    public string? TrackerReference { get; init; }
    public IReadOnlyCollection<Guid>? EnabledAgentIds { get; init; }
}

public enum ProjectOnboardingCompletionStatus
{
    Succeeded,
    FailedBeforeProjectCreation,
    PartialProjectCreated,
    AlreadyRegistered
}

public sealed class ProjectOnboardingResult
{
    private ProjectOnboardingResult(
        ProjectOnboardingCompletionStatus status,
        Project? project,
        ProjectContextReference? context,
        string? errorMessage,
        Project? existingProject = null)
    {
        Status = status;
        Project = project;
        ExistingProject = existingProject;
        Context = context;
        ErrorMessage = errorMessage;
    }

    public ProjectOnboardingCompletionStatus Status { get; }
    public bool Succeeded => Status == ProjectOnboardingCompletionStatus.Succeeded;
    public bool IsPartialProjectCreated => Status == ProjectOnboardingCompletionStatus.PartialProjectCreated;
    public Project? Project { get; }
    public Project? ExistingProject { get; }
    public ProjectContextReference? Context { get; }
    public string? ErrorMessage { get; }

    public static ProjectOnboardingResult Success(Project project, ProjectContextReference context) =>
        new(ProjectOnboardingCompletionStatus.Succeeded, project, context, null);

    public static ProjectOnboardingResult FailedBeforeProjectCreation(string errorMessage) =>
        new(
            ProjectOnboardingCompletionStatus.FailedBeforeProjectCreation,
            null,
            null,
            errorMessage);

    public static ProjectOnboardingResult PartialProjectCreated(Project project, string errorMessage) =>
        new(
            ProjectOnboardingCompletionStatus.PartialProjectCreated,
            project ?? throw new ArgumentNullException(nameof(project)),
            null,
            errorMessage);

    public static ProjectOnboardingResult AlreadyRegistered(Project project) =>
        new(
            ProjectOnboardingCompletionStatus.AlreadyRegistered,
            project,
            null,
            "Project already registered.",
            project);

    // Kept as a small compatibility helper for existing callers while making the result's
    // semantic status explicit. A result carrying a project is necessarily partial.
    public static ProjectOnboardingResult Failure(Project? project, string errorMessage) =>
        project is null
            ? FailedBeforeProjectCreation(errorMessage)
            : PartialProjectCreated(project, errorMessage);
}

/// <summary>Coordinates the bounded APO-39 onboarding workflow without routing or execution.</summary>
public interface IProjectOnboardingService
{
    Task<LocalRepositoryInspection> InspectRepositoryAsync(
        string localPath,
        CancellationToken cancellationToken = default);

    Task<ProjectOnboardingResult> CompleteAsync(
        ProjectOnboardingRequest request,
        CancellationToken cancellationToken = default);
}
