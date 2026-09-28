using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using ShepherdAPI.Api.Data;
using Xunit;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>
/// Creates a disposable database (schema taken directly from the authoritative
/// database/Create_Experiments_Database_And_Tables-6.sql, not a hand-copied duplicate) so
/// integration tests exercise real SQL Server constraints without ever touching the real,
/// already-populated Experiments database. Torn down after the test run.
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    private const string TestDatabaseName = "ShepherdAPI_IntegrationTest";
    private const string MasterConnectionString = "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True;";

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
        await RunTableDdlAsync();
        await SeedReferenceDataAsync();
        ConnectionFactory = new FixedConnectionFactory(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await using var master = new SqlConnection(MasterConnectionString);
        await master.OpenAsync();
        await using var cmd = new SqlCommand(
            $"""
            IF EXISTS (SELECT 1 FROM sys.databases WHERE name = '{TestDatabaseName}')
            BEGIN
                ALTER DATABASE [{TestDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{TestDatabaseName}];
            END
            """, master);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task RecreateDatabaseAsync()
    {
        await using var master = new SqlConnection(MasterConnectionString);
        await master.OpenAsync();
        await using var cmd = new SqlCommand(
            $"""
            IF EXISTS (SELECT 1 FROM sys.databases WHERE name = '{TestDatabaseName}')
            BEGIN
                ALTER DATABASE [{TestDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{TestDatabaseName}];
            END
            CREATE DATABASE [{TestDatabaseName}];
            """, master);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task RunTableDdlAsync()
    {
        var batches = ExtractTableDdlBatches();

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in batches)
        {
            await using var cmd = new SqlCommand(batch, connection);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static List<string> ExtractTableDdlBatches()
    {
        var sql = File.ReadAllText(FindDdlFilePath());

        const string startMarker = "USE [Experiments]";
        const string endMarker = "USE [master]";
        var startIdx = sql.IndexOf(startMarker, StringComparison.Ordinal);
        var endIdx = sql.LastIndexOf(endMarker, StringComparison.Ordinal);
        var body = sql[(startIdx + startMarker.Length)..endIdx];

        return Regex.Split(body, @"(?im)^\s*GO\s*$")
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .ToList();
    }

    private static string FindDdlFilePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "database", "Create_Experiments_Database_And_Tables-6.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate database/Create_Experiments_Database_And_Tables-6.sql above the test output directory.");
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

    private sealed class FixedConnectionFactory(string connectionString) : ISqlConnectionFactory
    {
        public async Task<IDbConnection> CreateOpenConnectionAsync()
        {
            var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            return connection;
        }
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
