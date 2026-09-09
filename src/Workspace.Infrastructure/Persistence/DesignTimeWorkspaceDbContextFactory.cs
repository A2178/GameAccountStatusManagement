using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Workspace.Infrastructure.Persistence;

public sealed class DesignTimeWorkspaceDbContextFactory : IDesignTimeDbContextFactory<WorkspaceDbContext>
{
    public WorkspaceDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<WorkspaceDbContext>();
        builder.UseNpgsql("Host=localhost;Database=workspace_dev;Username=workspace;Password=workspace_dev_only");
        return new WorkspaceDbContext(builder.Options);
    }
}
