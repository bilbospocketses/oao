using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenAudioOrchestrator.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceLibraryAndGenerationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReferenceVoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VoiceId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AudioFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    TranscriptText = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferenceVoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GenerationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModelProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferenceVoiceId = table.Column<int>(type: "INTEGER", nullable: true),
                    InputText = table.Column<string>(type: "TEXT", nullable: false),
                    OutputFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenerationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GenerationLogs_ModelProfiles_ModelProfileId",
                        column: x => x.ModelProfileId,
                        principalTable: "ModelProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GenerationLogs_ReferenceVoices_ReferenceVoiceId",
                        column: x => x.ReferenceVoiceId,
                        principalTable: "ReferenceVoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationLogs_ModelProfileId",
                table: "GenerationLogs",
                column: "ModelProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationLogs_ReferenceVoiceId",
                table: "GenerationLogs",
                column: "ReferenceVoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceVoices_VoiceId",
                table: "ReferenceVoices",
                column: "VoiceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GenerationLogs");

            migrationBuilder.DropTable(
                name: "ReferenceVoices");
        }
    }
}
