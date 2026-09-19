using System.Xml.Linq;

namespace AIUsageMonitor.Domain.Tests;

public sealed class ArchitectureDependencyTests
{
    [Fact]
    public void ProductionProjectReferencesFollowTheAcceptedDirection()
    {
        var root = FindRepositoryRoot();
        var allowed = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["AIUsageMonitor.Domain"] = [],
            ["AIUsageMonitor.Application"] = ["AIUsageMonitor.Domain"],
            ["AIUsageMonitor.Infrastructure"] = ["AIUsageMonitor.Application", "AIUsageMonitor.Domain"],
            ["AIUsageMonitor.Providers"] = ["AIUsageMonitor.Application", "AIUsageMonitor.Domain"],
            ["AIUsageMonitor.Desktop"] = ["AIUsageMonitor.Application", "AIUsageMonitor.Infrastructure", "AIUsageMonitor.Providers"]
        };

        foreach (var (projectName, allowedReferences) in allowed)
        {
            var projectPath = Path.Combine(root, "src", projectName, projectName + ".csproj");
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var references = XDocument.Load(projectPath)
                .Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension(Path.GetFullPath(
                    Path.Combine(projectDirectory, (string)reference.Attribute("Include")!))))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var forbidden = references.Except(allowedReferences, StringComparer.OrdinalIgnoreCase).ToArray();

            Assert.True(
                forbidden.Length == 0,
                $"{projectName} has forbidden production ProjectReference(s): {string.Join(", ", forbidden)}");
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "src",
                    "AIUsageMonitor.Domain",
                    "AIUsageMonitor.Domain.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The AI_Orchestrator repository root could not be located.");
    }
}
