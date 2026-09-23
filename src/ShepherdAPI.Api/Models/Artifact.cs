namespace ShepherdAPI.Api.Models;

public class Artifact
{
    public long ArtifactID { get; set; }
    public long ExecutionID { get; set; }
    public int ArtifactTypeID { get; set; }
    public string OriginalName { get; set; } = string.Empty;
    public string OriginalURI { get; set; } = string.Empty;
    public string StoredURI { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long ByteLength { get; set; }
    public string SHA256 { get; set; } = string.Empty;
    public DateTime ReceivedUTC { get; set; }
    public DateTime? StoredUTC { get; set; }
}
