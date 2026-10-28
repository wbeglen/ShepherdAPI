using Microsoft.Data.SqlClient;

namespace ShepherdAPI.Api.Infrastructure;

/// <summary>
/// One transparent retry after a deadlock-victim error (SQL 1205), for a single repository operation (Issue #8).
///
/// It is safe only because of how the operations it wraps are built, and it must not be used outside those limits:
///   - the operation is ONE call of ONE controlled-write procedure on a NEW connection, with no caller-supplied
///     transaction (the procedures reject one, Issue #10), so the victim's transaction has fully rolled back and
///     nothing from the failed attempt survives;
///   - the procedure is re-run from the start, so it re-reads and re-validates current state under the lock; the
///     decision of the failed attempt is never reused;
///   - callers pass only that repository call, never a service method, its pre-reads, or its follow-up read.
/// A lock-wait timeout (1222) is deliberately not retried: it already waited the full bound and stays the client's
/// retryable-conflict. If the second attempt is also a deadlock victim, its exception propagates and is reported as
/// the retryable-conflict response. Finalization is not wrapped: it stays client-retryable.
/// </summary>
public static class DeadlockRetry
{
    public const int MaxAttempts = 2;

    public static async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (SqlException ex) when (ex.Number == ErrorCatalog.DeadlockVictim)
        {
            return await operation();
        }
    }
}
