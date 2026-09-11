using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Workspace.Infrastructure.Persistence.Migrations;

[DbContext(typeof(WorkspaceDbContext))]
[Migration("202609090001_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "preview_accounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_preview_accounts", value => value.Id));

        migrationBuilder.InsertData(
            table: "preview_accounts",
            columns: ["Id", "DisplayName"],
            values: new object[,]
            {
                { Guid.Parse("10000000-0000-0000-0000-000000000001"), "青鳥測試帳號" },
                { Guid.Parse("10000000-0000-0000-0000-000000000002"), "山貓測試帳號" }
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("preview_accounts");
}
