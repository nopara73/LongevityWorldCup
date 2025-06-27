using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LongevityWorldCup.Website.Migrations
{
    public partial class CreateAgeGuesses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgeGuesses",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AthleteId = table.Column<int>(nullable: false),
                    Guess = table.Column<int>(nullable: false),
                    WhenUtc = table.Column<DateTime>(nullable: false),
                    FingerprintHash = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgeGuesses", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgeGuesses");
        }
    }
}
