using System.IO;
using System.Xml.Linq;

namespace AIUsageMonitor.Desktop.Tests;

public sealed class ProductRecoveryStructureTests
{
    [Fact]
    public void ExecutionUsesOwnerModeWithAdvancedContractDetails()
    {
        var xaml = File.ReadAllText(SourcePath("src", "AIUsageMonitor.Desktop", "Views", "ExecutionView.xaml"));

        var document = XDocument.Parse(xaml);
        var wpf = (XNamespace)"http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        Assert.Single(document.Descendants(wpf + "ScrollViewer"));
        Assert.Contains("OWNER MODE", xaml, StringComparison.Ordinal);
        Assert.Contains("WORK / REQUEST", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Advanced details\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TechnicalDetailsExpanderStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("Bounded request acceptance criteria", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellAndProviderSurfaceUseRecoveredSharedTopology()
    {
        var xaml = File.ReadAllText(SourcePath("src", "AIUsageMonitor.Desktop", "MainWindow.xaml"));

        Assert.Contains("<ColumnDefinition Width=\"264\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"116\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("AI PROJECT ORCHESTRATOR", xaml, StringComparison.Ordinal);
        Assert.Contains("<UniformGrid Columns=\"2\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("OwnerStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("DELIVERY LIFECYCLE", xaml, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"False\"", xaml, StringComparison.Ordinal);
    }

    private static string SourcePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TASK.md")))
        {
            directory = directory.Parent;
        }

        return Path.Combine([directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found."), .. segments]);
    }
}
