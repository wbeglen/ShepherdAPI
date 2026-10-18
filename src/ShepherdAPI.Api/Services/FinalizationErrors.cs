using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using ShepherdAPI.Api.Infrastructure;

namespace ShepherdAPI.Api.Services;

/// <summary>
/// Turns the finalization procedure's failed-check errors (SQL 50501-50507, plus 50500) into a
/// <see cref="CatalogException"/> whose body names the offending Executions, phases or observations.
/// The procedure ends each such message with a machine-readable suffix, for example
/// <c> [ExecutionIDs=12,15;Total=2]</c>; each member becomes a ProblemDetails extension
/// (<c>executionIDs</c>: [12, 15], <c>total</c>: 2). The suffix is dropped from the human-readable detail.
/// </summary>
public static partial class FinalizationErrors
{
    public static bool TryTranslate(SqlException ex, out CatalogException translated)
    {
        translated = null!;

        var entry = ErrorCatalog.FindBySqlNumber(ex.Number);
        if (entry is null || ex.Number < ErrorCatalog.FinalizationRangeStart || ex.Number >= ErrorCatalog.FinalizationRangeStart + ErrorCatalog.RangeSize
            || ex.Number == ErrorCatalog.SnapshotInsertNotPermitted.SqlNumber)
        {
            return false;
        }

        var (detail, extensions) = Parse(ex.Message);
        translated = new CatalogException(entry, detail, extensions);
        return true;
    }

    public static (string Detail, IReadOnlyDictionary<string, object?> Extensions) Parse(string message)
    {
        var extensions = new Dictionary<string, object?>();
        var match = SuffixPattern().Match(message);
        if (!match.Success)
        {
            return (message, extensions);
        }

        foreach (var member in match.Groups["body"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = member.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var key = char.ToLowerInvariant(parts[0][0]) + parts[0][1..];
            var numbers = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToArray();
            extensions[key] = key == "total" ? numbers.Single() : numbers;
        }

        return (message[..match.Index].TrimEnd(), extensions);
    }

    [GeneratedRegex(@"\s*\[(?<body>[A-Za-z]+=[0-9,]*(?:;[A-Za-z]+=[0-9,]*)*)\]\s*$")]
    private static partial Regex SuffixPattern();
}
