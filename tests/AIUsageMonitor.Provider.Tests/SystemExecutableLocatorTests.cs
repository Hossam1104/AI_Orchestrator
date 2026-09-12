using AIUsageMonitor.Providers.Common;

namespace AIUsageMonitor.Provider.Tests;

/// <summary>
/// Resolution-order facts. The bounded process host starts processes with UseShellExecute
/// disabled, so an extensionless POSIX shim resolved ahead of a real image is not a cosmetic
/// preference: it is the difference between a provider probe running and failing to start.
/// </summary>
public sealed class SystemExecutableLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AIUsageMonitor",
        "locator-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ARealImageWinsOverAnExtensionlessShimInTheSameDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = CreateDirectory("bin");
        WriteCommand(directory, "codex");
        var expected = WriteCommand(directory, "codex.exe");

        Assert.Equal(expected, CreateLocator(directory).Find("codex"));
    }

    [Fact]
    public void AScriptWrapperWinsOverAnExtensionlessShimInTheSameDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = CreateDirectory("bin");
        WriteCommand(directory, "claude");
        var expected = WriteCommand(directory, "claude.cmd");

        Assert.Equal(expected, CreateLocator(directory).Find("claude"));
    }

    [Fact]
    public void AnExtensionlessShimIsStillReportedWhenNothingElseExists()
    {
        var directory = CreateDirectory("bin");
        var expected = WriteCommand(directory, "codex");

        Assert.Equal(expected, CreateLocator(directory).Find("codex"));
    }

    [Fact]
    public void EarlierPathEntriesStillWin()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var first = CreateDirectory("first");
        var second = CreateDirectory("second");
        var expected = WriteCommand(first, "codex.cmd");
        WriteCommand(second, "codex.exe");

        Assert.Equal(expected, CreateLocator(first, second).Find("codex"));
    }

    [Fact]
    public void QuotedAndMalformedPathEntriesAreSkippedRatherThanFailingTheLookup()
    {
        var directory = CreateDirectory("bin");
        var expected = WriteCommand(directory, "codex" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var searchPath = string.Join(
            Path.PathSeparator,
            "   ",
            "\"" + directory + "\"");

        Assert.Equal(expected, new SystemExecutableLocator(() => searchPath).Find("codex"));
    }

    [Fact]
    public void AnAbsentCommandRemainsUnresolved()
    {
        Assert.Null(CreateLocator(CreateDirectory("bin")).Find("codex"));
    }

    [Fact]
    public void AnEmptySearchPathRemainsUnresolved()
    {
        Assert.Null(new SystemExecutableLocator(() => null).Find("codex"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temporary directory must never fail a test run.
        }
    }

    private static SystemExecutableLocator CreateLocator(params string[] directories) =>
        new(() => string.Join(Path.PathSeparator, directories));

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteCommand(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
