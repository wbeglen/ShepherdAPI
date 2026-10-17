using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ShepherdAPI.Api.Infrastructure;
using Xunit;

namespace ShepherdAPI.Api.Tests.Infrastructure;

public class ExceptionHandlingMiddlewareTests
{
    internal static async Task<(int Status, JsonElement Body)> RunAsync(Exception toThrow)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(_ => throw toThrow, NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, doc.RootElement.Clone());
    }

    [Fact]
    public async Task NotFound_KeepsItsStatusAndTitle_AndGainsACode()
    {
        var (status, body) = await RunAsync(new NotFoundException("Agent 9 was not found."));

        Assert.Equal(404, status);
        Assert.Equal("Not Found", body.GetProperty("title").GetString());
        Assert.Equal("Agent 9 was not found.", body.GetProperty("detail").GetString());
        Assert.Equal("not-found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RecordLocked_KeepsItsStatusAndTitle_AndGainsACode()
    {
        var (status, body) = await RunAsync(new RecordLockedException("locked"));

        Assert.Equal(409, status);
        Assert.Equal("Record Locked", body.GetProperty("title").GetString());
        Assert.Equal("record-locked", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Conflict_KeepsItsStatusAndTitle_AndGainsACode()
    {
        var (status, body) = await RunAsync(new ConflictException("conflict"));

        Assert.Equal(409, status);
        Assert.Equal("Conflict", body.GetProperty("title").GetString());
        Assert.Equal("conflict", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Validation_KeepsFieldErrors_AndGainsACode()
    {
        var (status, body) = await RunAsync(new ValidationAppException("Name", "Name is required."));

        Assert.Equal(400, status);
        Assert.Equal("Validation Failed", body.GetProperty("title").GetString());
        Assert.Equal("validation-failed", body.GetProperty("code").GetString());
        Assert.Equal("Name is required.", body.GetProperty("errors").GetProperty("Name")[0].GetString());
    }

    [Fact]
    public async Task CatalogException_UsesItsEntry()
    {
        var (status, body) = await RunAsync(new CatalogException(ErrorCatalog.DesignFrozen, "Executions exist."));

        Assert.Equal(409, status);
        Assert.Equal("design-frozen", body.GetProperty("code").GetString());
        Assert.Equal("Executions exist.", body.GetProperty("detail").GetString());
        Assert.False(body.TryGetProperty("retryable", out _));
    }

    [Fact]
    public async Task CatalogException_WritesItsExtensionMembers_NextToTheCode()
    {
        var extensions = new Dictionary<string, object?>
        {
            ["actualWeightPercent"] = 90m,
            ["scoreRules"] = new[] { new { scoreRuleID = 4 } },
        };

        var (status, body) = await RunAsync(new CatalogException(ErrorCatalog.InvalidRuleConfiguration, "bad total", extensions));

        Assert.Equal(409, status);
        Assert.Equal("invalid-rule-configuration", body.GetProperty("code").GetString());
        Assert.Equal(90m, body.GetProperty("actualWeightPercent").GetDecimal());
        Assert.Equal(4, body.GetProperty("scoreRules")[0].GetProperty("scoreRuleID").GetInt32());
    }

    [Fact]
    public async Task RetryableEntry_IsFlaggedRetryable()
    {
        var (status, body) = await RunAsync(new CatalogException(ErrorCatalog.RetryableConflict, "retry"));

        Assert.Equal(409, status);
        Assert.Equal("retryable-conflict", body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task UnhandledException_Is500_WithTraceIdAndCode_AndNoLeakedMessage()
    {
        var (status, body) = await RunAsync(new InvalidOperationException("secret internal detail"));

        Assert.Equal(500, status);
        Assert.Equal("internal-error", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
        Assert.DoesNotContain("secret", body.GetProperty("detail").GetString());
    }
}
