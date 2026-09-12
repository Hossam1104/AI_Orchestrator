namespace AIUsageMonitor.Providers.Common;

public sealed class SystemExecutableLocator : IExecutableLocator
{
    /// <summary>
    /// Windows probe order. Directly launchable images come first because the bounded process host
    /// starts processes with UseShellExecute disabled, where Windows can only run a real image.
    /// Script wrappers and extensionless POSIX shims are still reported last so that presence
    /// detection stays truthful, but they are never preferred over a launchable sibling.
    /// </summary>
    private static readonly string[] WindowsExtensions = [".exe", ".com", ".cmd", ".bat", ""];

    private readonly Func<string?> _pathProvider;

    public SystemExecutableLocator()
        : this(null)
    {
    }

    /// <summary>
    /// The PATH seam exists so resolution order can be proven against a controlled search path
    /// instead of mutating the process environment of a running test host.
    /// </summary>
    public SystemExecutableLocator(Func<string?>? pathProvider)
    {
        _pathProvider = pathProvider ?? (static () => Environment.GetEnvironmentVariable("PATH"));
    }

    public string? Find(string commandName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);

        var path = _pathProvider();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var probeWindowsExtensions =
            OperatingSystem.IsWindows() && Path.GetExtension(commandName).Length == 0;
        var candidates = probeWindowsExtensions
            ? WindowsExtensions.Select(extension => commandName + extension).ToArray()
            : [commandName];

        // Directory-major, extension-minor: the same order Windows itself resolves a command in, so
        // APO reports the executable the operator's own shell would run.
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var candidate in candidates)
            {
                if (ResolveExisting(directory, candidate) is { } resolved)
                {
                    return resolved;
                }
            }
        }

        return null;
    }

    private static string? ResolveExisting(string directory, string candidate)
    {
        // PATH is operator-controlled and routinely contains quoted or malformed entries; a bad
        // entry must skip that directory rather than fail the whole lookup.
        var normalized = directory.Trim().Trim('"');
        if (normalized.Length == 0)
        {
            return null;
        }

        try
        {
            var fullPath = Path.Combine(normalized, candidate);
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
