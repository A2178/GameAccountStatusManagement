using Microsoft.EntityFrameworkCore;
using Workspace.Domain;

namespace Workspace.Infrastructure.Persistence;

public sealed class WorkspaceDbContext(DbContextOptions<WorkspaceDbContext> options) : DbContext(options)
{
    public DbSet<GameAccount> Accounts => Set<GameAccount>();
    public DbSet<CharacterCard> Cards => Set<CharacterCard>();
    public DbSet<FieldRegion> Regions => Set<FieldRegion>();
    public DbSet<ResourceReservation> Reservations => Set<ResourceReservation>();
    public DbSet<ParticipantSession> Participants => Set<ParticipantSession>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<WorkspaceState> WorkspaceStates => Set<WorkspaceState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameAccount>(entity => { entity.ToTable("preview_accounts"); entity.HasKey(x => x.Id); entity.Property(x => x.DisplayName).HasMaxLength(80); });
        modelBuilder.Entity<CharacterCard>(entity => { entity.ToTable("character_cards"); entity.HasKey(x => x.Id); entity.Property(x => x.DisplayName).HasMaxLength(80); entity.Property(x => x.UsageStatus).HasConversion<string>(); entity.HasOne<GameAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<FieldRegion>(entity => { entity.ToTable("field_regions"); entity.HasKey(x => x.Id); entity.Property(x => x.DisplayName).HasMaxLength(80); });
        modelBuilder.Entity<ResourceReservation>(entity => { entity.ToTable("resource_reservations"); entity.HasKey(x => x.Id); entity.Property(x => x.State).HasConversion<string>(); entity.Ignore(x => x.IsActive); entity.HasIndex(x => new { x.AccountId, x.State }); entity.HasIndex(x => x.CardId).IsUnique().HasFilter("\"State\" <> 'Released'"); entity.HasOne<GameAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict); entity.HasOne<CharacterCard>().WithMany().HasForeignKey(x => x.CardId).OnDelete(DeleteBehavior.Restrict); entity.HasOne<FieldRegion>().WithMany().HasForeignKey(x => x.RegionId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<ParticipantSession>(entity => { entity.ToTable("participant_sessions"); entity.HasKey(x => x.Id); entity.Property(x => x.Nickname).HasMaxLength(80); entity.HasIndex(x => x.SessionToken).IsUnique(); });
        modelBuilder.Entity<AuditEvent>(entity => { entity.ToTable("audit_events"); entity.HasKey(x => x.Id); entity.Property(x => x.ActorNameSnapshot).HasMaxLength(80); entity.Property(x => x.Description).HasMaxLength(500); });
        modelBuilder.Entity<WorkspaceState>(entity => { entity.ToTable("workspace_state"); entity.HasKey(x => x.Id); });
    }
}

public sealed class ParticipantSession
{
    private ParticipantSession() { }
    public ParticipantSession(Guid id, Guid sessionToken, string nickname, DateTimeOffset now) { Id = id; SessionToken = sessionToken; Nickname = nickname.Trim(); CreatedAt = now; }
    public Guid Id { get; private set; }
    public Guid SessionToken { get; private set; }
    public string Nickname { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsAdmin => Nickname == "Admin";
}

public sealed class AuditEvent
{
    private AuditEvent() { }
    public AuditEvent(Guid id, Guid actorId, string actorName, string description, DateTimeOffset now) { Id = id; ActorParticipantId = actorId; ActorNameSnapshot = actorName; Description = description; OccurredAt = now; }
    public Guid Id { get; private set; }
    public Guid ActorParticipantId { get; private set; }
    public string ActorNameSnapshot { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
}

public sealed class WorkspaceState
{
    private WorkspaceState() { }
    public WorkspaceState(int id) { Id = id; }
    public int Id { get; private set; }
    public long Version { get; private set; }
    public void Advance() => Version++;
}
