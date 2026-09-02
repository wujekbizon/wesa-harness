using System.Security.Cryptography;
using System.Text.Json;
using MediatR;
using Wesa.Harness.Api.Infrastructure.Data;

namespace Wesa.Harness.Api.Behaviors;

public sealed class CommandAuditBehavior<TRequest, TResponse>(
    HarnessDbContext db,
    IHttpContextAccessor httpContextAccessor)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IAuditedCommand)
        {
            return await next(cancellationToken);
        }

        var context = httpContextAccessor.HttpContext;
        var actorId = context?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? "internal";
        var correlationId = context?.TraceIdentifier ?? Guid.NewGuid().ToString("N");
        var requestHash = Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        try
        {
            var response = await next(cancellationToken);
            await WriteAudit(actorId, correlationId, requestHash, true, null, cancellationToken);
            return response;
        }
        catch (Exception exception)
        {
            await WriteAudit(
                actorId,
                correlationId,
                requestHash,
                false,
                exception.GetType().Name,
                cancellationToken);
            throw;
        }
    }

    private async Task WriteAudit(
        string actorId,
        string correlationId,
        string requestHash,
        bool succeeded,
        string? errorType,
        CancellationToken cancellationToken)
    {
        db.ControlAudit.Add(new ControlAuditRecord
        {
            Id = Guid.NewGuid(),
            ActorId = actorId,
            CommandType = typeof(TRequest).Name,
            RequestHash = requestHash,
            CorrelationId = correlationId,
            Succeeded = succeeded,
            ErrorType = errorType,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
