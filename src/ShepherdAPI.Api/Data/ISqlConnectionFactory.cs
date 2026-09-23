using System.Data;

namespace ShepherdAPI.Api.Data;

/// <summary>
/// Creates connections to the Experiments database. Kept as a seam so integration tests can
/// substitute a connection wrapped in a transaction that is always rolled back, without touching
/// the real data.
/// </summary>
public interface ISqlConnectionFactory
{
    Task<IDbConnection> CreateOpenConnectionAsync();
}
