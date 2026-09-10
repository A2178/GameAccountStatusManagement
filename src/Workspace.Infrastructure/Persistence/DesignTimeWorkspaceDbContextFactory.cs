using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Workspace.Infrastructure.Persistence;

public sealed class DesignTimeWorkspaceDbContextFactory : IDesignTimeDbContextFactory<WorkspaceDbContext>
{
    private const string DevelopmentConnectionString =
        "Host=localhost;Database=workspace_dev;Username=workspace;Password=workspace_dev_only";

    public WorkspaceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Workspace")
            ?? DevelopmentConnectionString;
        var builder = new DbContextOptionsBuilder<WorkspaceDbContext>();
        builder.UseNpgsql(connectionString);
        return new WorkspaceDbContext(builder.Options);
    }
}
