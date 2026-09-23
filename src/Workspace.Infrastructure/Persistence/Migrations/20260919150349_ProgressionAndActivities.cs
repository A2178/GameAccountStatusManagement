using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Workspace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProgressionAndActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedFromStageIdsJson",
                table: "stage_definitions",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "EntryRequirement",
                table: "stage_definitions",
                type: "text",
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "character_cards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesCardId",
                table: "character_cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "audit_events",
                type: "character varying(1200)",
                maxLength: 1200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.CreateTable(
                name: "command_receipts",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_command_receipts", x => x.RequestId);
                    table.ForeignKey(
                        name: "FK_command_receipts_character_cards_CardId",
                        column: x => x.CardId,
                        principalTable: "character_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "credit_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    TotalAfter = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_credit_entries_character_cards_CardId",
                        column: x => x.CardId,
                        principalTable: "character_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "progression_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    LevelTarget = table.Column<long>(type: "bigint", nullable: false),
                    TaskItemTarget = table.Column<long>(type: "bigint", nullable: false),
                    MeritTarget = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_progression_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "progression_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TimeZoneId = table.Column<string>(type: "text", nullable: false),
                    ResetHour = table.Column<int>(type: "integer", nullable: false),
                    ConversionRequiresActiveActivity = table.Column<bool>(type: "boolean", nullable: false),
                    AccumulationStageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_progression_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_progression_settings_stage_definitions_AccumulationStageId",
                        column: x => x.AccumulationStageId,
                        principalTable: "stage_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "qualification_cycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    QualifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EligibleFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "text", nullable: false),
                    ResetHour = table.Column<int>(type: "integer", nullable: false),
                    ThresholdSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InvalidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InvalidationReason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qualification_cycles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_qualification_cycles_character_cards_CardId",
                        column: x => x.CardId,
                        principalTable: "character_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "card_progression",
                columns: table => new
                {
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Level = table.Column<long>(type: "bigint", nullable: false),
                    TaskItems = table.Column<long>(type: "bigint", nullable: false),
                    MeritBalance = table.Column<long>(type: "bigint", nullable: false),
                    CumulativeCredits = table.Column<long>(type: "bigint", nullable: false),
                    CreditVersion = table.Column<long>(type: "bigint", nullable: false),
                    CurrentCycleId = table.Column<Guid>(type: "uuid", nullable: true),
                    PermanentlyDisqualified = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_progression", x => x.CardId);
                    table.CheckConstraint("CK_progression_balances", "\"MeritBalance\" >= 0 AND \"CumulativeCredits\" >= 0 AND \"CumulativeCredits\" <= 9007199254740991");
                    table.ForeignKey(
                        name: "FK_card_progression_character_cards_CardId",
                        column: x => x.CardId,
                        principalTable: "character_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_card_progression_progression_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "progression_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "activity_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<Guid>(type: "uuid", nullable: false),
                    QualificationCycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorWasHidden = table.Column<bool>(type: "boolean", nullable: false),
                    Channel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_activity_sessions_character_cards_CardId",
                        column: x => x.CardId,
                        principalTable: "character_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_activity_sessions_qualification_cycles_QualificationCycleId",
                        column: x => x.QualificationCycleId,
                        principalTable: "qualification_cycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_character_cards_ReplacesCardId",
                table: "character_cards",
                column: "ReplacesCardId");

            migrationBuilder.CreateIndex(
                name: "IX_activity_sessions_CardId",
                table: "activity_sessions",
                column: "CardId",
                unique: true,
                filter: "\"EndedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_activity_sessions_QualificationCycleId",
                table: "activity_sessions",
                column: "QualificationCycleId");

            migrationBuilder.CreateIndex(
                name: "IX_card_progression_ProfileId",
                table: "card_progression",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_command_receipts_CardId",
                table: "command_receipts",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_entries_CardId",
                table: "credit_entries",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_entries_RequestId",
                table: "credit_entries",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_progression_settings_AccumulationStageId",
                table: "progression_settings",
                column: "AccumulationStageId");

            migrationBuilder.CreateIndex(
                name: "IX_qualification_cycles_CardId",
                table: "qualification_cycles",
                column: "CardId",
                unique: true,
                filter: "\"InvalidatedAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_character_cards_character_cards_ReplacesCardId",
                table: "character_cards",
                column: "ReplacesCardId",
                principalTable: "character_cards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                INSERT INTO progression_settings ("Id", "TimeZoneId", "ResetHour", "ConversionRequiresActiveActivity", "AccumulationStageId", "Version")
                VALUES (1, 'Asia/Taipei', 4, false, '50000000-0000-0000-0000-000000000003', 1);
                UPDATE stage_definitions SET "EntryRequirement" = 'Level' WHERE "Id" = '50000000-0000-0000-0000-000000000002';
                UPDATE stage_definitions SET "EntryRequirement" = 'Task' WHERE "Id" = '50000000-0000-0000-0000-000000000003';
                UPDATE stage_definitions SET "EntryRequirement" = 'Qualification' WHERE "Id" = '50000000-0000-0000-0000-000000000004';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_character_cards_character_cards_ReplacesCardId",
                table: "character_cards");

            migrationBuilder.DropTable(
                name: "activity_sessions");

            migrationBuilder.DropTable(
                name: "card_progression");

            migrationBuilder.DropTable(
                name: "command_receipts");

            migrationBuilder.DropTable(
                name: "credit_entries");

            migrationBuilder.DropTable(
                name: "progression_settings");

            migrationBuilder.DropTable(
                name: "qualification_cycles");

            migrationBuilder.DropTable(
                name: "progression_profiles");

            migrationBuilder.DropIndex(
                name: "IX_character_cards_ReplacesCardId",
                table: "character_cards");

            migrationBuilder.DropColumn(
                name: "AllowedFromStageIdsJson",
                table: "stage_definitions");

            migrationBuilder.DropColumn(
                name: "EntryRequirement",
                table: "stage_definitions");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "character_cards");

            migrationBuilder.DropColumn(
                name: "ReplacesCardId",
                table: "character_cards");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "audit_events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1200)",
                oldMaxLength: 1200);
        }
    }
}
