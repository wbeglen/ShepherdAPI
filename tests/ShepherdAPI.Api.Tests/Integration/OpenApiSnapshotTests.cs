using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>
/// contracts/openapi.json is the committed contract clients generate from. This fails when the API
/// changes without the snapshot being regenerated. To regenerate after an intentional change:
/// set UPDATE_OPENAPI=1 and run this test once.
/// </summary>
public class OpenApiSnapshotTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task CommittedSnapshot_MatchesTheGeneratedContract()
    {
        using var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();
        var generated = await client.GetStringAsync("/swagger/v1/swagger.json");
        var path = RepoPaths.OpenApiSnapshot;

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            File.WriteAllText(path, generated);
            return;
        }

        var committed = JsonNode.Parse(File.ReadAllText(path));
        Assert.True(JsonNode.DeepEquals(committed, JsonNode.Parse(generated)),
            "contracts/openapi.json is out of date. Regenerate it with: UPDATE_OPENAPI=1 dotnet test --filter OpenApiSnapshotTests");
    }
}
