using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleballScheduler.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleViolations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Hr1Violations",
                table: "Events",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Hr2Violations",
                table: "Events",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RepeatSuggestion",
                table: "Events",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Hr1Violations",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Hr2Violations",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "RepeatSuggestion",
                table: "Events");
        }
    }
}
