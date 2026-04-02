using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenAudioOrchestrator.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTtsJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TtsJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    ModelProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferenceVoiceId = table.Column<int>(type: "INTEGER", nullable: true),
                    InputText = table.Column<string>(type: "TEXT", nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ReferenceId = table.Column<string>(type: "TEXT", nullable: true),
                    OutputFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TtsJobs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TtsJobs_ModelProfiles_ModelProfileId",
                        column: x => x.ModelProfileId,
                        principalTable: "ModelProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TtsJobs_ReferenceVoices_ReferenceVoiceId",
                        column: x => x.ReferenceVoiceId,
                        principalTable: "ReferenceVoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TtsJobs_ModelProfileId",
                table: "TtsJobs",
                column: "ModelProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TtsJobs_ReferenceVoiceId",
                table: "TtsJobs",
                column: "ReferenceVoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_TtsJobs_UserId",
                table: "TtsJobs",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TtsJobs");
        }
    }
}
