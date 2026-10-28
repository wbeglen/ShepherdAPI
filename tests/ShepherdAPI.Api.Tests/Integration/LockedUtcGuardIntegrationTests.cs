using Dapper;
using Microsoft.Data.SqlClient;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Services;
using ShepherdAPI.Api.Tests.Infrastructure;
using Xunit;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>
/// Issue #9: ExperimentVersion.LockedUTC can be assigned only by finalization. dbo.trg_ExperimentVersion_Guard rejects an
/// INSERT with LockedUTC set and admits a NULL-to-value update only while the finalization session-context key names the
/// same ExperimentVersionID. The key is an accident guard, not a security boundary (permissions: Issue #11). Real SQL Server.
/// </summary>
[Collection("Database")]
public class LockedUtcGuardIntegrationTests(DatabaseFixture fixture)
{
    private const int LockRequiresFinalization = 50509;
    private const int VersionLocked = 50100;

    private ExperimentDesignService Design() => new(new ExperimentDesignRepository(fixture.ConnectionFactory));

    private async Task<long> NewVersionAsync() => (await Design().CreateExperimentVersionAsync(new CreateExperimentVersionRequest
    {
        Name = $"LockedUtc-{Guid.NewGuid():N}", VersionLabel = "v1", StatusID = fixture.DraftStatusId,
        EffectiveDateUTC = DateTime.UtcNow, Description = "LockedUTC guard test",
    })).ExperimentVersionID;

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    private Task<DateTime?> LockedUtcAsync(long version) =>
        ScalarAsync<DateTime?>("SELECT LockedUTC FROM dbo.ExperimentVersion WHERE ExperimentVersionID = @version;", new { version });

    private static async Task AssertRejectedAsync(SqlException ex, int number, string code)
    {
        Assert.Equal(number, ex.Number);
        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(ex);
        Assert.Equal(409, status);
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    private async Task<SqlException> RunRejectedAsync(string sql, long? token = null)
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        if (token is not null)
        {
            await LockedUtcTestHelper.SetTokenAsync(connection, token);
        }

        try
        {
            return await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql));
        }
        finally
        {
            await LockedUtcTestHelper.SetTokenAsync(connection, null);
        }
    }

    [Fact]
    public async Task AnInsertWithLockedUtc_IsRejected_WithOrWithoutTheToken()
    {
        const string insert = """
            INSERT dbo.ExperimentVersion (Name, VersionLabel, StatusID, EffectiveDateUTC, Description, LockedUTC)
            VALUES (@name, N'v1', @status, SYSUTCDATETIME(), N'born locked', SYSUTCDATETIME());
            """;
        var name = $"BornLocked-{Guid.NewGuid():N}";

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        var plain = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(insert, new { name, status = fixture.DraftStatusId }));
        await AssertRejectedAsync(plain, LockRequiresFinalization, "lock-requires-finalization");

        // A key naming any value does not help an INSERT: finalization only ever updates.
        await LockedUtcTestHelper.SetTokenAsync(connection, 1);
        var withToken = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(insert, new { name, status = fixture.DraftStatusId }));
        await LockedUtcTestHelper.SetTokenAsync(connection, null);
        Assert.Equal(LockRequiresFinalization, withToken.Number);

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ExperimentVersion WHERE Name = @name;", new { name }));
    }

    [Fact]
    public async Task AnInsertWithoutLockedUtc_StillWorks()
    {
        var version = await NewVersionAsync();

        Assert.Null(await LockedUtcAsync(version));
    }

    [Fact]
    public async Task ANullToValueUpdate_WithoutTheToken_IsRejectedWithTheNewError()
    {
        var version = await NewVersionAsync();

        var ex = await RunRejectedAsync($"UPDATE dbo.ExperimentVersion SET LockedUTC = SYSUTCDATETIME() WHERE ExperimentVersionID = {version};");

        await AssertRejectedAsync(ex, LockRequiresFinalization, "lock-requires-finalization");
        Assert.Null(await LockedUtcAsync(version));
    }

    [Fact]
    public async Task AMatchingToken_PermitsTheUpdate_AndTheHelperClearsItAfterwards()
    {
        var version = await NewVersionAsync();

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        await LockedUtcTestHelper.LockAsync(connection, version);

        Assert.NotNull(await LockedUtcAsync(version));
        Assert.Null(await connection.QuerySingleAsync<long?>("SELECT TRY_CONVERT(bigint, SESSION_CONTEXT(N'ShepherdAPI.FinalizingVersionID'));"));
    }

    [Fact]
    public async Task ATokenForOneVersion_CannotAuthorizeAnotherVersionsTransition()
    {
        var authorized = await NewVersionAsync();
        var other = await NewVersionAsync();

        var ex = await RunRejectedAsync($"UPDATE dbo.ExperimentVersion SET LockedUTC = SYSUTCDATETIME() WHERE ExperimentVersionID = {other};", token: authorized);

        await AssertRejectedAsync(ex, LockRequiresFinalization, "lock-requires-finalization");
        Assert.Null(await LockedUtcAsync(other));
        Assert.Null(await LockedUtcAsync(authorized));
    }

    [Fact]
    public async Task AGarbageToken_AuthorizesNothing()
    {
        var version = await NewVersionAsync();

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = N'not a number';");
        var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            $"UPDATE dbo.ExperimentVersion SET LockedUTC = SYSUTCDATETIME() WHERE ExperimentVersionID = {version};"));
        await LockedUtcTestHelper.SetTokenAsync(connection, null);

        Assert.Equal(LockRequiresFinalization, ex.Number);
        Assert.Null(await LockedUtcAsync(version));
    }

    [Fact]
    public async Task AMultirowUpdate_WithAnyUnauthorizedTransition_FailsAsAWhole()
    {
        var authorized = await NewVersionAsync();
        var other = await NewVersionAsync();

        var ex = await RunRejectedAsync(
            $"UPDATE dbo.ExperimentVersion SET LockedUTC = SYSUTCDATETIME() WHERE ExperimentVersionID IN ({authorized}, {other});", token: authorized);

        Assert.Equal(LockRequiresFinalization, ex.Number);
        Assert.Null(await LockedUtcAsync(authorized)); // even the authorized row stays unlocked
        Assert.Null(await LockedUtcAsync(other));
    }

    [Fact]
    public async Task AMultirowUpdate_WhereEveryRowIsAuthorized_Succeeds()
    {
        var version = await NewVersionAsync();

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        await LockedUtcTestHelper.SetTokenAsync(connection, version);
        await connection.ExecuteAsync($"UPDATE dbo.ExperimentVersion SET LockedUTC = SYSUTCDATETIME(), StatusID = StatusID WHERE ExperimentVersionID IN ({version}, {version});");
        await LockedUtcTestHelper.SetTokenAsync(connection, null);

        Assert.NotNull(await LockedUtcAsync(version));
    }

    [Fact]
    public async Task AnAlreadyLockedVersion_StillReports50100First_EvenWithAMatchingToken()
    {
        var version = await NewVersionAsync();
        await LockedUtcTestHelper.LockAsync(fixture.ConnectionFactory, version);
        var lockedAt = await LockedUtcAsync(version);

        foreach (var sql in new[]
        {
            $"UPDATE dbo.ExperimentVersion SET LockedUTC = NULL WHERE ExperimentVersionID = {version};",
            $"UPDATE dbo.ExperimentVersion SET LockedUTC = DATEADD(SECOND, 1, LockedUTC) WHERE ExperimentVersionID = {version};",
            $"UPDATE dbo.ExperimentVersion SET Description = N'changed' WHERE ExperimentVersionID = {version};",
            $"DELETE dbo.ExperimentVersion WHERE ExperimentVersionID = {version};",
        })
        {
            var ex = await RunRejectedAsync(sql, token: version);
            await AssertRejectedAsync(ex, VersionLocked, "version-locked");

            var noToken = await RunRejectedAsync(sql);
            Assert.Equal(VersionLocked, noToken.Number);
        }

        Assert.Equal(lockedAt, await LockedUtcAsync(version));
    }

    [Fact]
    public async Task TheHelper_ClearsTheToken_EvenWhenItsUpdateFails()
    {
        var version = await NewVersionAsync();

        // Lock it a day ahead, so a second assignment of "now" is a different value and therefore a rejected change.
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        await LockedUtcTestHelper.SetTokenAsync(connection, version);
        await connection.ExecuteAsync("UPDATE dbo.ExperimentVersion SET LockedUTC = DATEADD(DAY, 1, SYSUTCDATETIME()) WHERE ExperimentVersionID = @version;", new { version });
        await LockedUtcTestHelper.SetTokenAsync(connection, null);

        await Assert.ThrowsAsync<SqlException>(() => LockedUtcTestHelper.LockAsync(connection, version)); // 50100: it is already locked

        Assert.Null(await connection.QuerySingleAsync<long?>("SELECT TRY_CONVERT(bigint, SESSION_CONTEXT(N'ShepherdAPI.FinalizingVersionID'));"));
    }

    [Fact]
    public async Task UpdatesThatDoNotAssignLockedUtc_NeedNoToken()
    {
        var version = await NewVersionAsync();

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        await connection.ExecuteAsync(
            "UPDATE dbo.ExperimentVersion SET Description = N'edited', StatusID = @status WHERE ExperimentVersionID = @version;",
            new { version, status = fixture.RetiredStatusId });

        Assert.Null(await LockedUtcAsync(version));
    }
}
