namespace ShepherdAPI.Api.Infrastructure;

/// <summary>A catalog entry plus the client-facing detail text to return with it.</summary>
public sealed record MappedError(ErrorEntry Entry, string Detail);

/// <summary>
/// Maps a SQL Server error number (and message) to a catalog entry. Kept free of SqlException so
/// it can be unit-tested directly; the middleware passes <c>ex.Number</c> and <c>ex.Message</c>.
/// </summary>
public static class SqlErrorMapper
{
    public const int UniqueConstraintViolation = 2627;
    public const int UniqueIndexViolation = 2601;
    public const int ForeignKeyViolation = 547;

    public const string DuplicateObservationConstraint = "UC_ScoreObservation_ExecutionID_ScoreRuleID_RaterID";

    public static bool TryMap(int number, string message, out MappedError mapped)
    {
        switch (number)
        {
            case ErrorCatalog.DeadlockVictim:
                mapped = new MappedError(ErrorCatalog.RetryableConflict,
                    "The operation was chosen as a deadlock victim because of a concurrent request. Retry the request.");
                return true;

            case ErrorCatalog.LockTimeout:
                mapped = new MappedError(ErrorCatalog.RetryableConflict,
                    "The operation timed out waiting for a lock held by a concurrent request. Retry the request.");
                return true;

            case UniqueConstraintViolation or UniqueIndexViolation
                when message.Contains(DuplicateObservationConstraint, StringComparison.OrdinalIgnoreCase):
                mapped = new MappedError(ErrorCatalog.DuplicateObservation,
                    "An observation for this Execution, ScoreRule and Rater already exists.");
                return true;

            case UniqueConstraintViolation or UniqueIndexViolation:
                mapped = new MappedError(ErrorCatalog.DuplicateKey, "A record with the same unique key already exists.");
                return true;

            case ForeignKeyViolation:
                mapped = new MappedError(ErrorCatalog.ConstraintViolation,
                    "The request references or is referenced by a related record that prevents this operation.");
                return true;
        }

        var entry = ErrorCatalog.FindBySqlNumber(number);
        if (entry is not null)
        {
            mapped = new MappedError(entry, message);
            return true;
        }

        mapped = null!;
        return false;
    }
}
