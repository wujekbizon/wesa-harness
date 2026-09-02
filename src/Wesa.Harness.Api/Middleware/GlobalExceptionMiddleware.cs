using FluentValidation;
using Microsoft.EntityFrameworkCore;
using System.Security;

namespace Wesa.Harness.Api.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException exception)
        {
            await WriteProblem(
                context,
                StatusCodes.Status400BadRequest,
                "Validation failed",
                exception.Errors.Select(error => new { field = error.PropertyName, error = error.ErrorMessage }));
        }
        catch (KeyNotFoundException exception)
        {
            await WriteProblem(context, StatusCodes.Status404NotFound, exception.Message);
        }
        catch (SecurityException exception)
        {
            logger.LogWarning(exception, "Secure Engine denied a request. CorrelationId={CorrelationId}", context.TraceIdentifier);
            await WriteProblem(context, StatusCodes.Status403Forbidden, "Secure Engine denied the request");
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "Database constraint failure. CorrelationId={CorrelationId}", context.TraceIdentifier);
            await WriteProblem(context, StatusCodes.Status409Conflict, "The requested state conflicts with stored data");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled request failure. CorrelationId={CorrelationId}", context.TraceIdentifier);
            await WriteProblem(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred");
        }
    }

    private static Task WriteProblem(HttpContext context, int status, string title, object? errors = null)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(
            new { title, status, correlationId = context.TraceIdentifier, errors });
    }
}
