using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>Applies .sql files (with GO batch separators) in filename order, the way database/README.md describes.</summary>
public static class SqlScriptRunner
{
    public static List<string> SplitBatches(string sql) =>
        Regex.Split(sql, @"(?im)^\s*GO\s*$")
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .ToList();

    public static async Task ApplyBatchesAsync(SqlConnection connection, IEnumerable<string> batches)
    {
        foreach (var batch in batches)
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    public static async Task ApplyFolderAsync(string connectionString, string folder)
    {
        var files = Directory.GetFiles(folder, "*.sql")
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var file in files)
        {
            await ApplyBatchesAsync(connection, SplitBatches(File.ReadAllText(file)));
        }
    }
}
