using System.Data;
using Microsoft.Data.SqlClient;

namespace ShepherdAPI.Api.Data;

public class SqlConnectionFactory(IConfiguration configuration) : ISqlConnectionFactory
{
    private readonly string _connectionString = configuration.GetConnectionString("Experiments")
        ?? throw new InvalidOperationException("Connection string 'Experiments' is not configured.");

    public async Task<IDbConnection> CreateOpenConnectionAsync()
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
