using AIUsageMonitor.Application.Projects;

namespace AIUsageMonitor.Infrastructure.Git;

internal static class WorkspaceDiscovery
{
    private static readonly string[] GovernancePaths =
    [
        "AGENTS.md",
        "CLAUDE.md",
        "TASK.md",
        ".ai",
        ".ai/CURRENT_STATE.md",
        "README.md"
    ];

    public static ProjectWorkspaceDiscovery Inspect(
        string selectedRoot,
        IReadOnlyList<RepositoryRemote>? remotes = null)
    {
        var displayName = TryGetDirectoryName(selectedRoot);
        var states = new Dictionary<string, WorkspaceFileState>(StringComparer.OrdinalIgnoreCase);
        var projectFiles = new List<string>();
        var exists = false;
        var readable = false;

        try
        {
            var directory = new DirectoryInfo(selectedRoot);
            exists = directory.Exists;
            if (exists)
            {
                // Enumerating one entry proves the directory can be opened without reading file
                // contents. It is intentionally bounded to the selected root.
                using var entries = directory.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly).GetEnumerator();
                _ = entries.MoveNext();
                readable = true;
            }

            foreach (var relativePath in GovernancePaths)
            {
                var fullPath = Path.Combine(selectedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                states[relativePath] = !exists
                    ? WorkspaceFileState.Unknown
                    : ExistsAsFileOrDirectory(fullPath);
            }

            if (readable)
            {
                foreach (var entry in directory.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
                {
                    if (entry is FileInfo file && IsProjectFile(file.Name))
                    {
                        projectFiles.Add(file.Name);
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            readable = false;
            foreach (var relativePath in GovernancePaths)
            {
                states[relativePath] = WorkspaceFileState.Unknown;
            }
        }
        catch (IOException)
        {
            readable = false;
            foreach (var relativePath in GovernancePaths)
            {
                states[relativePath] = WorkspaceFileState.Unknown;
            }
        }
        catch (ArgumentException)
        {
            readable = false;
            foreach (var relativePath in GovernancePaths)
            {
                states[relativePath] = WorkspaceFileState.Unknown;
            }
        }

        return new ProjectWorkspaceDiscovery(
            selectedRoot,
            displayName,
            exists,
            readable,
            states,
            projectFiles,
            trackerReferences: Array.Empty<string>(),
            remoteProvider: InferRemoteProvider(remotes));
    }

    private static WorkspaceFileState ExistsAsFileOrDirectory(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path)
                ? WorkspaceFileState.Present
                : WorkspaceFileState.Missing;
        }
        catch (UnauthorizedAccessException)
        {
            return WorkspaceFileState.Unknown;
        }
        catch (IOException)
        {
            return WorkspaceFileState.Unknown;
        }
    }

    private static bool IsProjectFile(string name) =>
        name.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Directory.Build.targets", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("global.json", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("package.json", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    private static string TryGetDirectoryName(string path)
    {
        try
        {
            return new DirectoryInfo(path).Name;
        }
        catch (ArgumentException)
        {
            return "Selected workspace";
        }
    }

    private static string? InferRemoteProvider(IReadOnlyList<RepositoryRemote>? remotes)
    {
        var urls = remotes?.Select(remote => remote.SanitizedUrl).ToArray() ?? Array.Empty<string>();
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

        return urls.Length == 0 ? null : "Other / Unknown";
    }
}
