using System.IO;
using System.Xml.Linq;
using AIUsageMonitor.Desktop.ViewModels;

namespace AIUsageMonitor.Desktop.Tests;

public sealed class ShellRecoveryTests
{
    [Fact]
    public void GlobalStatus_DescribesLocalPersistenceTruthfully()
    {
        var viewModel = new MainWindowViewModel(new AiCapacityViewModel());

        Assert.Equal("SETUP REQUIRED", viewModel.GlobalStatusText);

        viewModel.SetPersistenceAvailability(true);

        Assert.Equal("LOCAL READY", viewModel.GlobalStatusText);
    }

    [Fact]
    public void PrimaryNavigation_ChangesTheActiveWorkspace()
    {
        var viewModel = new MainWindowViewModel(new AiCapacityViewModel());

        viewModel.ShowProjectsCommand.Execute(null);
        Assert.Same(viewModel.Projects, viewModel.ActiveWorkspace);
        Assert.True(viewModel.IsProjectsSelected);

        viewModel.ShowAiCapacityCommand.Execute(null);
        Assert.Same(viewModel.AiCapacity, viewModel.ActiveWorkspace);
        Assert.True(viewModel.IsAiCapacitySelected);

        viewModel.ShowMissionControlCommand.Execute(null);
        Assert.Same(viewModel.MissionControl, viewModel.ActiveWorkspace);
        Assert.True(viewModel.IsMissionControlSelected);
    }

    [Fact]
    public void ThemeResources_ExposeLightDarkTokensAndShellHasNoRejectedClaims()
    {
        var root = FindRepositoryRoot();
        var light = XDocument.Load(Path.Combine(root, "src", "AIUsageMonitor.Desktop", "Resources", "Colors.xaml"));
        var theme = XDocument.Load(Path.Combine(root, "src", "AIUsageMonitor.Desktop", "Resources", "Theme.xaml"));
        var dark = XDocument.Load(Path.Combine(root, "src", "AIUsageMonitor.Desktop", "Resources", "Theme.Dark.xaml"));
        var shell = File.ReadAllText(Path.Combine(root, "src", "AIUsageMonitor.Desktop", "MainWindow.xaml"));
        var brushes = File.ReadAllText(Path.Combine(root, "src", "AIUsageMonitor.Desktop", "Resources", "Brushes.xaml"));
        var controls = File.ReadAllText(Path.Combine(root, "src", "AIUsageMonitor.Desktop", "Resources", "Controls.xaml"));

        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var lightKeys = light.Descendants().Select(element => (string?)element.Attribute(xNamespace + "Key")).ToArray();
        var darkKeys = dark.Descendants().Select(element => (string?)element.Attribute(xNamespace + "Key")).ToArray();

        Assert.Contains("CanvasColor", lightKeys);
        Assert.Contains("TextPrimaryColor", lightKeys);
        Assert.Contains("ThemeVariant", lightKeys);
        Assert.Contains("CanvasColor", darkKeys);
        Assert.Contains("ThemeVariant", darkKeys);
        Assert.Contains("Colors.xaml", theme.ToString());
        Assert.Contains("NavigationSelectedGradientBrush", brushes);
        Assert.Contains("BrandFocusBrush", brushes);
        Assert.Contains("StatusPillStyle", controls);
        Assert.Contains("AI Orchestrator", shell);
        Assert.Contains("AI PROJECT ORCHESTRATOR", shell);
        Assert.DoesNotContain("CAPACITY READY", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Text=\"SOON\"", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RMS+", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DBS", shell, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AIUsageMonitor.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root could not be located from the test output directory.");
    }
}
