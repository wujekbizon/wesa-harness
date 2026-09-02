using Microsoft.EntityFrameworkCore;

namespace Wesa.Harness.Api.Infrastructure.Data;

public sealed class HarnessDbContext(DbContextOptions<HarnessDbContext> options) : DbContext(options)
{
    public DbSet<WorkspaceRecord> Workspaces => Set<WorkspaceRecord>();
    public DbSet<SessionRecord> Sessions => Set<SessionRecord>();
    public DbSet<TaskRecord> Tasks => Set<TaskRecord>();
    public DbSet<TrajectoryEventRecord> TrajectoryEvents => Set<TrajectoryEventRecord>();
    public DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();
    public DbSet<ApprovalRecord> Approvals => Set<ApprovalRecord>();
    public DbSet<ArtifactRecord> Artifacts => Set<ArtifactRecord>();
    public DbSet<ControlAuditRecord> ControlAudit => Set<ControlAuditRecord>();
    public DbSet<PipelineRunRecord> PipelineRuns => Set<PipelineRunRecord>();
    public DbSet<PipelineStageRecord> PipelineStages => Set<PipelineStageRecord>();
    public DbSet<PipelineScheduleRecord> PipelineSchedules => Set<PipelineScheduleRecord>();
    public DbSet<SandboxExecutionRecord> SandboxExecutions => Set<SandboxExecutionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("harness");
        modelBuilder.Entity<WorkspaceRecord>(entity =>
        {
            entity.ToTable("workspaces");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.RootPath).IsUnique();
            entity.Property(value => value.Name).HasMaxLength(200);
            entity.Property(value => value.RootPath).HasMaxLength(2048);
        });
        modelBuilder.Entity<SessionRecord>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(value => value.Id);
            entity.HasOne(value => value.Workspace).WithMany().HasForeignKey(value => value.WorkspaceId);
            entity.Property(value => value.Status).HasMaxLength(50);
        });
        modelBuilder.Entity<TaskRecord>(entity =>
        {
            entity.ToTable("tasks");
            entity.HasKey(value => value.Id);
            entity.HasOne(value => value.Session).WithMany().HasForeignKey(value => value.SessionId);
            entity.HasIndex(value => new { value.WorkspaceId, value.CreatedAt });
            entity.Property(value => value.TaskType).HasMaxLength(100);
            entity.Property(value => value.AgentId).HasMaxLength(100);
            entity.Property(value => value.Issuer).HasMaxLength(100);
            entity.Property(value => value.Provider).HasMaxLength(100);
            entity.Property(value => value.Model).HasMaxLength(300);
            entity.Property(value => value.Status).HasMaxLength(50);
            entity.Property(value => value.GateOutcome).HasMaxLength(50);
            entity.Property(value => value.AttachmentsJson).HasColumnType("jsonb");
        });
        modelBuilder.Entity<TrajectoryEventRecord>(entity =>
        {
            entity.ToTable("trajectory_events");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.SessionId, value.Sequence }).IsUnique();
            entity.Property(value => value.EventType).HasMaxLength(160);
            entity.Property(value => value.PayloadJson).HasColumnType("jsonb");
        });
        modelBuilder.Entity<OutboxMessageRecord>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.ProcessedAt, value.OccurredAt });
            entity.Property(value => value.EventType).HasMaxLength(160);
            entity.Property(value => value.PayloadJson).HasColumnType("jsonb");
        });
        modelBuilder.Entity<ApprovalRecord>(entity =>
        {
            entity.ToTable("approvals");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Action).HasMaxLength(200);
            entity.Property(value => value.Status).HasMaxLength(50);
            entity.HasIndex(value => value.PipelineRunId);
        });
        modelBuilder.Entity<ArtifactRecord>(entity =>
        {
            entity.ToTable("artifacts");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.Sha256);
            entity.Property(value => value.MediaType).HasMaxLength(200);
            entity.Property(value => value.Sha256).HasMaxLength(64);
            entity.Property(value => value.GateStatus).HasMaxLength(50);
            entity.Property(value => value.StoragePath).HasMaxLength(4096);
        });
        modelBuilder.Entity<ControlAuditRecord>(entity =>
        {
            entity.ToTable("control_audit");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.OccurredAt);
            entity.Property(value => value.ActorId).HasMaxLength(100);
            entity.Property(value => value.CommandType).HasMaxLength(200);
            entity.Property(value => value.RequestHash).HasMaxLength(64);
            entity.Property(value => value.CorrelationId).HasMaxLength(100);
            entity.Property(value => value.ErrorType).HasMaxLength(200);
        });
        modelBuilder.Entity<PipelineRunRecord>(entity =>
        {
            entity.ToTable("pipeline_runs");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.WorkspaceId, value.CreatedAt });
            entity.Property(value => value.Definition).HasMaxLength(200);
            entity.Property(value => value.Provider).HasMaxLength(100);
            entity.Property(value => value.Model).HasMaxLength(300);
            entity.Property(value => value.Status).HasMaxLength(50);
        });
        modelBuilder.Entity<PipelineStageRecord>(entity =>
        {
            entity.ToTable("pipeline_stages");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.PipelineRunId, value.Sequence }).IsUnique();
            entity.HasOne(value => value.PipelineRun)
                .WithMany(value => value.Stages)
                .HasForeignKey(value => value.PipelineRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(value => value.Name).HasMaxLength(200);
            entity.Property(value => value.StageKey).HasMaxLength(100);
            entity.Property(value => value.Kind).HasMaxLength(50);
            entity.Property(value => value.AgentId).HasMaxLength(100);
            entity.Property(value => value.TaskType).HasMaxLength(100);
            entity.Property(value => value.Provider).HasMaxLength(100);
            entity.Property(value => value.Model).HasMaxLength(300);
            entity.Property(value => value.OutputPath).HasMaxLength(2048);
            entity.Property(value => value.DependsOnJson).HasColumnType("jsonb");
            entity.Property(value => value.AttachmentStagesJson).HasColumnType("jsonb");
            entity.Property(value => value.Condition).HasMaxLength(50);
            entity.Property(value => value.Status).HasMaxLength(50);
            entity.Property(value => value.SandboxProfile).HasMaxLength(100);
            entity.Property(value => value.SourceStageKey).HasMaxLength(100);
            entity.Property(value => value.ExtensionPluginId).HasMaxLength(100);
            entity.Property(value => value.ExtensionOperation).HasMaxLength(100);
            entity.Property(value => value.ExtensionInputsJson).HasColumnType("jsonb");
            entity.Property(value => value.ResultJson).HasColumnType("jsonb");
            entity.Property(value => value.ResultSha256).HasMaxLength(64);
            entity.Property(value => value.ResultStoragePath).HasMaxLength(4096);
        });
        modelBuilder.Entity<PipelineScheduleRecord>(entity =>
        {
            entity.ToTable("pipeline_schedules");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.Enabled, value.NextRunAt });
            entity.Property(value => value.Definition).HasMaxLength(200);
            entity.Property(value => value.Provider).HasMaxLength(100);
            entity.Property(value => value.Model).HasMaxLength(300);
        });
        modelBuilder.Entity<SandboxExecutionRecord>(entity =>
        {
            entity.ToTable("sandbox_executions");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.SourceTaskId, value.CreatedAt });
            entity.Property(value => value.Profile).HasMaxLength(100);
            entity.Property(value => value.Status).HasMaxLength(50);
            entity.Property(value => value.OutputPath).HasMaxLength(2048);
            entity.Property(value => value.GateOutcome).HasMaxLength(50);
        });
    }
}
