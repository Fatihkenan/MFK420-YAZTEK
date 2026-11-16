using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YAZTEK.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialReadingAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reading_analysis_reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    text_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    reference_text_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    transcript = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    model_id = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    analysis_json = table.Column<string>(type: "jsonb", nullable: false),
                    consent_granted = table.Column<bool>(type: "boolean", nullable: false),
                    audio_retained = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reading_analysis_reports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reading_analysis_reports_expires_at_utc",
                table: "reading_analysis_reports",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_reading_analysis_reports_student_id_created_at_utc",
                table: "reading_analysis_reports",
                columns: new[] { "student_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reading_analysis_reports");
        }
    }
}
