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

    [Fact]
    public void SelectedNavigationUsesDistinctSurfaceRailBorderAndWeight()
    {
        var xaml = File.ReadAllText(SourcePath("src", "AIUsageMonitor.Desktop", "MainWindow.xaml"));

        Assert.Contains("<ColumnDefinition Width=\"5\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NavIndicator\" Width=\"5\"", xaml, StringComparison.Ordinal);
        Assert.Contains("NavigationSelectedBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("AccentVioletBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("TextElement.FontWeight\" Value=\"SemiBold\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Trigger Property=\"Tag\" Value=\"True\">", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ScrollBarsUseCompactPillThumbsWithHoverDragAndBothOrientations()
    {
        var xaml = File.ReadAllText(SourcePath("src", "AIUsageMonitor.Desktop", "Resources", "Controls.xaml"));
        var brushes = File.ReadAllText(SourcePath("src", "AIUsageMonitor.Desktop", "Resources", "Brushes.xaml"));
        var darkTheme = File.ReadAllText(SourcePath("src", "AIUsageMonitor.Desktop", "Resources", "Theme.Dark.xaml"));

        Assert.Contains("ApoScrollBarThumbTemplate", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"999\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollBarThumbHoverBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollBarThumbDragBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Width\" Value=\"8\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"8\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("ApoHorizontalScrollBarThumbStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("ApoVerticalScrollBarTemplate", xaml, StringComparison.Ordinal);
        Assert.Contains("ApoHorizontalScrollBarTemplate", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollBarThumbDragBrush", brushes, StringComparison.Ordinal);
        Assert.Contains("ScrollBarThumbDragBrush", darkTheme, StringComparison.Ordinal);
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
