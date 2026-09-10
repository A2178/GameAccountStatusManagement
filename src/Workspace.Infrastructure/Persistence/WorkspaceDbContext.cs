using Microsoft.EntityFrameworkCore;

namespace Workspace.Infrastructure.Persistence;

public sealed class WorkspaceDbContext(DbContextOptions<WorkspaceDbContext> options) : DbContext(options)
{
    public DbSet<PreviewAccount> PreviewAccounts => Set<PreviewAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<PreviewAccount>();
        account.ToTable("preview_accounts");
        account.HasKey(value => value.Id);
        account.Property(value => value.DisplayName).HasMaxLength(80).IsRequired();
        account.HasData(
            new PreviewAccount(Guid.Parse("10000000-0000-0000-0000-000000000001"), "青鳥測試帳號"),
            new PreviewAccount(Guid.Parse("10000000-0000-0000-0000-000000000002"), "山貓測試帳號"));
    }
}

public sealed record PreviewAccount(Guid Id, string DisplayName);
