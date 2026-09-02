namespace Wesa.Harness.Contracts.Workspaces;

public sealed record RegisterWorkspaceRequest(string Name, string RootPath);

public sealed record WorkspaceDto(Guid Id, string Name, string RootPath, DateTimeOffset CreatedAt);

