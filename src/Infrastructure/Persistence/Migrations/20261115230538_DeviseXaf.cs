using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviseXaf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default currency is now XAF (Cameroon). Existing amounts keep their numeric value.
            migrationBuilder.Sql("UPDATE tontines SET devise = 'XAF' WHERE devise = 'XOF';");
            migrationBuilder.Sql("UPDATE versements SET devise = 'XAF' WHERE devise = 'XOF';");
            migrationBuilder.Sql("UPDATE projets SET devise = 'XAF' WHERE devise = 'XOF';");

            migrationBuilder.UpdateData(
                table: "plans_abonnement",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "devise",
                value: "XAF");

            migrationBuilder.UpdateData(
                table: "plans_abonnement",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "devise",
                value: "XAF");

            migrationBuilder.UpdateData(
                table: "plans_abonnement",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                column: "devise",
                value: "XAF");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "plans_abonnement",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "devise",
                value: "XOF");

            migrationBuilder.UpdateData(
                table: "plans_abonnement",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "devise",
                value: "XOF");

            migrationBuilder.UpdateData(
                table: "plans_abonnement",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                column: "devise",
                value: "XOF");
        }
    }
}
