using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using ShepherdAPI.Api.Data;
using Xunit;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>
/// Creates a disposable database (schema taken directly from the authoritative baseline script in
/// database/, plus the upgrade/ and programmability/ scripts, not a hand-copied duplicate) so
/// integration tests exercise real SQL Server behavior without ever touching the real,
/// already-populated Experiments database. Torn down after the test run.
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    private const string TestDatabaseName = "ShepherdAPI_IntegrationTest";
    private const string MasterConnectionString = "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True;";

    /// <summary>Lock timeout used by the fixture's connection factory; short so a test that blocks fails fast.</summary>
    public const int FixtureLockTimeoutMs = 5000;

    public string ConnectionString { get; } =
        $"Server=localhost;Database={TestDatabaseName};Trusted_Connection=True;TrustServerCertificate=True;";

    public ISqlConnectionFactory ConnectionFactory { get; private set; } = null!;

    // Seeded reference data IDs, populated in InitializeAsync, available to every test.
    public int DraftStatusId { get; private set; }
    public int LockedStatusId { get; private set; }
    public int RetiredStatusId { get; private set; }
    public int AgentId { get; private set; }
    public int PhaseId { get; private set; }
    public int ScoreCategoryId { get; private set; }
    public int ScoreCategoryId2 { get; private set; }
    public int ArtifactTypeId { get; private set; }
    public int RaterId { get; private set; }

    public async Task InitializeAsync()
    {
        await RecreateDatabaseAsync();
        await RunBaselineDdlAsync();
        await ApplyDeploymentScriptsAsync();
        await SeedReferenceDataAsync();
        ConnectionFactory = CreateConnectionFactory(FixtureLockTimeoutMs);
    }

    public async Task DisposeAsync()
    {
        await using var master = new SqlConnection(MasterConnectionString);
        await master.OpenAsync();
        await using var cmd = new SqlCommand(DropDatabaseSql(), master);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Applies upgrade/ then programmability/ (each in filename order). Safe to call repeatedly: the scripts must be re-runnable.</summary>
    public async Task ApplyDeploymentScriptsAsync()
    {
        foreach (var folder in RepoPaths.DeploymentFolders())
        {
            await SqlScriptRunner.ApplyFolderAsync(ConnectionString, folder);
        }
    }

    /// <summary>A real <see cref="SqlConnectionFactory"/> pointed at the test database. Pass null to leave Database:LockTimeoutMs unset.</summary>
    public SqlConnectionFactory CreateConnectionFactory(int? lockTimeoutMs)
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:Experiments"] = ConnectionString };
        if (lockTimeoutMs is not null)
        {
            settings[SqlConnectionFactory.LockTimeoutKey] = lockTimeoutMs.Value.ToString();
        }
        return new SqlConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    private static string DropDatabaseSql() =>
        $"""
        IF EXISTS (SELECT 1 FROM sys.databases WHERE name = '{TestDatabaseName}')
        BEGIN
            ALTER DATABASE [{TestDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE [{TestDatabaseName}];
        END
        """;

    private static async Task RecreateDatabaseAsync()
    {
        await using var master = new SqlConnection(MasterConnectionString);
        await master.OpenAsync();
        await using var cmd = new SqlCommand($"{DropDatabaseSql()}\nCREATE DATABASE [{TestDatabaseName}];", master);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task RunBaselineDdlAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await SqlScriptRunner.ApplyBatchesAsync(connection, ExtractBaselineBatches());
    }

    /// <summary>The baseline script minus its CREATE DATABASE header and trailing USE [master] footer.</summary>
    private static List<string> ExtractBaselineBatches()
    {
        var sql = File.ReadAllText(RepoPaths.BaselineDdl);

        const string startMarker = "USE [Experiments]";
        const string endMarker = "USE [master]";
        var startIdx = sql.IndexOf(startMarker, StringComparison.Ordinal);
        var endIdx = sql.LastIndexOf(endMarker, StringComparison.Ordinal);
        return SqlScriptRunner.SplitBatches(sql[(startIdx + startMarker.Length)..endIdx]);
    }

    private async Task SeedReferenceDataAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        DraftStatusId = await InsertAsync(connection, "INSERT INTO dbo.Status (Name) OUTPUT INSERTED.StatusID VALUES ('Draft');");
        LockedStatusId = await InsertAsync(connection, "INSERT INTO dbo.Status (Name) OUTPUT INSERTED.StatusID VALUES ('Locked');");
        RetiredStatusId = await InsertAsync(connection, "INSERT INTO dbo.Status (Name) OUTPUT INSERTED.StatusID VALUES ('Retired');");
        await InsertAsync(connection, "INSERT INTO dbo.Status (Name) OUTPUT INSERTED.StatusID VALUES ('Complete');");

        AgentId = await InsertAsync(connection,
            "INSERT INTO dbo.Agent (Name, Provider, DefaultModel, DefaultInterface, Active) OUTPUT INSERTED.AgentID VALUES ('TestAgent', 'TestProvider', 'test-model', 'cli', 1);");
        PhaseId = await InsertAsync(connection, "INSERT INTO dbo.Phase (Name, Sequence) OUTPUT INSERTED.PhaseID VALUES ('Implementation', 1);");
        ScoreCategoryId = await InsertAsync(connection,
            "INSERT INTO dbo.ScoreCategory (Name, HigherIsBetter) OUTPUT INSERTED.ScoreCategoryID VALUES ('Correctness', 1);");
        ScoreCategoryId2 = await InsertAsync(connection,
            "INSERT INTO dbo.ScoreCategory (Name, HigherIsBetter) OUTPUT INSERTED.ScoreCategoryID VALUES ('Traceability', 1);");
        ArtifactTypeId = await InsertAsync(connection, "INSERT INTO dbo.ArtifactType (Name) OUTPUT INSERTED.ArtifactTypeID VALUES ('Screenshot');");
        RaterId = await InsertAsync(connection, "INSERT INTO dbo.Rater (Name, RaterType) OUTPUT INSERTED.RaterID VALUES ('TestRater', 'Human');");
    }

    private static async Task<int> InsertAsync(SqlConnection connection, string sql)
    {
        await using var cmd = new SqlCommand(sql, connection);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
