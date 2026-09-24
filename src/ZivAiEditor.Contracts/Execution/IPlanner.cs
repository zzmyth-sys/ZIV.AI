namespace ZivAiEditor.Contracts.Execution;

public interface IPlanner
{
    Task<EditPlan> PlanAsync(PlanRequest request, CancellationToken ct = default);
}
