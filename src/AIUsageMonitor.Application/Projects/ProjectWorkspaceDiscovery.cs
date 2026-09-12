namespace AIUsageMonitor.Application.Projects;

public enum WorkspaceFileState
{
    Present,
    Missing,
    Unknown
}

/// <summary>
/// Bounded, read-only facts discovered from a selected workspace. It contains names and states,
/// never file contents, credentials, prompts, or repository payloads.
/// </summary>
public sealed class ProjectWorkspaceDiscovery
{
    public ProjectWorkspaceDiscovery(
        string selectedRoot,
        string displayName,
        bool exists,
        bool isReadable,
        IReadOnlyDictionary<string, WorkspaceFileState>? governanceFiles = null,
        IReadOnlyList<string>? projectFiles = null,
        IReadOnlyList<string>? trackerReferences = null,
        string? remoteProvider = null)
    {
        if (string.IsNullOrWhiteSpace(selectedRoot))
        {
            throw new ArgumentException("Selected workspace root is required.", nameof(selectedRoot));
        }

        SelectedRoot = selectedRoot;
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? new DirectoryInfo(selectedRoot).Name
            : displayName.Trim();
        Exists = exists;
        IsReadable = isReadable;
        GovernanceFiles = new Dictionary<string, WorkspaceFileState>(
            governanceFiles ?? new Dictionary<string, WorkspaceFileState>(),
            StringComparer.OrdinalIgnoreCase);
        ProjectFiles = (projectFiles ?? Array.Empty<string>()).ToArray();
        TrackerReferences = (trackerReferences ?? Array.Empty<string>()).ToArray();
        RemoteProvider = string.IsNullOrWhiteSpace(remoteProvider) ? null : remoteProvider.Trim();
    }

    public string SelectedRoot { get; }
    public string DisplayName { get; }
    public bool Exists { get; }
    public bool IsReadable { get; }
    public IReadOnlyDictionary<string, WorkspaceFileState> GovernanceFiles { get; }
    public IReadOnlyList<string> ProjectFiles { get; }
    public IReadOnlyList<string> TrackerReferences { get; }
    public string? RemoteProvider { get; }

    public IReadOnlyList<string> PresentGovernanceFiles => GovernanceFiles
        .Where(static item => item.Value == WorkspaceFileState.Present)
        .Select(static item => item.Key)
        .ToArray();
}
