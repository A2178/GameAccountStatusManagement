using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Workspace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurableData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MetadataVersion",
                table: "preview_accounts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "field_regions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MetadataVersion",
                table: "character_cards",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "StageId",
                table: "character_cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetId",
                table: "audit_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stage_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stage_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CardLabel = table.Column<string>(type: "text", nullable: false),
                    SettingsLabel = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "data_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_data_records_collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "field_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    Binding = table.Column<string>(type: "text", nullable: false),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    RelationCollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Color = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    Hidden = table.Column<bool>(type: "boolean", nullable: false),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_field_definitions_collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_field_definitions_collections_RelationCollectionId",
                        column: x => x.RelationCollectionId,
                        principalTable: "collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "view_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Display = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    StageId = table.Column<Guid>(type: "uuid", nullable: true),
                    HiddenFieldsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SortFieldId = table.Column<Guid>(type: "uuid", nullable: true),
                    Descending = table.Column<bool>(type: "boolean", nullable: false),
                    FilterFieldId = table.Column<Guid>(type: "uuid", nullable: true),
                    FilterText = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_view_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_view_definitions_collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_view_definitions_stage_definitions_StageId",
                        column: x => x.StageId,
                        principalTable: "stage_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "record_field_values",
                columns: table => new
                {
                    FieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValueJson = table.Column<string>(type: "jsonb", nullable: false),
                    Attached = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_record_field_values", x => new { x.FieldId, x.RecordId });
                    table.ForeignKey(
                        name: "FK_record_field_values_field_definitions_FieldId",
                        column: x => x.FieldId,
                        principalTable: "field_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shared_field_values",
                columns: table => new
                {
                    FieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValueJson = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_field_values", x => x.FieldId);
                    table.ForeignKey(
                        name: "FK_shared_field_values_field_definitions_FieldId",
                        column: x => x.FieldId,
                        principalTable: "field_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_character_cards_StageId",
                table: "character_cards",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_data_records_CollectionId",
                table: "data_records",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_field_definitions_CollectionId",
                table: "field_definitions",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_field_definitions_RelationCollectionId",
                table: "field_definitions",
                column: "RelationCollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_view_definitions_CollectionId",
                table: "view_definitions",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_view_definitions_StageId",
                table: "view_definitions",
                column: "StageId");

            migrationBuilder.AddForeignKey(
                name: "FK_character_cards_stage_definitions_StageId",
                table: "character_cards",
                column: "StageId",
                principalTable: "stage_definitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                INSERT INTO workspace_settings ("Id", "Name", "CardLabel", "SettingsLabel", "Version") VALUES (1, '協作工作區', '卡片', '設定', 1);
                INSERT INTO collections ("Id", "Name", "Kind", "Version") VALUES
                  ('40000000-0000-0000-0000-000000000001', '角色資料', 'Cards', 1),
                  ('40000000-0000-0000-0000-000000000002', '帳號資料', 'Accounts', 1);
                INSERT INTO stage_definitions ("Id", "Name", "Position", "Version") VALUES
                  ('50000000-0000-0000-0000-000000000001', '練等', 1, 1),
                  ('50000000-0000-0000-0000-000000000002', '任務', 2, 1),
                  ('50000000-0000-0000-0000-000000000003', '功勳', 3, 1),
                  ('50000000-0000-0000-0000-000000000004', '正式活動', 4, 1);
                INSERT INTO view_definitions ("Id", "CollectionId", "Name", "Display", "Position", "StageId", "HiddenFieldsJson", "Descending", "FilterText", "Version") VALUES
                  ('60000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000001', '總覽', 'Board', 1, NULL, '[]', false, '', 1),
                  ('60000000-0000-0000-0000-000000000002', '40000000-0000-0000-0000-000000000002', '帳號', 'Table', 0, NULL, '[]', false, '', 1),
                  ('60000000-0000-0000-0000-000000000003', '40000000-0000-0000-0000-000000000001', '練等', 'Table', 2, '50000000-0000-0000-0000-000000000001', '[]', false, '', 1),
                  ('60000000-0000-0000-0000-000000000004', '40000000-0000-0000-0000-000000000001', '任務', 'Table', 3, '50000000-0000-0000-0000-000000000002', '[]', false, '', 1),
                  ('60000000-0000-0000-0000-000000000005', '40000000-0000-0000-0000-000000000001', '功勳', 'Table', 4, '50000000-0000-0000-0000-000000000003', '[]', false, '', 1),
                  ('60000000-0000-0000-0000-000000000006', '40000000-0000-0000-0000-000000000001', '正式活動', 'Table', 5, '50000000-0000-0000-0000-000000000004', '[]', false, '', 1);
                INSERT INTO field_definitions ("Id", "CollectionId", "Name", "Kind", "Scope", "Binding", "OptionsJson", "Color", "Position", "Required", "Hidden", "Deleted", "Version") VALUES
                  ('70000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000001', '前往區域', 'Text', 'Common', 'Resource', '[]', '#22d3ee', 0, false, false, false, 1),
                  ('70000000-0000-0000-0000-000000000002', '40000000-0000-0000-0000-000000000002', '登入帳號', 'Text', 'Common', 'None', '[]', '#526b84', 0, false, false, false, 1),
                  ('70000000-0000-0000-0000-000000000003', '40000000-0000-0000-0000-000000000002', '密碼', 'Text', 'Common', 'None', '[]', '#526b84', 1, false, false, false, 1),
                  ('70000000-0000-0000-0000-000000000004', '40000000-0000-0000-0000-000000000002', '擁有者', 'Text', 'Common', 'None', '[]', '#526b84', 2, false, false, false, 1);
                UPDATE preview_accounts SET "MetadataVersion" = 1;
                UPDATE character_cards SET "MetadataVersion" = 1;
                UPDATE field_regions SET "Version" = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_character_cards_stage_definitions_StageId",
                table: "character_cards");

            migrationBuilder.DropTable(
                name: "data_records");

            migrationBuilder.DropTable(
                name: "record_field_values");

            migrationBuilder.DropTable(
                name: "shared_field_values");

            migrationBuilder.DropTable(
                name: "view_definitions");

            migrationBuilder.DropTable(
                name: "workspace_settings");

            migrationBuilder.DropTable(
                name: "field_definitions");

            migrationBuilder.DropTable(
                name: "stage_definitions");

            migrationBuilder.DropTable(
                name: "collections");

            migrationBuilder.DropIndex(
                name: "IX_character_cards_StageId",
                table: "character_cards");

            migrationBuilder.DropColumn(
                name: "MetadataVersion",
                table: "preview_accounts");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "field_regions");

            migrationBuilder.DropColumn(
                name: "MetadataVersion",
                table: "character_cards");

            migrationBuilder.DropColumn(
                name: "StageId",
                table: "character_cards");

            migrationBuilder.DropColumn(
                name: "TargetId",
                table: "audit_events");
        }
    }
}
