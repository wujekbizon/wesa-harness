using Mapster;
using Wesa.Harness.Api.Infrastructure.Data;
using Wesa.Harness.Contracts.Tasks;

namespace Wesa.Harness.Api.Features.Tasks.Mappings;

public sealed class TaskMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
        => config.NewConfig<TaskRecord, TaskDto>();
}

