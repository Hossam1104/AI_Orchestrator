using AIUsageMonitor.Application.MissionControl;

namespace AIUsageMonitor.Application.Orchestration;

/// <summary>Safe owner-facing projection of already authoritative execution context.</summary>
public sealed class ExecutionContextPrefill
{
    public ExecutionContextPrefill(
        string source,
        string? title = null,
        string? objective = null,
        string? workItemReference = null,
        IReadOnlyList<string>? acceptanceCriteria = null,
        IReadOnlyList<string>? constraints = null,
        IReadOnlyList<string>? validationExpectations = null,
        bool isAuthoritative = true)
    {
        Source = string.IsNullOrWhiteSpace(source) ? throw new ArgumentException("A context source is required.", nameof(source)) : source.Trim();
        Title = title?.Trim();
        Objective = objective?.Trim();
        WorkItemReference = workItemReference?.Trim();
        AcceptanceCriteria = acceptanceCriteria ?? Array.Empty<string>();
        Constraints = constraints ?? Array.Empty<string>();
        ValidationExpectations = validationExpectations ?? Array.Empty<string>();
        IsAuthoritative = isAuthoritative;
    }

    public string Source { get; }

    public string? Title { get; }

    public string? Objective { get; }

    public string? WorkItemReference { get; }

    public IReadOnlyList<string> AcceptanceCriteria { get; }

    public IReadOnlyList<string> Constraints { get; }

    public IReadOnlyList<string> ValidationExpectations { get; }

    public bool IsAuthoritative { get; }
}

public static class ExecutionContextPrefillPolicy
{
    public static ExecutionContextPrefill FromPreparedExecution(PreparedExecution prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);

        var contract = prepared.Contract;
        return new(
            "Persisted Ready execution contract",
            contract.WorkItem.Title,
            contract.IncludedScope.FirstOrDefault(static clause => string.Equals(clause.Id, "objective", StringComparison.Ordinal))?.Statement,
            contract.WorkItem.Reference,
            contract.AcceptanceCriteria.Select(static criterion => criterion.Statement).ToArray(),
            contract.Constraints.Select(static clause => clause.Statement).ToArray(),
            contract.ValidationRequirements.Select(static requirement => requirement.Description).ToArray());
    }

    public static ExecutionContextPrefill FromMissionControl(MissionControlSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var work = snapshot.CurrentWork;
        var title = Known(work.Title, "No current work selected") ? work.Title : null;
        var reference = Known(work.Reference, "No authoritative current work evidence") ? work.Reference : null;
        return new(
            "Mission Control current-work read model",
            title,
            null,
            reference,
            isAuthoritative: work.IsAuthoritative);
    }

    public static string DeriveTitle(string ownerRequest)
    {
        var firstLine = ownerRequest
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
        var compact = string.Join(' ', firstLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 120 ? compact : compact[..120].TrimEnd() + "…";
    }

    private static bool Known(string value, string placeholder) =>
        !string.IsNullOrWhiteSpace(value) && !string.Equals(value, placeholder, StringComparison.Ordinal);
}
