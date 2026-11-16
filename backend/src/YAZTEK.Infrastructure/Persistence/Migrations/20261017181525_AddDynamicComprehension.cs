using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YAZTEK.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicComprehension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comprehension_test_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    text_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    text_title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    source_text = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    source_text_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    total_score = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: true),
                    max_score = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    failure_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comprehension_test_attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "comprehension_questions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_order = table.Column<int>(type: "integer", nullable: false),
                    question_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    skill = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    difficulty = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    prompt = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    options_json = table.Column<string>(type: "jsonb", nullable: true),
                    correct_option_index = table.Column<int>(type: "integer", nullable: true),
                    reference_answer = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    explanation = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    evidence = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    rubric_json = table.Column<string>(type: "jsonb", nullable: false),
                    normalized_prompt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comprehension_questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_comprehension_questions_comprehension_test_attempts_attempt~",
                        column: x => x.attempt_id,
                        principalTable: "comprehension_test_attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comprehension_answers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selected_option_index = table.Column<int>(type: "integer", nullable: true),
                    text_answer = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    evaluation_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    awarded_points = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    feedback = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comprehension_answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_comprehension_answers_comprehension_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "comprehension_questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_comprehension_answers_question_id",
                table: "comprehension_answers",
                column: "question_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_comprehension_questions_attempt_id_question_order",
                table: "comprehension_questions",
                columns: new[] { "attempt_id", "question_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_comprehension_questions_normalized_prompt_hash",
                table: "comprehension_questions",
                column: "normalized_prompt_hash");

            migrationBuilder.CreateIndex(
                name: "IX_comprehension_test_attempts_student_id_text_id_idempotency_~",
                table: "comprehension_test_attempts",
                columns: new[] { "student_id", "text_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_comprehension_test_attempts_student_id_text_id_source_text_~",
                table: "comprehension_test_attempts",
                columns: new[] { "student_id", "text_id", "source_text_hash", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comprehension_answers");

            migrationBuilder.DropTable(
                name: "comprehension_questions");

            migrationBuilder.DropTable(
                name: "comprehension_test_attempts");
        }
    }
}
