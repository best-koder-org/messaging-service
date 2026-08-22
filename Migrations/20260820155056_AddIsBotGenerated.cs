using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MessagingService.Migrations
{
    /// <inheritdoc />
    public partial class AddIsBotGenerated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBotGenerated",
                table: "Messages",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "GhostTrackings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GhostUserId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VictimUserId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConversationId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DetectedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Reported = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GhostTrackings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Conversation_Filter",
                table: "Messages",
                columns: new[] { "ConversationId", "IsDeleted", "ModerationStatus", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Participants",
                table: "Messages",
                columns: new[] { "SenderId", "ReceiverId" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Unread_Filter",
                table: "Messages",
                columns: new[] { "ReceiverId", "IsRead", "IsDeleted", "ModerationStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_GhostTrackings_ConversationId",
                table: "GhostTrackings",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_GhostTrackings_GhostUserId_VictimUserId",
                table: "GhostTrackings",
                columns: new[] { "GhostUserId", "VictimUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GhostTrackings");

            migrationBuilder.DropIndex(
                name: "IX_Messages_Conversation_Filter",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_Participants",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_Unread_Filter",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "IsBotGenerated",
                table: "Messages");
        }
    }
}
