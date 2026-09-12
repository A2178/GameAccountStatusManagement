using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Workspace.Infrastructure.Persistence.Migrations;

[DbContext(typeof(WorkspaceDbContext))]
partial class WorkspaceDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
        => M1Model.Build(modelBuilder);
}

internal static class M1Model
{
    public static void Build(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.10")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
        modelBuilder.Entity("Workspace.Domain.GameAccount", b => { b.Property<Guid>("Id").HasColumnType("uuid"); b.Property<long>("CoordinationVersion").HasColumnType("bigint"); b.Property<string>("DisplayName").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)"); b.HasKey("Id"); b.ToTable("preview_accounts"); });
        modelBuilder.Entity("Workspace.Domain.CharacterCard", b => { b.Property<Guid>("Id").HasColumnType("uuid"); b.Property<Guid>("AccountId").HasColumnType("uuid"); b.Property<string>("DisplayName").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)"); b.Property<Guid?>("PrimaryOperatorId").HasColumnType("uuid"); b.Property<string>("UsageStatus").IsRequired().HasColumnType("text"); b.HasKey("Id"); b.HasIndex("AccountId"); b.ToTable("character_cards"); });
        modelBuilder.Entity("Workspace.Domain.FieldRegion", b => { b.Property<Guid>("Id").HasColumnType("uuid"); b.Property<string>("DisplayName").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)"); b.HasKey("Id"); b.ToTable("field_regions"); });
        modelBuilder.Entity("Workspace.Domain.ResourceReservation", b => { b.Property<Guid>("Id").HasColumnType("uuid"); b.Property<Guid>("AccountId").HasColumnType("uuid"); b.Property<Guid>("CardId").HasColumnType("uuid"); b.Property<Guid>("CreatedByParticipantId").HasColumnType("uuid"); b.Property<Guid>("RegionId").HasColumnType("uuid"); b.Property<string>("State").IsRequired().HasColumnType("text"); b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone"); b.Property<long>("Version").HasColumnType("bigint"); b.HasKey("Id"); b.HasIndex("AccountId", "State"); b.HasIndex("CardId").IsUnique().HasFilter("\"State\" <> 'Released'"); b.HasIndex("RegionId"); b.ToTable("resource_reservations"); });
        modelBuilder.Entity("Workspace.Infrastructure.Persistence.ParticipantSession", b => { b.Property<Guid>("Id").HasColumnType("uuid"); b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone"); b.Property<string>("Nickname").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)"); b.Property<Guid>("SessionToken").HasColumnType("uuid"); b.HasKey("Id"); b.HasIndex("SessionToken").IsUnique(); b.ToTable("participant_sessions"); });
        modelBuilder.Entity("Workspace.Infrastructure.Persistence.AuditEvent", b => { b.Property<Guid>("Id").HasColumnType("uuid"); b.Property<string>("ActorNameSnapshot").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)"); b.Property<Guid>("ActorParticipantId").HasColumnType("uuid"); b.Property<string>("Description").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)"); b.Property<DateTimeOffset>("OccurredAt").HasColumnType("timestamp with time zone"); b.HasKey("Id"); b.ToTable("audit_events"); });
        modelBuilder.Entity("Workspace.Infrastructure.Persistence.WorkspaceState", b => { b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer"); NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id")); b.Property<long>("Version").HasColumnType("bigint"); b.HasKey("Id"); b.ToTable("workspace_state"); });
        modelBuilder.Entity("Workspace.Domain.CharacterCard", b => b.HasOne("Workspace.Domain.GameAccount", null).WithMany().HasForeignKey("AccountId").OnDelete(DeleteBehavior.Restrict).IsRequired());
        modelBuilder.Entity("Workspace.Domain.ResourceReservation", b => { b.HasOne("Workspace.Domain.GameAccount", null).WithMany().HasForeignKey("AccountId").OnDelete(DeleteBehavior.Restrict).IsRequired(); b.HasOne("Workspace.Domain.CharacterCard", null).WithMany().HasForeignKey("CardId").OnDelete(DeleteBehavior.Restrict).IsRequired(); b.HasOne("Workspace.Domain.FieldRegion", null).WithMany().HasForeignKey("RegionId").OnDelete(DeleteBehavior.Restrict).IsRequired(); });
    }
}
