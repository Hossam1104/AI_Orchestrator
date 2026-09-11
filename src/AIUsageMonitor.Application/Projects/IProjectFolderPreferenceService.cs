namespace AIUsageMonitor.Application.Projects;

/// <summary>
/// Resolves where the local project folder picker should start, and records the folder of a
/// project that was actually registered successfully. This is a convenience preference only: it
/// never registers a project, never selects one, and never invents a path that does not exist.
/// </summary>
public interface IProjectFolderPreferenceService
{
    /// <summary>
    /// Returns the folder the picker should open at, or <see langword="null"/> when no preferred
    /// root can be proven to exist on this machine (the caller then uses the normal Windows
    /// folder-picker default).
    /// </summary>
    Task<string?> GetPreferredPickerRootAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the local path of a project that completed registration successfully. Callers must
    /// only invoke this after a proven successful registration, never on selection or preview.
    /// </summary>
    Task RecordSuccessfulProjectFolderAsync(
        string projectLocalPath,
        CancellationToken cancellationToken = default);
}
