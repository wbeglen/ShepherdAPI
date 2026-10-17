using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ShepherdAPI.Api.Infrastructure;

/// <summary>
/// Single place that translates domain exceptions and SQL Server errors into RFC 7807
/// ProblemDetails responses, so controllers stay free of try/catch noise. Every response carries a
/// stable machine-readable <c>code</c> from <see cref="ErrorCatalog"/>.
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (NotFoundException ex)
        {
            await WriteProblem(context, ErrorCatalog.NotFound, ex.Message);
        }
        catch (RecordLockedException ex)
        {
            await WriteProblem(context, ErrorCatalog.RecordLocked, ex.Message);
        }
        catch (ConflictException ex)
        {
            await WriteProblem(context, ErrorCatalog.Conflict, ex.Message);
        }
        catch (CatalogException ex)
        {
            await WriteProblem(context, ex.Entry, ex.Message);
        }
        catch (ValidationAppException ex)
        {
            var problem = new ValidationProblemDetails(ex.Errors)
            {
                Status = (int)ErrorCatalog.ValidationFailed.Status,
                Title = ErrorCatalog.ValidationFailed.Title,
            };
            problem.Extensions["code"] = ErrorCatalog.ValidationFailed.Code;
            context.Response.StatusCode = problem.Status.Value;
            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (SqlException ex) when (SqlErrorMapper.TryMap(ex.Number, ex.Message, out var mapped))
        {
            await WriteProblem(context, mapped.Entry, mapped.Detail);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path} (TraceId: {TraceId})",
                context.Request.Method, context.Request.Path, context.TraceIdentifier);
            await WriteProblem(context, ErrorCatalog.InternalError, "An unexpected error occurred.", context.TraceIdentifier);
        }
    }

    private static async Task WriteProblem(HttpContext context, ErrorEntry entry, string detail, string? traceId = null)
    {
        var problem = new ProblemDetails
        {
            Status = (int)entry.Status,
            Title = entry.Title,
            Detail = detail,
        };
        problem.Extensions["code"] = entry.Code;
        if (entry.Retryable)
        {
            problem.Extensions["retryable"] = true;
        }
        if (traceId is not null)
        {
            problem.Extensions["traceId"] = traceId;
        }

        context.Response.StatusCode = (int)entry.Status;
        await context.Response.WriteAsJsonAsync(problem);
    }
}
