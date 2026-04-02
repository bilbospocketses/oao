using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenAudioOrchestrator.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CheckpointPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ImageTag = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    HostPort = table.Column<int>(type: "INTEGER", nullable: false),
                    EnableHalf = table.Column<bool>(type: "INTEGER", nullable: false),
                    CudaAllocConf = table.Column<string>(type: "TEXT", nullable: true),
                    ContainerId = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModelProfiles_Name",
                table: "ModelProfiles",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelProfiles");
        }
    }
}
