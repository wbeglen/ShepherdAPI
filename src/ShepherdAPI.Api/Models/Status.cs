namespace ShepherdAPI.Api.Models;

public class Status
{
    public int StatusID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
