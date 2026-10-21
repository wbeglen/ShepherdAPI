using System.Data;
using Dapper;
using ShepherdAPI.Api.Data;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>
/// Sets ExperimentVersion.LockedUTC from NULL by raw SQL for test setup (Issue #9). dbo.trg_ExperimentVersion_Guard admits
/// that transition only while the finalization session-context key names the same version, so the helper sets the key for
/// that one statement and always clears it afterwards. The key is session state: sites that run inside an explicit
/// transaction on a specific connection pass that connection and transaction, so the key is set on the right session.
/// </summary>
public static class LockedUtcTestHelper
{
    public const string TokenKey = "ShepherdAPI.FinalizingVersionID";

    /// <summary>Locks the version on its own connection (setup that is not part of a specific session or transaction).</summary>
    public static async Task LockAsync(ISqlConnectionFactory factory, long versionId)
    {
        using var connection = await factory.CreateOpenConnectionAsync();
        await LockAsync(connection, versionId);
    }

    /// <summary>Locks the version on the given connection, inside the given transaction when there is one.</summary>
    public static async Task LockAsync(IDbConnection connection, long versionId, IDbTransaction? transaction = null)
    {
        await SetTokenAsync(connection, versionId, transaction);
        try
        {
            await connection.ExecuteAsync(
                "UPDATE dbo.ExperimentVersion SET LockedUTC = SYSUTCDATETIME() WHERE ExperimentVersionID = @versionId;",
                new { versionId }, transaction);
        }
        finally
        {
            await SetTokenAsync(connection, null, transaction);
        }
    }

    /// <summary>Sets or (with null) clears the finalization key on the connection.</summary>
    public static Task SetTokenAsync(IDbConnection connection, long? versionId, IDbTransaction? transaction = null) =>
        connection.ExecuteAsync(
            "EXEC sys.sp_set_session_context @key = @key, @value = @value;",
            new { key = TokenKey, value = versionId }, transaction);
}
