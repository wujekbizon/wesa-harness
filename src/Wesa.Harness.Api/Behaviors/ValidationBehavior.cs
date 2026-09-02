using FluentValidation;
using MediatR;

namespace Wesa.Harness.Api.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var validationContext = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(validationContext, cancellationToken)));
        var failures = results.SelectMany(result => result.Errors).Where(error => error is not null).ToArray();
        if (failures.Length > 0)
        {
            throw new ValidationException(failures);
        }

        return await next(cancellationToken);
    }
}

