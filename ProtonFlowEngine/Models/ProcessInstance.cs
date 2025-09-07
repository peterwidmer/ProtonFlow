namespace BpmnEngine.Models;

public class ProcessInstance
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");
    public string ProcessDefinitionId { get; init; } = string.Empty;
    public string ProcessKey { get; init; } = string.Empty;
    public Dictionary<string, object?> Variables { get; } = new();
    public HashSet<string> ActiveTokens { get; } = new();
    public bool IsCompleted { get; set; }
    public bool SimulationMode { get; set; }

    // Tracks how many incoming branch tokens have arrived to a parallel join gateway
    public Dictionary<string, int> ParallelJoinWaits { get; } = new();
}
