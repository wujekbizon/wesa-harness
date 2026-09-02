using FluentValidation;
using Wesa.Harness.Api.Features.Plugins.Messages;

namespace Wesa.Harness.Api.Features.Plugins.Validators;

public sealed class SetPluginEnabledValidator : AbstractValidator<SetPluginEnabledCommand>
{
    public SetPluginEnabledValidator()
    {
        RuleFor(value => value.PluginId).NotEmpty().MaximumLength(100).Matches("^[a-z][a-z0-9-]*$");
    }
}
