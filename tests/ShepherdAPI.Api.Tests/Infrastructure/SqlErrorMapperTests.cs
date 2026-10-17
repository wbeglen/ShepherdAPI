using ShepherdAPI.Api.Infrastructure;
using Xunit;

namespace ShepherdAPI.Api.Tests.Infrastructure;

public class SqlErrorMapperTests
{
    [Theory]
    [InlineData(1205)]
    [InlineData(1222)]
    public void DeadlockAndLockTimeout_MapToTheSameRetryableEntry(int number)
    {
        Assert.True(SqlErrorMapper.TryMap(number, "engine message", out var mapped));

        Assert.Same(ErrorCatalog.RetryableConflict, mapped.Entry);
        Assert.True(mapped.Entry.Retryable);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, mapped.Entry.Status);
    }

    [Fact]
    public void EveryThrowNumberInTheCatalog_MapsToItsEntry_WithTheThrownMessageAsDetail()
    {
        foreach (var entry in ErrorCatalog.All.Where(e => e.SqlNumber is not null))
        {
            Assert.True(SqlErrorMapper.TryMap(entry.SqlNumber!.Value, "thrown by proc", out var mapped));
            Assert.Same(entry, mapped.Entry);
            Assert.Equal("thrown by proc", mapped.Detail);
        }
    }

    [Theory]
    [InlineData(2627)]
    [InlineData(2601)]
    public void DuplicateKeyOnTheObservationConstraint_IsDuplicateObservation(int number)
    {
        var message = "Violation of UNIQUE KEY constraint 'UC_ScoreObservation_ExecutionID_ScoreRuleID_RaterID'. Cannot insert duplicate key.";

        Assert.True(SqlErrorMapper.TryMap(number, message, out var mapped));

        Assert.Same(ErrorCatalog.DuplicateObservation, mapped.Entry);
    }

    [Theory]
    [InlineData(2627)]
    [InlineData(2601)]
    public void DuplicateKeyOnAnyOtherConstraint_StaysTheGenericDuplicateKey(int number)
    {
        Assert.True(SqlErrorMapper.TryMap(number, "Violation of UNIQUE KEY constraint 'UC_Agent_Name'.", out var mapped));

        Assert.Same(ErrorCatalog.DuplicateKey, mapped.Entry);
        Assert.Equal("A record with the same unique key already exists.", mapped.Detail);
    }

    [Fact]
    public void ForeignKeyViolation_StaysTheGenericConstraintViolation()
    {
        Assert.True(SqlErrorMapper.TryMap(547, "The INSERT statement conflicted with the FOREIGN KEY constraint.", out var mapped));

        Assert.Same(ErrorCatalog.ConstraintViolation, mapped.Entry);
    }

    [Theory]
    [InlineData(208)]
    [InlineData(50000)]
    [InlineData(59999)]
    public void UnregisteredNumbers_AreNotMapped(int number)
    {
        Assert.False(SqlErrorMapper.TryMap(number, "anything", out _));
    }
}
