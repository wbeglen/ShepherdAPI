using System.Data;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Services;
using ShepherdAPI.Api.Tests.Infrastructure;
using Xunit;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>
/// Issue #10: controlled-write procedures own the transaction they begin and are called standalone. Called inside a
/// caller's transaction they fail with THROW 50000 before touching anything, leaving that transaction intact and
/// committable. The lock primitives and assertion helpers stay callable inside transactions. Every <c>usp_</c> procedure
/// found in the database is exercised, so a controlled-write procedure added later is covered automatically.
/// </summary>
[Collection("Database")]
public class StandaloneTransactionIntegrationTests(DatabaseFixture fixture)
{
    private static readonly HashSet<string> Helpers = new(StringComparer.OrdinalIgnoreCase)
    {
        "usp_AcquireVersionLock", "usp_LockVersions", "usp_AssertVersionsUnlocked", "usp_AssertDesignWritable", "usp_AssertDesignNotFrozen",
    };

    private sealed record Param(string Name, string Type, bool IsOutput);

    private sealed record Proc(string Name, IReadOnlyList<Param> Parameters);

    private async Task<List<Proc>> ControlledProceduresAsync()
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        var rows = (await connection.QueryAsync<(string ProcName, string ParamName, string ParamType, bool IsOutput)>(
            """
            SELECT p.name AS ProcName, pr.name AS ParamName, TYPE_NAME(pr.user_type_id) AS ParamType, pr.is_output AS IsOutput
            FROM sys.procedures AS p
            JOIN sys.parameters AS pr ON pr.object_id = p.object_id
            WHERE p.name LIKE N'usp[_]%'
              AND pr.is_readonly = 0 -- a table-valued parameter cannot be NULL; omitted, it is an empty table
            ORDER BY p.name, pr.parameter_id;
            """)).ToList();

        return rows
            .GroupBy(r => r.ProcName)
            .Where(g => !Helpers.Contains(g.Key) && !g.Key.StartsWith("usp_Test", StringComparison.OrdinalIgnoreCase))
            .Select(g => new Proc(g.Key, g.Select(r => new Param(r.ParamName, r.ParamType, r.IsOutput)).ToList()))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// A batch calling the procedure with the first input set to a nonexistent id and every other input NULL. Output
    /// parameters get variables. Returns the T-SQL and the names of the variables for outputs whose name ends in "Found".
    /// </summary>
    private static (string Sql, List<string> FoundVariables) CallBatch(Proc proc)
    {
        var declarations = new StringBuilder();
        var arguments = new List<string>();
        var found = new List<string>();
        var firstInput = true;

        foreach (var p in proc.Parameters)
        {
            if (p.IsOutput)
            {
                var variable = "@v_" + p.Name.TrimStart('@');
                declarations.AppendLine($"DECLARE {variable} {(p.Type == "datetime2" ? "datetime2(3)" : p.Type)};");
                arguments.Add($"{p.Name} = {variable} OUTPUT");
                if (p.Name.EndsWith("Found", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(variable);
                }
            }
            else
            {
                arguments.Add($"{p.Name} = {(firstInput ? "-1" : "NULL")}");
                firstInput = false;
            }
        }

        return ($"{declarations}EXEC dbo.{proc.Name} {string.Join(", ", arguments)};", found);
    }

    [Fact]
    public async Task EveryControlledWriteProcedure_IsDiscovered()
    {
        var names = (await ControlledProceduresAsync()).Select(p => p.Name).ToHashSet();

        foreach (var expected in new[]
        {
            "usp_CreateExecution", "usp_CreateScoreObservation", "usp_UpdateScoreObservationContent",
            "usp_UpdateExperimentVersion", "usp_CreateIteration", "usp_UpdateIteration", "usp_DeleteIteration",
            "usp_CreateParameter", "usp_UpdateParameter", "usp_DeleteParameter",
            "usp_CreateScoreRule", "usp_UpdateScoreRule", "usp_DeleteScoreRule", "usp_FinalizeExperimentVersion",
        })
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public async Task InsideACallersTransaction_EveryControlledProcedure_IsRejected_WithoutHarmingTheCaller()
    {
        var procedures = await ControlledProceduresAsync();
        Assert.NotEmpty(procedures);

        foreach (var proc in procedures)
        {
            var marker = "StandaloneMarker-" + Guid.NewGuid().ToString("N");
            var (call, _) = CallBatch(proc);

            using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
            using var transaction = connection.BeginTransaction();
            await connection.ExecuteAsync("INSERT dbo.ScoreCategory (Name, HigherIsBetter) VALUES (@marker, 1);", new { marker }, transaction);

            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(call, transaction: transaction));

            Assert.True(ex.Number == 50000, $"{proc.Name}: expected 50000 but got {ex.Number} ({ex.Message})");
            Assert.Contains(proc.Name, ex.Message);

            // The caller's transaction is untouched: same count, not doomed, earlier work present and committable.
            Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT @@TRANCOUNT;", transaction: transaction));
            Assert.True(1 == await connection.QuerySingleAsync<int>("SELECT XACT_STATE();", transaction: transaction),
                $"{proc.Name}: the caller's transaction was doomed");
            Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM dbo.ScoreCategory WHERE Name = @marker;", new { marker }, transaction));

            transaction.Commit();
            Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM dbo.ScoreCategory WHERE Name = @marker;", new { marker }));
        }
    }

    [Fact]
    public async Task TheStandaloneRejection_IsAnInternalError_NotACatalogEntry()
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "EXEC dbo.usp_UpdateIteration @IterationID = -1, @Sequence = 1, @IterationLabel = N'x', @Prompt = N'x', @Found = NULL;", transaction: transaction));

        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(ex);

        Assert.Equal(50000, ex.Number);
        Assert.Equal(500, status);
        Assert.Equal("internal-error", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Standalone_EveryControlledProcedure_ReportsNotFound_AndLeavesNoTransactionOpen()
    {
        foreach (var proc in await ControlledProceduresAsync())
        {
            var (call, foundVariables) = CallBatch(proc);
            Assert.NotEmpty(foundVariables); // every controlled procedure reports "not found" through an output flag

            using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
            var selects = string.Join(", ", foundVariables.Select((v, i) => $"CAST(ISNULL({v}, 0) AS int) AS F{i}"));
            var row = await connection.QuerySingleAsync<dynamic>($"{call}\nSELECT @@TRANCOUNT AS TranCount, {selects};");

            Assert.True((int)row.TranCount == 0, $"{proc.Name}: a transaction was left open");
            Assert.True((int)row.F0 == 0, $"{proc.Name}: expected the not-found result");
            Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT @@TRANCOUNT;"));
        }
    }

    [Fact]
    public async Task TheLockPrimitivesAndAssertionHelpers_StayCallableInsideTransactions()
    {
        var version = (await new ExperimentDesignService(new ExperimentDesignRepository(fixture.ConnectionFactory))
            .CreateExperimentVersionAsync(new CreateExperimentVersionRequest
            {
                Name = $"Standalone-{Guid.NewGuid():N}", VersionLabel = "v1", StatusID = fixture.DraftStatusId,
                EffectiveDateUTC = DateTime.UtcNow, Description = "standalone helpers",
            })).ExperimentVersionID;

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync($"""
            DECLARE @v dbo.VersionIdList;
            INSERT @v (ExperimentVersionID) VALUES ({version});
            EXEC dbo.usp_LockVersions @Versions = @v;
            EXEC dbo.usp_LockVersions @Versions = @v, @Exclusive = 1;
            EXEC dbo.usp_AssertVersionsUnlocked @Versions = @v;
            EXEC dbo.usp_AssertDesignNotFrozen @Versions = @v;
            EXEC dbo.usp_AssertDesignWritable @ExperimentVersionID = {version}, @LockedUTC = NULL;
            DECLARE @found bit, @locked datetime2(3);
            EXEC dbo.usp_AcquireVersionLock {version}, 0, @found OUTPUT, @locked OUTPUT;
            """, transaction: transaction);

        Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT @@TRANCOUNT;", transaction: transaction));
        transaction.Rollback();
    }

    [Fact]
    public async Task Finalization_RejectedInsideACallersTransaction_NeitherSetsNorLeavesTheToken()
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "EXEC dbo.usp_FinalizeExperimentVersion @ExperimentVersionID = -1, @Found = NULL, @LockedUTC = NULL;", transaction: transaction));

        Assert.Null(await connection.QuerySingleAsync<long?>(
            "SELECT TRY_CONVERT(bigint, SESSION_CONTEXT(N'ShepherdAPI.FinalizingVersionID'));", transaction: transaction));
        Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT XACT_STATE();", transaction: transaction));
        transaction.Rollback();
    }

    [Fact]
    public async Task Finalization_FailingACheck_LeavesNoTransactionOpen_AndTheNextCallStillWorks()
    {
        var version = (await new ExperimentDesignService(new ExperimentDesignRepository(fixture.ConnectionFactory))
            .CreateExperimentVersionAsync(new CreateExperimentVersionRequest
            {
                Name = $"Standalone-{Guid.NewGuid():N}", VersionLabel = "v1", StatusID = fixture.DraftStatusId,
                EffectiveDateUTC = DateTime.UtcNow, Description = "no executions",
            })).ExperimentVersionID;

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DECLARE @f bit, @l datetime2(3); EXEC dbo.usp_FinalizeExperimentVersion @ExperimentVersionID = @v, @Found = @f OUTPUT, @LockedUTC = @l OUTPUT;",
            new { v = version }));

        Assert.Equal(50500, ex.Number);
        Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT @@TRANCOUNT;"));
        Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT 1;"));
    }
}
