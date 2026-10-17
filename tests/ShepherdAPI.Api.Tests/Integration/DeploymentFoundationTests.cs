using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>The fixture schema matches the baseline, and the deployment-script mechanism is ordered and re-runnable.</summary>
[Collection("Database")]
public class DeploymentFoundationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task FixtureSchema_MatchesV8()
    {
        Assert.Equal("Create_Experiments_Database_And_Tables-8.sql", RepoPaths.BaselineDdlFileName);

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();

        Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = 'ScoreObservationSnapshot';"));
        Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = 'WeightedScore';"));
        Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_ScoreRule_RowState';"));
        Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM sys.default_constraints WHERE name = 'DF_ScoreRule_RowState';"));
        Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ExperimentVersion') AND name = 'LockedUTC';"));
        Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM sys.key_constraints WHERE name = 'PK_ScoreObservationSnapshot';"));
    }

    [Fact]
    public async Task ApplyingTheRepositoryScriptsTwice_Succeeds()
    {
        await fixture.ApplyDeploymentScriptsAsync();
        await fixture.ApplyDeploymentScriptsAsync();
    }

    [Fact]
    public async Task ScriptFolder_AppliesInFilenameOrder_WithGoBatches_AndIsRerunnable()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shepherd-scripts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // 020 depends on 010 and is named so a naive (non-ordinal) listing could not reorder them by luck.
            File.WriteAllText(Path.Combine(folder, "020_second.sql"),
                "CREATE OR ALTER VIEW dbo.vDeployTest2 AS SELECT n + 1 AS n FROM dbo.vDeployTest1;");
            File.WriteAllText(Path.Combine(folder, "010_first.sql"),
                "CREATE OR ALTER VIEW dbo.vDeployTest1 AS SELECT 41 AS n;\nGO\nSELECT 1;");
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "this is not a script and must be ignored");

            await SqlScriptRunner.ApplyFolderAsync(fixture.ConnectionString, folder);
            await SqlScriptRunner.ApplyFolderAsync(fixture.ConnectionString, folder);

            using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
            Assert.Equal(42, await connection.QuerySingleAsync<int>("SELECT n FROM dbo.vDeployTest2;"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            await using var cleanup = new SqlConnection(fixture.ConnectionString);
            await cleanup.OpenAsync();
            await cleanup.ExecuteAsync("DROP VIEW IF EXISTS dbo.vDeployTest2; DROP VIEW IF EXISTS dbo.vDeployTest1;");
        }
    }

    [Fact]
    public void SplitBatches_SplitsOnGoLines_OnlyWhenGoStandsAlone()
    {
        var batches = SqlScriptRunner.SplitBatches("SELECT 'GO to market';\r\nGO\r\nSELECT 2;\nGO\n\nSELECT 3");

        Assert.Equal(["SELECT 'GO to market';", "SELECT 2;", "SELECT 3"], batches);
    }

    [Fact]
    public void RepositoryDeploymentFolders_AreUpgradeThenProgrammability()
    {
        var names = RepoPaths.DeploymentFolders().Select(Path.GetFileName).ToList();

        Assert.Equal(["upgrade", "programmability"], names);
    }
}
