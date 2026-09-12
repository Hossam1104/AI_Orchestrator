using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using AIUsageMonitor.Desktop.ViewModels;

namespace AIUsageMonitor.Desktop.Tests;

/// <summary>
/// Shell lifetime facts. These need the shared WPF harness because a real window must be shown and
/// closed for its Loaded/Closed handlers to run.
/// </summary>
[Collection("WPF visual acceptance")]
public sealed class MainWindowLifetimeTests
{
    private readonly WpfRenderHarness _harness;

    public MainWindowLifetimeTests(WpfRenderHarness harness)
    {
        _harness = harness;
    }

    [Fact]
    public void ClosingTheShell_ReleasesItsStaticThemeSubscription()
    {
        _harness.Run(() =>
        {
            var beforeOpen = ThemeSubscriberCount();
            var window = new MainWindow(new MainWindowViewModel(new AiCapacityViewModel()))
            {
                Width = 800,
                Height = 600,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000
            };
            window.SetPersistenceAvailability(false);

            window.Show();
            Pump();
            var whileOpen = ThemeSubscriberCount();

            window.Close();
            Pump();
            var afterClose = ThemeSubscriberCount();

            Assert.Equal(beforeOpen + 1, whileOpen);
            Assert.Equal(beforeOpen, afterClose);
        });
    }

    private static void Pump() =>
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

    /// <summary>
    /// ThemeChanged is a static field-like event, so its invocation list is the only direct
    /// evidence that a closed window no longer holds the shell graph alive.
    /// </summary>
    private static int ThemeSubscriberCount()
    {
        var field = typeof(ThemeManager).GetField(
                nameof(ThemeManager.ThemeChanged),
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The ThemeChanged backing field could not be located.");

        return ((EventHandler?)field.GetValue(null))?.GetInvocationList().Length ?? 0;
    }
}
