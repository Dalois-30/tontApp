using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PalierEffectifLe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "effectif_le",
                table: "changements_palier",
                type: "timestamp with time zone",
                nullable: true);

            // Existing applied changes took effect when they were recorded.
            migrationBuilder.Sql("UPDATE changements_palier SET effectif_le = demande_le WHERE statut = 'Applique';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "effectif_le",
                table: "changements_palier");
        }
    }
}
