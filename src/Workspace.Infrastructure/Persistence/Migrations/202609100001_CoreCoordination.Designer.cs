using Microsoft.EntityFrameworkCore;

#nullable disable

namespace Workspace.Infrastructure.Persistence.Migrations;

public sealed partial class CoreCoordination
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => M1Model.Build(modelBuilder);
}
