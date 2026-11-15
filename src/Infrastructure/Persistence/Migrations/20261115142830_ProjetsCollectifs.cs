using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjetsCollectifs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nom = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    objectif = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    devise = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    periodicite = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    date_debut = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    nombre_echeances = table.Column<int>(type: "integer", nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    gestionnaire_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    code_invitation_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    code_invitation_expire_le = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    paliers = table.Column<List<decimal>>(type: "numeric(18,2)[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "depenses_projet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    libelle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    montant = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    justificatif = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    proposee_le = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    proposee_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    decidee_le = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decidee_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    motif = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    projet_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_depenses_projet", x => x.id);
                    table.ForeignKey(
                        name: "FK_depenses_projet_projets_projet_id",
                        column: x => x.projet_id,
                        principalTable: "projets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "journal_projet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    acteur = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    horodatage = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    hash_precedent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hash_courant = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    projet_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_projet", x => x.id);
                    table.ForeignKey(
                        name: "FK_journal_projet_projets_projet_id",
                        column: x => x.projet_id,
                        principalTable: "projets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "paiements_projet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    montant = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    preuve = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    saisi_le = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    saisi_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    decide_le = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decide_par = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    motif = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    projet_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_paiements_projet", x => x.id);
                    table.ForeignKey(
                        name: "FK_paiements_projet_projets_projet_id",
                        column: x => x.projet_id,
                        principalTable: "projets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "participants_projet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    utilisateur_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_adhesion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    projet_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_participants_projet", x => x.id);
                    table.ForeignKey(
                        name: "FK_participants_projet_projets_projet_id",
                        column: x => x.projet_id,
                        principalTable: "projets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "changements_palier",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    palier = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    applicable_des = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    demande_le = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_changements_palier", x => x.id);
                    table.ForeignKey(
                        name: "FK_changements_palier_participants_projet_participant_id",
                        column: x => x.participant_id,
                        principalTable: "participants_projet",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_changements_palier_participant_id",
                table: "changements_palier",
                column: "participant_id");

            migrationBuilder.CreateIndex(
                name: "IX_depenses_projet_projet_id",
                table: "depenses_projet",
                column: "projet_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_projet_projet_id",
                table: "journal_projet",
                column: "projet_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_projet_sequence",
                table: "journal_projet",
                column: "sequence");

            migrationBuilder.CreateIndex(
                name: "IX_paiements_projet_projet_id",
                table: "paiements_projet",
                column: "projet_id");

            migrationBuilder.CreateIndex(
                name: "IX_participants_projet_projet_id",
                table: "participants_projet",
                column: "projet_id");

            migrationBuilder.CreateIndex(
                name: "IX_projets_code_invitation_hash",
                table: "projets",
                column: "code_invitation_hash");

            migrationBuilder.CreateIndex(
                name: "IX_projets_gestionnaire_id",
                table: "projets",
                column: "gestionnaire_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "changements_palier");

            migrationBuilder.DropTable(
                name: "depenses_projet");

            migrationBuilder.DropTable(
                name: "journal_projet");

            migrationBuilder.DropTable(
                name: "paiements_projet");

            migrationBuilder.DropTable(
                name: "participants_projet");

            migrationBuilder.DropTable(
                name: "projets");
        }
    }
}
