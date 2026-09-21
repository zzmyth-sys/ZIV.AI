namespace ZivAiEditor.Contracts.Planning;

public interface IPlanner
{
    Task<EditPlan> PlanAsync(PlanRequest request, CancellationToken ct = default);
}
