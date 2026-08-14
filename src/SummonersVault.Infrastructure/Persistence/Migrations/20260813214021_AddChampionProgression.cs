using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SummonersVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionProgression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "alias",
                table: "champions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "variant",
                table: "champions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE champions SET variant = 1 WHERE champion_id BETWEEN 60000 AND 60999;");

            migrationBuilder.CreateTable(
                name: "champion_eternal_sets",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "TEXT", nullable: false),
                    champion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    set_id = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    milestones_passed = table.Column<int>(type: "INTEGER", nullable: false),
                    stones_available = table.Column<int>(type: "INTEGER", nullable: false),
                    stones_illuminated = table.Column<int>(type: "INTEGER", nullable: false),
                    stones_owned = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_eternal_sets", x => new { x.account_id, x.champion_id, x.set_id });
                    table.ForeignKey(
                        name: "FK_champion_eternal_sets_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "champion_eternal_summaries",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "TEXT", nullable: false),
                    champion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    milestones_passed = table.Column<int>(type: "INTEGER", nullable: false),
                    stones_available = table.Column<int>(type: "INTEGER", nullable: false),
                    stones_illuminated = table.Column<int>(type: "INTEGER", nullable: false),
                    stones_owned = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_eternal_summaries", x => new { x.account_id, x.champion_id });
                    table.ForeignKey(
                        name: "FK_champion_eternal_summaries_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "champion_eternals",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "TEXT", nullable: false),
                    statstone_id = table.Column<string>(type: "TEXT", nullable: false),
                    champion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    set_id = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    category = table.Column<string>(type: "TEXT", nullable: true),
                    value = table.Column<double>(type: "REAL", nullable: false),
                    formatted_value = table.Column<string>(type: "TEXT", nullable: true),
                    milestone_level = table.Column<int>(type: "INTEGER", nullable: false),
                    formatted_milestone_level = table.Column<string>(type: "TEXT", nullable: true),
                    next_milestone = table.Column<double>(type: "REAL", nullable: true),
                    personal_best = table.Column<double>(type: "REAL", nullable: true),
                    formatted_personal_best = table.Column<string>(type: "TEXT", nullable: true),
                    is_complete = table.Column<int>(type: "INTEGER", nullable: false),
                    is_epic = table.Column<int>(type: "INTEGER", nullable: false),
                    is_featured = table.Column<int>(type: "INTEGER", nullable: false),
                    is_retired = table.Column<int>(type: "INTEGER", nullable: false),
                    image_asset_path = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_eternals", x => new { x.account_id, x.statstone_id });
                    table.ForeignKey(
                        name: "FK_champion_eternals_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "champion_masteries",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "TEXT", nullable: false),
                    champion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    level = table.Column<int>(type: "INTEGER", nullable: false),
                    points = table.Column<long>(type: "INTEGER", nullable: false),
                    points_since_last_level = table.Column<long>(type: "INTEGER", nullable: false),
                    points_until_next_level = table.Column<long>(type: "INTEGER", nullable: false),
                    season_milestone = table.Column<int>(type: "INTEGER", nullable: false),
                    highest_grade = table.Column<string>(type: "TEXT", nullable: true),
                    last_play_at = table.Column<string>(type: "TEXT", nullable: true),
                    marks_required_for_next_level = table.Column<int>(type: "INTEGER", nullable: false),
                    milestone_grades_json = table.Column<string>(type: "TEXT", nullable: false),
                    tokens_earned = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_masteries", x => new { x.account_id, x.champion_id });
                    table.ForeignKey(
                        name: "FK_champion_masteries_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_eternals_account_champion",
                table: "champion_eternals",
                columns: new[] { "account_id", "champion_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mastery_account_points",
                table: "champion_masteries",
                columns: new[] { "account_id", "points" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_eternal_sets");

            migrationBuilder.DropTable(
                name: "champion_eternal_summaries");

            migrationBuilder.DropTable(
                name: "champion_eternals");

            migrationBuilder.DropTable(
                name: "champion_masteries");

            migrationBuilder.DropColumn(
                name: "alias",
                table: "champions");

            migrationBuilder.DropColumn(
                name: "variant",
                table: "champions");
        }
    }
}
