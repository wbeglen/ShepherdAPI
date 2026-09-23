using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ShepherdAPI.Api.Infrastructure;

/// <summary>
/// Single place that translates domain exceptions (and a few known SQL Server error numbers)
/// into RFC 7807 ProblemDetails responses, so controllers stay free of try/catch noise.
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    // SQL Server error numbers for constraint violations we translate into client-meaningful responses.
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;
    private const int ForeignKeyViolation = 547;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (NotFoundException ex)
        {
            await WriteProblem(context, HttpStatusCode.NotFound, "Not Found", ex.Message);
        }
        catch (RecordLockedException ex)
        {
            await WriteProblem(context, HttpStatusCode.Conflict, "Record Locked", ex.Message);
        }
        catch (ConflictException ex)
        {
            await WriteProblem(context, HttpStatusCode.Conflict, "Conflict", ex.Message);
        }
        catch (ValidationAppException ex)
        {
            var problem = new ValidationProblemDetails(ex.Errors)
            {
                Status = (int)HttpStatusCode.BadRequest,
                Title = "Validation Failed",
            };
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (SqlException ex) when (ex.Number is UniqueConstraintViolation or UniqueIndexViolation)
        {
            await WriteProblem(context, HttpStatusCode.Conflict, "Conflict", "A record with the same unique key already exists.");
        }
        catch (SqlException ex) when (ex.Number == ForeignKeyViolation)
        {
            await WriteProblem(context, HttpStatusCode.Conflict, "Conflict", "The request references or is referenced by a related record that prevents this operation.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path} (TraceId: {TraceId})",
                context.Request.Method, context.Request.Path, context.TraceIdentifier);
            await WriteProblem(context, HttpStatusCode.InternalServerError, "Internal Server Error",
                "An unexpected error occurred.", context.TraceIdentifier);
        }
    }

    private static async Task WriteProblem(HttpContext context, HttpStatusCode status, string title, string detail, string? traceId = null)
    {
        var problem = new ProblemDetails
        {
            Status = (int)status,
            Title = title,
            Detail = detail,
        };
        if (traceId is not null)
        {
            problem.Extensions["traceId"] = traceId;
        }

        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsJsonAsync(problem);
    }
}
