using Microsoft.Data.SqlClient;
using ShepherdAPI.Api.Infrastructure;
using Xunit;

namespace ShepherdAPI.Api.Tests.Infrastructure;

/// <summary>The retry policy of Issue #8, without a database: exactly one retry, only for a deadlock victim (1205).</summary>
public class DeadlockRetryTests
{
    [Fact]
    public async Task ASuccessfulOperation_RunsOnce()
    {
        var attempts = 0;

        var result = await DeadlockRetry.RunAsync(() =>
        {
            attempts++;
            return Task.FromResult(42);
        });

        Assert.Equal(42, result);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ADeadlockVictim_IsRetriedOnce_AndTheSecondResultIsReturned()
    {
        var attempts = 0;

        var result = await DeadlockRetry.RunAsync(() =>
        {
            attempts++;
            return attempts == 1 ? throw SqlExceptionFactory.Create(1205) : Task.FromResult("second attempt");
        });

        Assert.Equal("second attempt", result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ASecondDeadlock_Propagates_AndMapsToTheRetryableConflict()
    {
        var attempts = 0;

        var thrown = await Assert.ThrowsAsync<SqlException>(() => DeadlockRetry.RunAsync<int>(() =>
        {
            attempts++;
            throw SqlExceptionFactory.Create(1205);
        }));

        Assert.Equal(2, attempts); // exactly one retry, never a third attempt
        Assert.Equal(ErrorCatalog.DeadlockVictim, thrown.Number);

        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(thrown);
        Assert.Equal(409, status);
        Assert.Equal("retryable-conflict", body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("retryable").GetBoolean());
    }

    [Theory]
    [InlineData(1222)]  // lock-wait timeout: already waited the whole bound; stays the client's retryable-conflict
    [InlineData(50100)] // a catalog error is final
    [InlineData(2627)]  // so is a constraint violation
    public async Task AnyOtherSqlError_IsNotRetried(int number)
    {
        var attempts = 0;

        var thrown = await Assert.ThrowsAsync<SqlException>(() => DeadlockRetry.RunAsync<int>(() =>
        {
            attempts++;
            throw SqlExceptionFactory.Create(number);
        }));

        Assert.Equal(1, attempts);
        Assert.Equal(number, thrown.Number);
    }

    [Fact]
    public async Task ANonSqlException_IsNotRetried()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => DeadlockRetry.RunAsync<int>(() =>
        {
            attempts++;
            throw new InvalidOperationException("not a database error");
        }));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void TheAttemptBound_IsTwo()
    {
        Assert.Equal(2, DeadlockRetry.MaxAttempts);
    }
}
