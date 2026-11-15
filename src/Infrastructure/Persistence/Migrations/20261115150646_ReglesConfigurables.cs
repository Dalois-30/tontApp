using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReglesConfigurables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "adhesion_en_cours",
                table: "tontines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "AvecValidation");

            migrationBuilder.AddColumn<bool>(
                name: "rattrapage_adhesion_tardive",
                table: "tontines",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "regle_baisse_autorisee",
                table: "projets",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "regle_baisse_retroactive",
                table: "projets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "regle_baisse_validation",
                table: "projets",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "regle_hausse_retroactive",
                table: "projets",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "regle_hausse_validation",
                table: "projets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "regle_paiement_partiel",
                table: "projets",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "regle_rattrapage_adhesion",
                table: "projets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "regle_trop_percu",
                table: "projets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Reporter");

            migrationBuilder.AddColumn<int>(
                name: "premier_tour_du",
                table: "membres_tontine",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "retroactif",
                table: "changements_palier",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "adhesion_en_cours",
                table: "tontines");

            migrationBuilder.DropColumn(
                name: "rattrapage_adhesion_tardive",
                table: "tontines");

            migrationBuilder.DropColumn(
                name: "regle_baisse_autorisee",
                table: "projets");

            migrationBuilder.DropColumn(
                name: "regle_baisse_retroactive",
                table: "projets");

            migrationBuilder.DropColumn(
                name: "regle_baisse_validation",
                table: "projets");

            migrationBuilder.DropColumn(
                name: "regle_hausse_retroactive",
                table: "projets");

            migrationBuilder.DropColumn(
                name: "regle_hausse_validation",
                table: "projets");

            migrationBuilder.DropColumn(
                name: "regle_paiement_partiel",
                table: "projets");

            migrationBuilder.DropColumn(
                name: "regle_rattrapage_adhesion",
                table: "projets");

            migrationBuilder.DropColumn(
                name: "regle_trop_percu",
                table: "projets");

            migrationBuilder.DropColumn(
                name: "premier_tour_du",
                table: "membres_tontine");

            migrationBuilder.DropColumn(
                name: "retroactif",
                table: "changements_palier");
        }
    }
}
