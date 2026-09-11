using Microsoft.EntityFrameworkCore;

#nullable disable

namespace Workspace.Infrastructure.Persistence.Migrations;

public partial class InitialCreate
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.10");

        modelBuilder.Entity("Workspace.Infrastructure.Persistence.PreviewAccount", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid");
            entity.Property<string>("DisplayName")
                .IsRequired()
                .HasMaxLength(80)
                .HasColumnType("character varying(80)");
            entity.HasKey("Id");
            entity.ToTable("preview_accounts");
            entity.HasData(
                new
                {
                    Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    DisplayName = "青鳥測試帳號"
                },
                new
                {
                    Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                    DisplayName = "山貓測試帳號"
                });
        });
    }
}
