namespace Workspace.Domain.Tests;

public sealed class ArchitectureSmokeTests
{
    [Fact]
    public void Domain_assembly_has_no_framework_dependencies()
    {
        var references = typeof(AssemblyMarker).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference =>
            reference.Name is not null &&
            (reference.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
             reference.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)));
    }
}
