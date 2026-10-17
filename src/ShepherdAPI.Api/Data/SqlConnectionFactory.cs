using System.Data;
using Microsoft.Data.SqlClient;

namespace ShepherdAPI.Api.Data;

/// <summary>
/// Opens connections to the Experiments database and applies the configured LOCK_TIMEOUT to each
/// one, so a request waiting on a lock fails with a retryable error (SQL 1222) instead of hanging
/// until the command timeout. Applied on every open because pooled connections do not carry
/// session settings forward reliably.
/// </summary>
public class SqlConnectionFactory : ISqlConnectionFactory
{
    public const string LockTimeoutKey = "Database:LockTimeoutMs";
    public const int DefaultLockTimeoutMs = 10000;

    private readonly string _connectionString;
    private readonly int _lockTimeoutMs;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Experiments")
            ?? throw new InvalidOperationException("Connection string 'Experiments' is not configured.");

        var configured = configuration[LockTimeoutKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            _lockTimeoutMs = DefaultLockTimeoutMs;
        }
        else if (!int.TryParse(configured, out _lockTimeoutMs) || _lockTimeoutMs < 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{LockTimeoutKey}' must be a non-negative number of milliseconds (was '{configured}').");
        }
    }

    public async Task<IDbConnection> CreateOpenConnectionAsync()
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SET LOCK_TIMEOUT {_lockTimeoutMs};";
            await command.ExecuteNonQueryAsync();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
