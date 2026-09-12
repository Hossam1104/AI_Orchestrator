using AIUsageMonitor.Application.Orchestration;

namespace AIUsageMonitor.Connection.Tests;

public sealed class ExecutionPreparationCoordinatorTests
{
    [Fact]
    public void OwnerRequestCreatesItsOwnAuthorityIdAndNormalizesOptionalLines()
    {
        var projectId = Guid.NewGuid();
        var request = new OrchestrationWorkRequest(
            projectId,
            "owner",
            "Bounded change",
            "Make the requested bounded change.",
            acceptanceCriteria: ["First criterion", "", "Second criterion"],
            constraints: [" Keep the scope local. "]);

        Assert.NotEqual(Guid.Empty, request.RequestId);
        Assert.Equal(projectId, request.ProjectId);
        Assert.Equal(["First criterion", "Second criterion"], request.AcceptanceCriteria);
        Assert.Equal(["Keep the scope local."], request.Constraints);
    }

    [Fact]
    public void OwnerRequestRequiresAtLeastOneAcceptanceCriterion()
    {
        Assert.Throws<ArgumentException>(() => new OrchestrationWorkRequest(
            Guid.NewGuid(),
            "owner",
            "Bounded change",
            "Make the requested bounded change.",
            acceptanceCriteria: []));
    }
}
