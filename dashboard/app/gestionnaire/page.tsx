import Link from "next/link";
import { cookies } from "next/headers";
import { ApiError, apiFetch } from "@/lib/api";
import { formatMontant, formatDate, libellePeriodicite, libelleStatutGroupe, libelleStatutMembre, doitSurLeTour,
  peutGerer, libelleRolesBureau, MESSAGE_HORS_BUREAU } from "@/lib/format";
import type { MembreDto, TontineDto, VersementDto } from "@/lib/types";
import { Badge, Card, EmptyState, Icon, PageHeader, SegmentedBar, Stat } from "@/components/ui";
import RelancerButton from "@/components/RelancerButton";
import AdhesionsEnAttente from "@/components/AdhesionsEnAttente";
import ReglesAdhesionTontine from "@/components/ReglesAdhesionTontine";
import RoundsChart, { type RoundPoint } from "@/components/charts/RoundsChart";

/** "hors_bureau": 403, the account sits on no tontine's bureau; "aucune": 404, a gestionnaire with none yet. */
type Echec = "hors_bureau" | "aucune" | "erreur";

async function charger(token: string) {
  let echec: Echec | null = null;
  const tontine = await apiFetch<TontineDto>("/api/v1/dashboard/gestionnaire/tontine", { token }).catch((e: unknown) => {
    const status = e instanceof ApiError ? e.status : 0;
    echec = status === 403 ? "hors_bureau" : status === 404 ? "aucune" : "erreur";
    return null;
  });
  if (!tontine) return { tontine: null, versements: [] as VersementDto[], echec: echec ?? ("erreur" as Echec) };
  const page = await apiFetch<{ items: VersementDto[] }>(`/api/v1/tontines/${tontine.id}/versements?page=1&pageSize=100`, { token })
    .catch(() => ({ items: [] as VersementDto[] }));
  return { tontine, versements: page.items ?? [], echec: null };
}

const ordreStatut: Record<string, number> = { en_retard: 0, a_payer: 1, paye_partiel: 2, en_attente: 3, paye: 4, beneficiaire: 5, suspendu: 6 };

export default async function TableauDeBordTontine() {
  const token = (await cookies()).get("accessToken")?.value ?? "";
  const { tontine, versements, echec } = await charger(token);

  if (echec === "erreur") {
    return (
      <>
        <PageHeader eyebrow="Espace du bureau" title="Tableau de bord indisponible" />
        <div className="card"><EmptyState icon="alert" title="Impossible de charger la tontine">
          Le serveur n&apos;a pas répondu correctement. Réessayez dans un instant ; si le problème persiste, contactez l&apos;administrateur.
        </EmptyState></div>
      </>
    );
  }

  if (echec === "hors_bureau") {
    return (
      <>
        <PageHeader eyebrow="Espace du bureau" title="Accès réservé au bureau" />
        <div className="card"><EmptyState icon="shield" title="Vous n'êtes au bureau d'aucune tontine">
          {MESSAGE_HORS_BUREAU}
        </EmptyState></div>
      </>
    );
  }

  if (!tontine) {
    return (
      <>
        <PageHeader eyebrow="Tontine" title="Aucune tontine pour le moment" />
        <div className="card"><EmptyState icon="users" title="Créez votre tontine depuis l'application mobile">
          Une fois créée, elle apparaît ici avec ses tours, ses membres et ses paiements.
        </EmptyState></div>
      </>
    );
  }

  const membres = tontine.membres.filter((m) => m.statut !== "adhesion_en_attente");
  const demandes = tontine.membres.filter((m) => m.statut === "adhesion_en_attente");
  const tour = tontine.tours.find((t) => t.status === "Ouvert");
  const payeurs = membres.filter((m) => m.statut !== "beneficiaire" && m.statut !== "suspendu");
  const compte = (s: string) => payeurs.filter((m) => m.statut === s).length;
  const resteDuTotal = membres.reduce((a, m) => a + (m.resteDu ?? 0), 0);

  const confirmes = versements.filter((v) => v.status === "confirme");
  const collecteTour = tour ? confirmes.filter((v) => v.tourId === tour.id).reduce((a, v) => a + v.montant, 0) : 0;
  const attenduTour = payeurs.length * tontine.montantCotisation;

  // Expected per round = members who owed it (all except that round's beneficiary), approximated
  // with today's members; collected = validated payments recorded on that round.
  const rounds: RoundPoint[] = tontine.tours.map((t) => ({
    numero: t.numero,
    enCours: t.status === "Ouvert",
    collecte: confirmes.filter((v) => v.tourId === t.id).reduce((a, v) => a + v.montant, 0),
    attendu: Math.max(0, membres.filter((m) => m.statut !== "suspendu").length - 1) * tontine.montantCotisation,
  }));

  const gere = peutGerer(tontine);
  const roles = libelleRolesBureau(tontine);

  const lignes = [...membres].sort((a, b) => (ordreStatut[a.statut] ?? 9) - (ordreStatut[b.statut] ?? 9) || (a.rang ?? 0) - (b.rang ?? 0));

  return (
    <>
      <PageHeader
        eyebrow="Tontine"
        badge={<>
          <Badge status={tontine.status} label={libelleStatutGroupe(tontine.status)} />
          {roles && <span data-testid="role-bureau" className="inline-flex items-center rounded-full h-6 px-2.5 text-[12px] font-medium bg-brand-soft text-brand">Vous : {roles}</span>}
        </>}
        title={tontine.nom}
        subtitle={tontine.description}
        actions={<Link href="/gestionnaire/paiements" className="btn-primary"><Icon name="cash" />Encaisser une cotisation</Link>}
      >
        <p className="text-[13px] text-muted">
          {formatMontant(tontine.montantCotisation)} · {libellePeriodicite(tontine.periodicite).toLowerCase()} · {membres.length} / {tontine.maxMembres} membres
        </p>
      </PageHeader>

      <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">
        <Stat label="Tour en cours" value={tour ? `Tour ${tour.numero}` : "—"}
          context={tour?.beneficiaire ? `Bénéficiaire : ${tour.beneficiaire.nom}` : tontine.status === "Draft" ? "La tontine n'est pas encore activée" : "Aucun tour ouvert"} />
        <Stat label="Collecté sur ce tour" value={formatMontant(collecteTour)}
          meter={attenduTour > 0 ? (collecteTour / attenduTour) * 100 : 0}
          context={`sur ${formatMontant(attenduTour)} attendus`} />
        <Stat label="Membres à jour" value={`${compte("paye")} / ${payeurs.length}`}
          context={compte("en_attente") > 0 ? `${compte("en_attente")} paiement(s) à valider` : "Aucun paiement à valider"}
          tone={compte("en_attente") > 0 ? "warn" : undefined} />
        <Stat label="Reste dû (tours passés)" value={formatMontant(resteDuTotal)}
          context={resteDuTotal > 0 ? `${membres.filter((m) => (m.resteDu ?? 0) > 0).length} membre(s) concerné(s)` : "Aucun arriéré"}
          tone={resteDuTotal > 0 ? "bad" : "good"} />
      </div>

      {demandes.length > 0 && (
        <AdhesionsEnAttente tontineId={tontine.id} demandes={demandes} rattrapage={tontine.rattrapageAdhesionTardive ?? true} peutDecider={gere} />
      )}

      <Card title={tour ? `Tour ${tour.numero} : où en sont les membres` : "Membres"}
        subtitle={tour ? `Le bénéficiaire ne cotise pas pour son propre tour. Échéance du tour : ${tour.dateLimite ? formatDate(tour.dateLimite) : "—"}.` : undefined}
        bodyClassName="pt-4">
        {tour && (
          <div className="px-5 md:px-6 pb-5">
            <SegmentedBar total={payeurs.length} segments={[
              { key: "paye", label: "Payé", value: compte("paye"), className: "bg-good" },
              { key: "paye_partiel", label: "Payé partiellement", value: compte("paye_partiel"), className: "bg-serious" },
              { key: "en_attente", label: "À valider", value: compte("en_attente"), className: "bg-warn-mark", textClass: "text-[#0b0b0b]" },
              { key: "a_payer", label: "À payer", value: compte("a_payer"), className: "bg-chart-2", textClass: "text-ink" },
              { key: "en_retard", label: "En retard", value: compte("en_retard"), className: "bg-bad" },
            ]} />
          </div>
        )}
        <div className="overflow-x-auto border-t border-line">
          <table className="table">
            <thead><tr><th>Membre</th><th>Rang</th><th>Ce tour</th><th className="text-right">Reste dû</th><th className="text-right">Action</th></tr></thead>
            <tbody>
              {lignes.map((m: MembreDto) => (
                <tr key={m.id}>
                  <td>
                    <p className="font-medium text-ink">{m.nom}</p>
                    {m.telephone && <p className="text-[12px] text-muted num">{m.telephone}</p>}
                  </td>
                  <td className="num text-ink-2">{m.rang}</td>
                  <td><Badge status={m.statut} label={libelleStatutMembre(m)} /></td>
                  <td className="text-right num">
                    {(m.resteDu ?? 0) > 0
                      ? <span className="text-bad font-medium" title={`Tours ${m.toursImpayes?.join(", ")}`}>{formatMontant(m.resteDu ?? 0)}</span>
                      : <span className="text-muted">—</span>}
                    {(m.resteDu ?? 0) > 0 && <p className="text-[12px] text-muted">tours {m.toursImpayes?.join(", ")}</p>}
                  </td>
                  <td className="text-right">
                    {gere && doitSurLeTour(m, tontine.montantCotisation) && m.telephone
                      ? <RelancerButton membreId={m.id} membreNom={m.nom} tontineId={tontine.id} />
                      : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      <div className="grid grid-cols-1 xl:grid-cols-5 gap-6">
        <Card className="xl:col-span-3" title="Collecte par tour" subtitle="Paiements validés, comparés au montant attendu des membres actuels.">
          {rounds.length === 0
            ? <EmptyState icon="cash" title="Pas encore de tour">Activez la tontine pour ouvrir le premier tour.</EmptyState>
            : <RoundsChart data={rounds} />}
        </Card>
        <Card className="xl:col-span-2" title="Derniers paiements"
          actions={<Link href="/gestionnaire/paiements" className="btn-ghost btn-sm">Tout voir <Icon name="arrow" className="h-3.5 w-3.5" /></Link>}
          bodyClassName="pt-3">
          {versements.length === 0 ? (
            <EmptyState icon="receipt" title="Aucun paiement enregistré" />
          ) : (
            <ul className="divide-y divide-line">
              {versements.slice(0, 6).map((v) => (
                <li key={v.id} className="flex items-center justify-between gap-3 px-5 md:px-6 py-3">
                  <div className="min-w-0">
                    <p className="font-medium text-ink truncate">{v.payeurNom}</p>
                    <p className="text-[12px] text-muted">Tour {v.tourNumero ?? "—"} · {formatDate(v.dateCreation)}</p>
                  </div>
                  <div className="text-right space-y-1">
                    <p className="num text-ink">{formatMontant(v.montant)}</p>
                    <Badge status={v.status} />
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      <ReglesAdhesionTontine tontineId={tontine.id} mode={tontine.adhesionEnCours ?? "AvecValidation"}
        rattrapage={tontine.rattrapageAdhesionTardive ?? true} modifiable={gere} />
    </>
  );
}
