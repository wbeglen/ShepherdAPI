namespace ShepherdAPI.Api.Models;

public class ArtifactType
{
    public int ArtifactTypeID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
