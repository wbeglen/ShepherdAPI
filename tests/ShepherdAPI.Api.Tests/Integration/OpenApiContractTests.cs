using System.Text.Json.Nodes;
using Xunit;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>
/// The response declarations Issue #8 added to the run-facts, artifact and evidence operations, asserted on the committed
/// contract (which OpenApiSnapshotTests keeps equal to the generated one). They are the real status codes the API returns
/// plus the 404 and 409 ProblemDetails that the controlled-write procedures produce.
/// </summary>
public class OpenApiContractTests
{
    private static JsonObject Responses(string path, string method)
    {
        var contract = JsonNode.Parse(File.ReadAllText(RepoPaths.OpenApiSnapshot))!;
        return contract["paths"]![path]![method]!["responses"]!.AsObject();
    }

    private static void AssertCodes(string path, string method, params string[] expected)
    {
        var responses = Responses(path, method);
        Assert.Equal(expected.OrderBy(c => c), responses.Select(r => r.Key).OrderBy(c => c));

        foreach (var code in new[] { "404", "409" }.Where(expected.Contains))
        {
            Assert.EndsWith("/ProblemDetails", responses[code]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>());
        }
    }

    [Fact]
    public void RunFactsUpdate_Declares200_400_404_409() =>
        AssertCodes("/api/executions/{id}/run-facts", "put", "200", "400", "404", "409");

    [Fact]
    public void ArtifactCreation_Declares201_404_409() =>
        AssertCodes("/api/artifacts", "post", "201", "404", "409");

    [Fact]
    public void EvidenceAddition_Declares201_400_404_409() =>
        AssertCodes("/api/score-observations/{id}/evidence", "post", "201", "400", "404", "409");

    [Fact]
    public void EvidenceRemoval_Declares204_404_409() =>
        AssertCodes("/api/score-observations/{id}/evidence/{artifactId}", "delete", "204", "404", "409");
}
