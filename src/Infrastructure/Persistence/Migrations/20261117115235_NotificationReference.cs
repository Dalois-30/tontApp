using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reference",
                table: "notifications",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_notifications_destinataire_reference",
                table: "notifications",
                columns: new[] { "destinataire_id", "reference" },
                unique: true,
                filter: "reference IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_notifications_destinataire_reference",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "reference",
                table: "notifications");
        }
    }
}
