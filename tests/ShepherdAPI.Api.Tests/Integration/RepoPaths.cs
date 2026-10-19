namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>Locates repository files by walking up from the test output directory.</summary>
public static class RepoPaths
{
    public const string BaselineDdlFileName = "Create_Experiments_Database_And_Tables-9.sql";

    public static string BaselineDdl => FindFile("database", BaselineDdlFileName);
    public static string OpenApiSnapshot => FindFile("contracts", "openapi.json");

    /// <summary>The upgrade/ and programmability/ folders, in the order they are applied. A folder that does not exist is skipped.</summary>
    public static IEnumerable<string> DeploymentFolders()
    {
        var databaseDir = Path.GetDirectoryName(BaselineDdl)!;
        foreach (var name in new[] { "upgrade", "programmability" })
        {
            var folder = Path.Combine(databaseDir, name);
            if (Directory.Exists(folder))
            {
                yield return folder;
            }
        }
    }

    public static string FindFile(params string[] relativeSegments)
    {
        var relative = Path.Combine(relativeSegments);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate '{relative}' above the test output directory.");
    }
}
