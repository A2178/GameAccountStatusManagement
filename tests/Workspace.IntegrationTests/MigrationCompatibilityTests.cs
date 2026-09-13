using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Workspace.Infrastructure.Persistence;

namespace Workspace.IntegrationTests;

public sealed class MigrationCompatibilityTests
{
    [Theory]
    [InlineData("InitialCreate", "202609090001_InitialCreate")]
    [InlineData("202609090001_InitialCreate", "202609090001_InitialCreate")]
    [InlineData("CoreCoordination", "202609100001_CoreCoordination")]
    [InlineData("202609100001_CoreCoordination", "202609100001_CoreCoordination")]
    public void Existing_migrations_resolve_by_name_and_unchanged_history_id(string target, string expected)
    {
        using var context = new WorkspaceDbContext(new DbContextOptionsBuilder<WorkspaceDbContext>()
            .UseNpgsql("Host=localhost;Database=migration_metadata_only").Options);

        Assert.Equal(expected, context.GetService<IMigrationsAssembly>().GetMigrationId(target));
    }

    [Fact]
    public void Upgrade_script_starts_after_M0_and_preserves_preview_accounts()
    {
        using var context = new DesignTimeWorkspaceDbContextFactory().CreateDbContext([]);

        var script = context.GetService<IMigrator>().GenerateScript("InitialCreate", "CoreCoordination");

        Assert.Contains("ALTER TABLE preview_accounts", script);
        Assert.DoesNotContain("DROP TABLE preview_accounts", script);
        Assert.DoesNotContain("CREATE TABLE preview_accounts", script);
        Assert.Contains("202609100001_CoreCoordination", script);
    }

    [Fact]
    public void New_migration_ids_keep_the_standard_format_and_unique_order()
    {
        var generator = new WorkspaceMigrationsIdGenerator();
        var first = generator.GenerateId("Next_Migration");
        var second = generator.GenerateId("Next_Migration");

        Assert.Matches(@"^[0-9]{14}_Next_Migration$", first);
        Assert.True(generator.IsValidId(first));
        Assert.Equal("Next_Migration", generator.GetName(first));
        Assert.True(string.CompareOrdinal(first, second) < 0);
        Assert.False(generator.IsValidId("InitialCreate"));
    }
}
