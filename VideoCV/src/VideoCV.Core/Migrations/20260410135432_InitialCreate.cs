using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VideoCV.Core.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Videos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShortId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    Skills = table.Column<string>(type: "TEXT", nullable: true),
                    Experience = table.Column<string>(type: "TEXT", nullable: true),
                    Education = table.Column<string>(type: "TEXT", nullable: true),
                    LinkedInUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CvFileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Script = table.Column<string>(type: "TEXT", nullable: true),
                    AudioPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    VideoPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ThumbnailPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    ViewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TemplateName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Videos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    PreviewImagePath = table.Column<string>(type: "TEXT", nullable: true),
                    BackgroundColor = table.Column<string>(type: "TEXT", nullable: false),
                    AccentColor = table.Column<string>(type: "TEXT", nullable: false),
                    FontFamily = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPremium = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SharedWithEmail = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    SharedWithName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    ViewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ViewedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoShares_Videos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "Videos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "VideoTemplates",
                columns: new[] { "Id", "AccentColor", "BackgroundColor", "CreatedAt", "Description", "DisplayName", "FontFamily", "IsActive", "IsPremium", "Name", "PreviewImagePath", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), "#3b82f6", "#0f172a", new DateTime(2026, 4, 10, 13, 54, 32, 286, DateTimeKind.Utc).AddTicks(2940), "Sleek dark theme with blue accents", "Professional Dark", "Inter", true, false, "professional-dark", null, 1 },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "#6366f1", "#ffffff", new DateTime(2026, 4, 10, 13, 54, 32, 286, DateTimeKind.Utc).AddTicks(2954), "Clean white theme with gradient accents", "Modern Light", "Inter", true, false, "modern-light", null, 2 },
                    { new Guid("33333333-3333-3333-3333-333333333333"), "#a78bfa", "#1e1b4b", new DateTime(2026, 4, 10, 13, 54, 32, 286, DateTimeKind.Utc).AddTicks(2959), "Bold gradient background with modern typography", "Creative Gradient", "Inter", true, false, "creative-gradient", null, 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Videos_Email",
                table: "Videos",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Videos_ShortId",
                table: "Videos",
                column: "ShortId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoShares_VideoId_SharedWithEmail",
                table: "VideoShares",
                columns: new[] { "VideoId", "SharedWithEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_VideoTemplates_Name",
                table: "VideoTemplates",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoShares");

            migrationBuilder.DropTable(
                name: "VideoTemplates");

            migrationBuilder.DropTable(
                name: "Videos");
        }
    }
}
