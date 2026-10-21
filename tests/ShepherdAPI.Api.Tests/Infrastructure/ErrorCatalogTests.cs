using System.Net;
using System.Text.RegularExpressions;
using ShepherdAPI.Api.Infrastructure;
using Xunit;

namespace ShepherdAPI.Api.Tests.Infrastructure;

public class ErrorCatalogTests
{
    // Every catalog entry, with the status, code and (where it has one) THROW number it must keep.
    // Adding an entry to ErrorCatalog.All without a row here fails Catalog_HasNoUntestedEntries.
    public static TheoryData<string, HttpStatusCode, int?> Expected => new()
    {
        { "not-found", HttpStatusCode.NotFound, null },
        { "record-locked", HttpStatusCode.Conflict, null },
        { "conflict", HttpStatusCode.Conflict, null },
        { "validation-failed", HttpStatusCode.BadRequest, null },
        { "duplicate-key", HttpStatusCode.Conflict, null },
        { "constraint-violation", HttpStatusCode.Conflict, null },
        { "internal-error", HttpStatusCode.InternalServerError, null },
        { "version-locked", HttpStatusCode.Conflict, 50100 },
        { "identity-change-rejected", HttpStatusCode.Conflict, 50101 },
        { "invalid-rule-configuration", HttpStatusCode.Conflict, 50200 },
        { "version-phase-mismatch", HttpStatusCode.Conflict, 50300 },
        { "out-of-range-score", HttpStatusCode.BadRequest, 50302 },
        { "duplicate-observation", HttpStatusCode.Conflict, null },
        { "design-frozen", HttpStatusCode.Conflict, 50400 },
        { "execution-delete-rejected", HttpStatusCode.Conflict, 50403 },
        { "finalize-no-executions", HttpStatusCode.Conflict, 50500 },
        { "finalize-execution-not-ended", HttpStatusCode.Conflict, 50501 },
        { "finalize-execution-without-observation", HttpStatusCode.Conflict, 50502 },
        { "finalize-execution-phase-not-configured", HttpStatusCode.Conflict, 50503 },
        { "finalize-phase-without-observation", HttpStatusCode.Conflict, 50504 },
        { "finalize-phase-weight-invalid", HttpStatusCode.Conflict, 50505 },
        { "finalize-observation-integrity", HttpStatusCode.Conflict, 50506 },
        { "finalize-snapshot-rows-exist", HttpStatusCode.Conflict, 50507 },
        { "snapshot-insert-not-permitted", HttpStatusCode.Conflict, 50508 },
        { "lock-requires-finalization", HttpStatusCode.Conflict, 50509 },
        { "retryable-conflict", HttpStatusCode.Conflict, null },
    };

    [Theory]
    [MemberData(nameof(Expected))]
    public void Entry_HasExpectedStatusCodeAndSqlNumber(string code, HttpStatusCode status, int? sqlNumber)
    {
        var entry = ErrorCatalog.FindByCode(code);

        Assert.NotNull(entry);
        Assert.Equal(status, entry!.Status);
        Assert.Equal(sqlNumber, entry.SqlNumber);
        Assert.False(string.IsNullOrWhiteSpace(entry.Title));
    }

    [Fact]
    public void Catalog_HasNoUntestedEntries()
    {
        var tested = Expected.Select(row => (string)row[0]).ToHashSet();

        Assert.Equal(tested, ErrorCatalog.All.Select(e => e.Code).ToHashSet());
    }

    [Fact]
    public void Codes_AreUniqueKebabCase()
    {
        Assert.Equal(ErrorCatalog.All.Count, ErrorCatalog.All.Select(e => e.Code).Distinct().Count());
        Assert.All(ErrorCatalog.All, e => Assert.Matches(new Regex("^[a-z]+(-[a-z]+)*$"), e.Code));
    }

    [Fact]
    public void SqlNumbers_AreUniqueAndInsideTheReservedRanges()
    {
        var numbers = ErrorCatalog.All.Where(e => e.SqlNumber is not null).Select(e => e.SqlNumber!.Value).ToList();

        Assert.Equal(numbers.Count, numbers.Distinct().Count());
        Assert.All(numbers, n => Assert.True(ErrorCatalog.IsInReservedRange(n), $"{n} is outside the reserved ranges"));
    }

    [Theory]
    [InlineData("version-locked", ErrorCatalog.SharedRangeStart)]
    [InlineData("identity-change-rejected", ErrorCatalog.SharedRangeStart)]
    [InlineData("invalid-rule-configuration", ErrorCatalog.ExecutionGuardRangeStart)]
    [InlineData("version-phase-mismatch", ErrorCatalog.ObservationRangeStart)]
    [InlineData("out-of-range-score", ErrorCatalog.ObservationRangeStart)]
    [InlineData("design-frozen", ErrorCatalog.DesignFreezeRangeStart)]
    [InlineData("finalize-no-executions", ErrorCatalog.FinalizationRangeStart)]
    [InlineData("snapshot-insert-not-permitted", ErrorCatalog.FinalizationRangeStart)]
    public void Entry_LivesInItsOwnersRange(string code, int rangeStart)
    {
        var number = ErrorCatalog.FindByCode(code)!.SqlNumber!.Value;

        Assert.InRange(number, rangeStart, rangeStart + ErrorCatalog.RangeSize - 1);
    }

    [Theory]
    [InlineData(50301, "unavailable-rule")]
    [InlineData(50401, "score-rule-already-deleted")]
    [InlineData(50402, "score-rule-invalid-rowstate")]
    public void NumbersAndCodesRetiredWithRowState_AreNotReused(int sqlNumber, string code)
    {
        Assert.Null(ErrorCatalog.FindBySqlNumber(sqlNumber));
        Assert.Null(ErrorCatalog.FindByCode(code));
    }

    [Fact]
    public void OnlyTheRetryableConflict_IsRetryable()
    {
        Assert.Equal(["retryable-conflict"], ErrorCatalog.All.Where(e => e.Retryable).Select(e => e.Code));
    }
}
