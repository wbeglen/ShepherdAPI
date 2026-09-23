namespace ShepherdAPI.Api.Models;

public class Parameter
{
    public long ParameterID { get; set; }
    public long ExperimentVersionID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string? Unit { get; set; }
    public string? Notes { get; set; }
}
