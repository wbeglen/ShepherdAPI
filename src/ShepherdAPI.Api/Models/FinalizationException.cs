namespace ShepherdAPI.Api.Models;

/// <summary>
/// An exception accepted when an ExperimentVersion was finalized (V11): an Execution whose end time is unknown
/// (ExceptionCode "end-time-unknown"), with the mandatory explanation given at finalization. Written only by
/// finalization, in the same transaction; RecordedUTC equals the version's LockedUTC. Immutable.
/// </summary>
public class FinalizationException
{
    public long ExperimentVersionID { get; set; }
    public long ExecutionID { get; set; }
    public string ExceptionCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public DateTime RecordedUTC { get; set; }
}
