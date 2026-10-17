namespace ShepherdAPI.Api.Models;

/// <summary>ScoreRule.RowState values. 'A' is the only live value; 'D' is a deliberate soft-delete kept for history.</summary>
public static class RowState
{
    public const char Active = 'A';
    public const char Deleted = 'D';
}
