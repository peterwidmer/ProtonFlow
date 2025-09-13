using Microsoft.EntityFrameworkCore;
using ProtonFlow.Persistence.EfCore.Storage.Models;

namespace ProtonFlow.Persistence.EfCore.Storage;

/// <summary>
/// EF Core DbContext encapsulating ProtonFlow persistence model.
/// Configures table names, constraints, indexes and relationships tailored for runtime queries and KPI aggregation.
/// </summary>
public class ProtonFlowDbContext : DbContext
{
    /// <summary>Process definition versions table.</summary>
    public DbSet<StoredProcessDefinition> ProcessDefinitions => Set<StoredProcessDefinition>();
    /// <summary>Process instances table.</summary>
    public DbSet<StoredProcessInstance> ProcessInstances => Set<StoredProcessInstance>();
    /// <summary>Per-element execution history.</summary>
    public DbSet<StepExecutionRecord> StepExecutions => Set<StepExecutionRecord>();
    /// <summary>Instance attached free-form notes.</summary>
    public DbSet<InstanceNote> InstanceNotes => Set<InstanceNote>();
    /// <summary>Background jobs queue.</summary>
    public DbSet<Job> Jobs => Set<Job>();

    /// <summary>Create a new DbContext with configured options (provider, connection etc.).</summary>
    public ProtonFlowDbContext(DbContextOptions<ProtonFlowDbContext> options) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder b)
    {
        // Process Definitions
        b.Entity<StoredProcessDefinition>(e =>
        {
            e.ToTable("ProcessDefinitions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).IsRequired().HasMaxLength(180);
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
            e.HasIndex(x => x.Key);
            e.HasIndex(x => new { x.Key, x.Version }).IsUnique();
            e.HasIndex(x => new { x.Key, x.IsLatest });
        });

        // Process Instances
        b.Entity<StoredProcessInstance>(e =>
        {
            e.ToTable("ProcessInstances");
            e.HasKey(x => x.Id);
            e.Property(x => x.ProcessKey).IsRequired().HasMaxLength(180);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.VariablesJson).IsRequired();
            e.Property(x => x.ActiveTokensJson).IsRequired();
            e.Property(x => x.ParallelJoinWaitsJson).IsRequired();
            // Use plain concurrency token so SQLite does not expect a DB-generated value
            e.Property(x => x.RowVersion).IsConcurrencyToken();
            e.HasIndex(x => new { x.ProcessDefinitionId, x.Status });
            e.HasIndex(x => new { x.ProcessKey, x.Status, x.StartedUtc });
            e.HasIndex(x => x.BusinessCorrelationId);
            e.HasOne(x => x.Definition)
             .WithMany(d => d.Instances)
             .HasForeignKey(x => x.ProcessDefinitionId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // Step Executions
        b.Entity<StepExecutionRecord>(e =>
        {
            e.ToTable("StepExecutions");
            e.HasKey(x => x.Id);
            e.Property(x => x.ElementId).IsRequired().HasMaxLength(128);
            e.Property(x => x.ElementType).IsRequired().HasMaxLength(64);
            e.Property(x => x.StepStatus).IsRequired().HasMaxLength(32);
            e.HasIndex(x => new { x.InstanceId, x.Sequence }).IsUnique();
            e.HasIndex(x => new { x.ProcessDefinitionId, x.ElementId, x.StartUtc });
            e.HasIndex(x => new { x.ProcessKey, x.ElementId, x.StartUtc });
            e.HasOne(x => x.Instance)
             .WithMany(i => i.StepExecutions)
             .HasForeignKey(x => x.InstanceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Instance Notes
        b.Entity<InstanceNote>(e =>
        {
            e.ToTable("InstanceNotes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).IsRequired().HasMaxLength(32);
            e.Property(x => x.Message).IsRequired();
            e.HasIndex(x => x.InstanceId);
            e.HasOne(x => x.Instance)
             .WithMany(i => i.Notes)
             .HasForeignKey(x => x.InstanceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Jobs
        b.Entity<Job>(entity =>
        {
            entity.ToTable("Jobs");
            entity.HasKey(j => j.Id);

            entity.Property(j => j.Type)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(j => j.Attempt)
                .IsRequired();

            // Concurrency token only; value is generated client-side when updating
            entity.Property(j => j.RowVersion)
                .IsConcurrencyToken();

            entity.Property(j => j.ProcessInstanceId)
                .IsRequired()
                .HasMaxLength(64);

            entity.HasIndex(j => new { j.RunAt, j.LockedUntil });
        });
    }
}
