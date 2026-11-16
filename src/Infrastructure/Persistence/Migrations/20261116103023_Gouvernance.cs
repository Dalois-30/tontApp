using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Gouvernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sexe",
                table: "participants_projet",
                type: "character varying(1)",
                maxLength: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "statut",
                table: "participants_projet",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Actif");

            migrationBuilder.CreateTable(
                name: "bureaux",
                columns: table => new
                {
                    groupe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_groupe = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    responsable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tresorier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    regle_duree_vote_jours = table.Column<int>(type: "integer", nullable: false, defaultValue: 7),
                    regle_mode_vote = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "MajoriteDesVotants"),
                    regle_quorum_pourcent = table.Column<int>(type: "integer", nullable: false, defaultValue: 50)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bureaux", x => x.groupe_id);
                });

            migrationBuilder.CreateTable(
                name: "journal_bureau",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    acteur = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    horodatage = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    hash_precedent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hash_courant = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    groupe_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_bureau", x => x.id);
                    table.ForeignKey(
                        name: "FK_journal_bureau_bureaux_groupe_id",
                        column: x => x.groupe_id,
                        principalTable: "bureaux",
                        principalColumn: "groupe_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    titre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    lieu = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    presents = table.Column<List<string>>(type: "text[]", nullable: false),
                    ordre_du_jour = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    compte_rendu = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    decisions = table.Column<List<string>>(type: "text[]", nullable: false),
                    auteur_id = table.Column<Guid>(type: "uuid", nullable: false),
                    redigee_le = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    groupe_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seances", x => x.id);
                    table.ForeignKey(
                        name: "FK_seances_bureaux_groupe_id",
                        column: x => x.groupe_id,
                        principalTable: "bureaux",
                        principalColumn: "groupe_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "votes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    objet = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    candidat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    propose_par_id = table.Column<Guid>(type: "uuid", nullable: false),
                    electeurs = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quorum_pourcent = table.Column<int>(type: "integer", nullable: false),
                    ouvert_le = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ferme_le = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    clos_le = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    groupe_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_votes", x => x.id);
                    table.ForeignKey(
                        name: "FK_votes_bureaux_groupe_id",
                        column: x => x.groupe_id,
                        principalTable: "bureaux",
                        principalColumn: "groupe_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bulletins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    utilisateur_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pour = table.Column<bool>(type: "boolean", nullable: false),
                    le = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    vote_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bulletins", x => x.id);
                    table.ForeignKey(
                        name: "FK_bulletins_votes_vote_id",
                        column: x => x.vote_id,
                        principalTable: "votes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bulletins_vote_id",
                table: "bulletins",
                column: "vote_id");

            migrationBuilder.CreateIndex(
                name: "IX_bureaux_responsable_id",
                table: "bureaux",
                column: "responsable_id");

            migrationBuilder.CreateIndex(
                name: "IX_bureaux_tresorier_id",
                table: "bureaux",
                column: "tresorier_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_bureau_groupe_id",
                table: "journal_bureau",
                column: "groupe_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_bureau_sequence",
                table: "journal_bureau",
                column: "sequence");

            migrationBuilder.CreateIndex(
                name: "IX_seances_date",
                table: "seances",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "IX_seances_groupe_id",
                table: "seances",
                column: "groupe_id");

            migrationBuilder.CreateIndex(
                name: "IX_votes_groupe_id",
                table: "votes",
                column: "groupe_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bulletins");

            migrationBuilder.DropTable(
                name: "journal_bureau");

            migrationBuilder.DropTable(
                name: "seances");

            migrationBuilder.DropTable(
                name: "votes");

            migrationBuilder.DropTable(
                name: "bureaux");

            migrationBuilder.DropColumn(
                name: "sexe",
                table: "participants_projet");

            migrationBuilder.DropColumn(
                name: "statut",
                table: "participants_projet");
        }
    }
}
