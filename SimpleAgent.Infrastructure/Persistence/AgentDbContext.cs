using Microsoft.EntityFrameworkCore;
using Pokok.BuildingBlocks.Common;
using Pokok.BuildingBlocks.Persistence.Context;
using Pokok.BuildingBlocks.Persistence.Extensions;
using SimpleAgent.Domain.Agents;

namespace SimpleAgent.Infrastructure.Persistence;

public sealed class AgentDbContext : DbContextBase
{
    public AgentDbContext(DbContextOptions<AgentDbContext> options, ICurrentUserService currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();

    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyGlobalConfigurations();

        modelBuilder.Entity<AgentRun>(builder =>
        {
            builder.ToTable("AgentRuns");
            builder.HasKey(run => run.Id);
            builder.Property(run => run.CorrelationId).HasMaxLength(100).IsRequired();
            builder.HasIndex(run => run.CorrelationId).IsUnique();
            builder.Property(run => run.Goal).IsRequired();
            builder.Property(run => run.ReplayOfRunId);
            builder.HasIndex(run => run.ReplayOfRunId);
            builder.Ignore(run => run.IsReplay);
            builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.Property(run => run.FinalAction).HasMaxLength(100);
            builder.Property(run => run.DurationMs);
            builder.HasMany(run => run.Steps)
                .WithOne()
                .HasForeignKey(step => step.AgentRunId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Navigation(run => run.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<AgentStep>(builder =>
        {
            builder.ToTable("AgentSteps");
            builder.HasKey(step => step.Id);
            builder.Property(step => step.Action).HasMaxLength(100).IsRequired();
            builder.Property(step => step.StepNumber).IsRequired();
            builder.Property(step => step.DurationMs).IsRequired();
        });
    }
}
