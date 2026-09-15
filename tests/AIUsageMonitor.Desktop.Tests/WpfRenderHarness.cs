using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace AIUsageMonitor.Desktop.Tests;

/// <summary>
/// Owns the single STA dispatcher and WPF <see cref="System.Windows.Application"/> the desktop
/// tests may create. WPF allows only one Application per process, so this is shared by every test
/// in the "WPF visual acceptance" collection rather than constructed per test.
/// </summary>
public sealed class WpfRenderHarness : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private Dispatcher? _dispatcher;
    private Exception? _startupException;

    public WpfRenderHarness()
    {
        _thread = new Thread(Start)
        {
            IsBackground = true,
            Name = "APO-70 WPF visual render thread"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _started.Wait();
        if (_startupException is not null)
        {
            throw new InvalidOperationException("The WPF render dispatcher could not start.", _startupException);
        }
    }

    public void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _dispatcher!.Invoke(action);
    }

    public void Dispose()
    {
        if (_dispatcher is not null && !_dispatcher.HasShutdownStarted)
        {
            _dispatcher.InvokeShutdown();
        }

        _thread.Join(TimeSpan.FromSeconds(10));
        _started.Dispose();
    }

    private void Start()
    {
        try
        {
            var application = new System.Windows.Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/AIUsageMonitor.Desktop;component/Resources/Theme.xaml",
                    UriKind.Relative)
            });
            _dispatcher = Dispatcher.CurrentDispatcher;
            _started.Set();
            Dispatcher.Run();
            application.Shutdown();
        }
        catch (Exception exception)
        {
            _startupException = exception;
            _started.Set();
        }
    }
}
