namespace OptimizationCore;

public interface IBudgetedAI
{
    void ProcessAITick();
    PerceptionTier CurrentTier { get; set; }
}
