namespace AIUsageMonitor.Providers.Common;

public sealed class SystemExecutableLocator : IExecutableLocator
{
    private static readonly string[] WindowsExecutableExtensions = [".exe", ".com"];
    private static readonly string[] WindowsWrapperExtensions = [".cmd", ".bat", ""];

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
        var directories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (probeWindowsExtensions)
        {
            // Prefer a directly launchable image anywhere on PATH. The bounded process host does
            // not invoke cmd.exe, so an earlier npm shim must not hide a later real CLI image.
            foreach (var extension in WindowsExecutableExtensions)
            {
                foreach (var directory in directories)
                {
                    if (ResolveExisting(directory, commandName + extension) is { } resolved)
                    {
                        return resolved;
                    }
                }
            }

            foreach (var extension in WindowsWrapperExtensions)
            {
                foreach (var directory in directories)
                {
                    if (ResolveExisting(directory, commandName + extension) is { } resolved)
                    {
                        return resolved;
                    }
                }
            }
        }
        else
        {
            foreach (var directory in directories)
            {
                if (ResolveExisting(directory, commandName) is { } resolved)
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
