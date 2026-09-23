namespace ShepherdAPI.Api.Models;

public class Agent
{
    public int AgentID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = string.Empty;
    public string DefaultInterface { get; set; } = string.Empty;
    public bool Active { get; set; }
}
