using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleballScheduler.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchScoresAndChampionship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsChampionship",
                table: "Rounds",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Team1Score",
                table: "Matches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Team2Score",
                table: "Matches",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsChampionship",
                table: "Rounds");

            migrationBuilder.DropColumn(
                name: "Team1Score",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "Team2Score",
                table: "Matches");
        }
    }
}
