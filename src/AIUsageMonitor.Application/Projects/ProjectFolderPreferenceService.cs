using AIUsageMonitor.Application.Settings;

namespace AIUsageMonitor.Application.Projects;

/// <summary>
/// Preferred local project folder resolution, persisted through the APO-owned settings boundary
/// (never an ad-hoc repository write). Resolution order is: the folder of the last project that
/// was registered successfully, then the configured default workspace root, then nothing — in
/// which case the caller falls back to the normal Windows folder-picker default.
/// </summary>
public sealed class ProjectFolderPreferenceService : IProjectFolderPreferenceService
{
    /// <summary>
    /// Settings key holding the local path of the last successfully registered project. The value
    /// is a plain local path preference and never carries credentials or tracker payloads.
    /// </summary>
    public const string LastProjectFolderKey = "projects.lastSuccessfulProjectFolder";

    /// <summary>
    /// Default workspace container used before any project has been registered. It is a
    /// convenience default only: it is offered exclusively when it exists on this machine, so
    /// installations without this drive or folder degrade to the normal Windows picker default.
    /// </summary>
    public const string DefaultWorkspaceRoot = @"D:\AI Tools\Active Projects";

    private readonly ISettingsService _settings;
    private readonly Func<string, bool> _directoryExists;
    private readonly string? _defaultWorkspaceRoot;

    public ProjectFolderPreferenceService(ISettingsService settings)
        : this(settings, directoryExists: null, DefaultWorkspaceRoot)
    {
    }

    public ProjectFolderPreferenceService(
        ISettingsService settings,
        Func<string, bool>? directoryExists,
        string? defaultWorkspaceRoot)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _directoryExists = directoryExists ?? DirectoryExistsCore;
        _defaultWorkspaceRoot = ProjectPathComparer.Normalize(defaultWorkspaceRoot);
    }

    public async Task<string?> GetPreferredPickerRootAsync(CancellationToken cancellationToken = default)
    {
        var lastFolder = await ReadLastProjectFolderAsync(cancellationToken).ConfigureAwait(false);
        if (lastFolder is not null && ResolveBrowseRoot(lastFolder) is { } fromHistory)
        {
            return fromHistory;
        }

        return _defaultWorkspaceRoot is not null && Exists(_defaultWorkspaceRoot)
            ? _defaultWorkspaceRoot
            : null;
    }

    public async Task RecordSuccessfulProjectFolderAsync(
        string projectLocalPath,
        CancellationToken cancellationToken = default)
    {
        if (ProjectPathComparer.Normalize(projectLocalPath) is not { } normalized)
        {
            return;
        }

        try
        {
            await _settings.SetAsync(LastProjectFolderKey, normalized, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A picker convenience preference must never turn a completed registration into a
            // failure, and it must never surface a persistence error to the operator.
        }
    }

    /// <summary>
    /// A registered project folder is a project, not a workspace container, so browsing starts at
    /// its parent where sibling projects live. The project folder itself is used only when it has
    /// no usable parent (for example a drive root).
    /// </summary>
    private string? ResolveBrowseRoot(string projectFolder)
    {
        string? parent;
        try
        {
            parent = Path.GetDirectoryName(projectFolder);
        }
        catch (ArgumentException)
        {
            parent = null;
        }

        if (!string.IsNullOrWhiteSpace(parent) && Exists(parent))
        {
            return parent;
        }

        return Exists(projectFolder) ? projectFolder : null;
    }

    private async Task<string?> ReadLastProjectFolderAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stored = await _settings
                .GetAsync<string>(LastProjectFolderKey, cancellationToken)
                .ConfigureAwait(false);
            return ProjectPathComparer.Normalize(stored);
        }
        catch (Exception)
        {
            // Unavailable or unreadable settings must degrade to the default root, never block
            // the operator from choosing a folder.
            return null;
        }
    }

    private bool Exists(string path)
    {
        try
        {
            return _directoryExists(path);
        }
        catch (Exception)
        {
            // An unreachable or permission-denied path is treated as absent rather than offered
            // as a preferred root.
            return false;
        }
    }

    private static bool DirectoryExistsCore(string path) => Directory.Exists(path);
}
