namespace BpmnEngine.Models;

public class ProcessInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string ProcessDefinitionId { get; set; } = string.Empty;
    public string ProcessKey { get; set; } = string.Empty;
    public Dictionary<string, object?> Variables { get; } = new Dictionary<string, object?>();
    public HashSet<string> ActiveTokens { get; } = new HashSet<string>();
    public bool IsCompleted { get; set; }
    public bool SimulationMode { get; set; }

    // Tracks how many incoming branch tokens have arrived to a parallel join gateway
    public Dictionary<string, int> ParallelJoinWaits { get; } = new Dictionary<string, int>();
}
