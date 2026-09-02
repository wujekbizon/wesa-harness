using FluentAssertions;
using Wesa.Harness.Api.Features.Pipelines.Definitions;

namespace Wesa.Harness.Tests.Contracts;

public sealed class WorkflowDefinitionTests
{
    [Fact]
    public void Catalog_loads_versioned_generic_definition()
    {
        var catalog = new WorkflowDefinitionCatalog(Path.Combine(FindHarnessRoot(), "config", "workflows"));

        var definition = catalog.Get("safe-text-review-v1");

        definition.Version.Should().Be(1);
        definition.Stages.Select(value => value.Id).Should().Equal("draft", "review");
        definition.Stages[1].DependsOn.Should().Equal("draft");
        definition.Stages[1].AttachmentStages.Should().Equal("draft");

        var sandbox = catalog.Get("python-compile-v1");
        sandbox.Stages[1].Kind.Should().Be("sandbox");
        sandbox.Stages[1].SandboxProfile.Should().Be("python-compile");
        sandbox.Stages[1].SourceStage.Should().Be("generate");
    }

    [Fact]
    public void Catalog_rejects_dependency_cycles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wesa-workflow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "cycle.workflow.json"),
                """
                {
                  "id":"cycle","version":1,"name":"Cycle","description":"invalid",
                  "stages":[
                    {"id":"a","name":"A","kind":"task","dependsOn":["b"],"agentId":"W1","taskType":"write","providerId":null,"model":null,"promptTemplate":"x","outputFile":"a.md","attachmentStages":[],"maxAttempts":1,"repeatCount":1,"condition":"on-success"},
                    {"id":"b","name":"B","kind":"task","dependsOn":["a"],"agentId":"W1","taskType":"write","providerId":null,"model":null,"promptTemplate":"x","outputFile":"b.md","attachmentStages":[],"maxAttempts":1,"repeatCount":1,"condition":"on-success"}
                  ]
                }
                """);

            Action action = () => _ = new WorkflowDefinitionCatalog(directory);

            action.Should().Throw<InvalidOperationException>().WithMessage("*cycle*");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FindHarnessRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Wesa.Harness.slnx")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new DirectoryNotFoundException("Wesa.Harness.slnx not found");
    }
}
