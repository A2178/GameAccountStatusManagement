using Microsoft.EntityFrameworkCore;
using Workspace.Infrastructure.Persistence;

namespace Workspace.IntegrationTests;

public sealed class DesignTimeWorkspaceDbContextFactoryTests
{
    [Fact]
    public void CreateDbContext_uses_CI_connection_string_from_environment()
    {
        const string variableName = "ConnectionStrings__Workspace";
        const string expected =
            "Host=ci-postgres;Database=workspace_test;Username=workspace;Password=test-only";
        var original = Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, expected);

            using var context = new DesignTimeWorkspaceDbContextFactory().CreateDbContext([]);

            Assert.Equal(expected, context.Database.GetConnectionString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, original);
        }
    }
}
