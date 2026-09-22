using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

/// <summary>
/// Z22 degradation chain: try the primary (LLM) planner, and on any failure
/// return the deterministic <see cref="FallbackPlanner"/> plan instead. The
/// fallback never fails once the request is valid, so callers of this planner
/// always get a plan or an argument error for a missing main image.
/// </summary>
public sealed class ResilientPlanner : IPlanner
{
    private readonly IPlanner _primary;
    private readonly IPlanner _fallback;
    private readonly Action<Exception>? _onDegrade;

    public ResilientPlanner(IPlanner primary, IPlanner fallback, Action<Exception>? onDegrade = null)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _onDegrade = onDegrade;
    }

    public async Task<EditPlan> PlanAsync(PlanRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await _primary.PlanAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _onDegrade?.Invoke(ex);
            return await _fallback.PlanAsync(request, ct).ConfigureAwait(false);
        }
    }
}