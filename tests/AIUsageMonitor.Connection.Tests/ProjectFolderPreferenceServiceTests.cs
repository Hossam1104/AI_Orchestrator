using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Settings;

namespace AIUsageMonitor.Connection.Tests;

/// <summary>
/// The folder picker must prefer the last successfully used project folder, then the configured
/// default workspace root, then the normal Windows default — and must never offer a root it cannot
/// prove exists on this machine.
/// </summary>
public sealed class ProjectFolderPreferenceServiceTests
{
    private const string DefaultRoot = "D:\\AI Tools\\Active Projects";

    [Fact]
    public async Task DefaultWorkspaceRootIsOfferedWhenNoProjectHasBeenRegistered()
    {
        var service = CreateService(new MemorySettingsService(), DefaultRoot);

        Assert.Equal(DefaultRoot, await service.GetPreferredPickerRootAsync());
    }

    [Fact]
    public async Task AbsentDefaultWorkspaceRootFallsBackToTheWindowsPickerDefault()
    {
        var service = CreateService(new MemorySettingsService(), existingDirectories: []);

        Assert.Null(await service.GetPreferredPickerRootAsync());
    }

    [Fact]
    public async Task LastSuccessfulProjectFolderTakesPrecedenceOverTheDefaultRoot()
    {
        var settings = new MemorySettingsService();
        await settings.SetAsync(ProjectFolderPreferenceService.LastProjectFolderKey, "C:\\Code\\Payments");
        var service = CreateService(settings, DefaultRoot, "C:\\Code", "C:\\Code\\Payments");

        // The registered folder is a project, so browsing opens at the container holding it.
        Assert.Equal("C:\\Code", await service.GetPreferredPickerRootAsync());
    }

    [Fact]
    public async Task ContainerIsStillOfferedWhenTheRegisteredProjectFolderIsGone()
    {
        var settings = new MemorySettingsService();
        await settings.SetAsync(ProjectFolderPreferenceService.LastProjectFolderKey, "C:\\Code\\Removed");
        var service = CreateService(settings, DefaultRoot, "C:\\Code");

        Assert.Equal("C:\\Code", await service.GetPreferredPickerRootAsync());
    }

    [Fact]
    public async Task VanishedHistoryFallsBackToTheDefaultRoot()
    {
        var settings = new MemorySettingsService();
        await settings.SetAsync(ProjectFolderPreferenceService.LastProjectFolderKey, "E:\\Gone\\Project");
        var service = CreateService(settings, DefaultRoot);

        Assert.Equal(DefaultRoot, await service.GetPreferredPickerRootAsync());
    }

    [Fact]
    public async Task DriveRootHistoryIsOfferedAsItsOwnBrowseRoot()
    {
        var settings = new MemorySettingsService();
        await settings.SetAsync(ProjectFolderPreferenceService.LastProjectFolderKey, "X:\\");
        var service = CreateService(settings, DefaultRoot, "X:\\");

        Assert.Equal("X:\\", await service.GetPreferredPickerRootAsync());
    }

    [Fact]
    public async Task SuccessfulProjectFolderIsPersistedCanonically()
    {
        var settings = new MemorySettingsService();
        var service = CreateService(settings, DefaultRoot);

        await service.RecordSuccessfulProjectFolderAsync("C:\\Code\\Payments\\");

        Assert.Equal(
            "C:\\Code\\Payments",
            await settings.GetAsync<string>(ProjectFolderPreferenceService.LastProjectFolderKey));
    }

    [Fact]
    public async Task BlankProjectFolderIsNeverPersisted()
    {
        var settings = new MemorySettingsService();
        var service = CreateService(settings, DefaultRoot);

        await service.RecordSuccessfulProjectFolderAsync("   ");

        Assert.Equal(0, settings.WriteCount);
    }

    [Fact]
    public async Task UnreadableSettingsDegradeToTheDefaultRootInsteadOfFailing()
    {
        var settings = new MemorySettingsService
        {
            ReadFailure = new IOException("simulated settings read failure")
        };
        var service = CreateService(settings, DefaultRoot);

        Assert.Equal(DefaultRoot, await service.GetPreferredPickerRootAsync());
    }

    [Fact]
    public async Task UnwritableSettingsDoNotSurfaceAsAPickerFailure()
    {
        var settings = new MemorySettingsService
        {
            WriteFailure = new UnauthorizedAccessException("simulated settings write failure")
        };
        var service = CreateService(settings, DefaultRoot);

        await service.RecordSuccessfulProjectFolderAsync("C:\\Code\\Payments");
    }

    [Fact]
    public async Task UnreachableDirectoryProbeIsTreatedAsAbsentRatherThanPreferred()
    {
        var service = new ProjectFolderPreferenceService(
            new MemorySettingsService(),
            _ => throw new IOException("simulated probe failure"),
            DefaultRoot);

        Assert.Null(await service.GetPreferredPickerRootAsync());
    }

    private static ProjectFolderPreferenceService CreateService(
        ISettingsService settings,
        params string[] existingDirectories)
    {
        var existing = new HashSet<string>(existingDirectories, StringComparer.OrdinalIgnoreCase);
        return new ProjectFolderPreferenceService(
            settings,
            path => existing.Contains(Path.TrimEndingDirectorySeparator(path)) || existing.Contains(path),
            DefaultRoot);
    }

    private sealed class MemorySettingsService : ISettingsService
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

        public Exception? ReadFailure { get; init; }

        public Exception? WriteFailure { get; init; }

        public int WriteCount { get; private set; }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (ReadFailure is not null)
            {
                throw ReadFailure;
            }

            return Task.FromResult(_values.TryGetValue(key, out var value) && value is T typed ? typed : default);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            if (WriteFailure is not null)
            {
                throw WriteFailure;
            }

            WriteCount++;
            _values[key] = value;
            return Task.CompletedTask;
        }
    }
}
