using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

// Step 1 skeleton: no business logic. LlmPlanner / FallbackPlanner arrive in later steps.
internal sealed class PlaceholderPlanner : IPlanner
{
    public Task<EditPlan> PlanAsync(PlanRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();
}
