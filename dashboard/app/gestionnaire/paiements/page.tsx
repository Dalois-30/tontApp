"use client";

import { useState, useEffect, useCallback, useRef } from "react";
import { formatMontant, formatDate, resteTourOuvert, doitSurLeTour, libelleStatutMembre, peutValider, libelleRolesBureau, MESSAGE_HORS_BUREAU } from "@/lib/format";
import type { TontineDto, VersementDto } from "@/lib/types";
import { Badge, Card, EmptyState, Icon, PageHeader } from "@/components/ui";
import Pagination from "@/components/Pagination";

interface PaiementsResponse { items: VersementDto[]; totalCount: number; page: number; pageSize: number; totalPages: number }

type Filtre = "tous" | "en_attente" | "confirme" | "rejete";

/** Outcome of the last load of the tontine: 403 → hors_bureau, 404 → aucune, other failure → erreur. */
type Etat = "chargement" | "ok" | "hors_bureau" | "aucune" | "erreur";

export default function PaiementsPage() {
  const [paiements, setPaiements] = useState<VersementDto[]>([]);
  const [tontine, setTontine] = useState<TontineDto | null>(null);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(true);
  const [message, setMessage] = useState<{ type: "ok" | "err"; text: string } | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [filtre, setFiltre] = useState<Filtre>("tous");
  const [etat, setEtat] = useState<Etat>("chargement");
  // Once known, the tontine is pinned so that it and its payments always come from the same tontine.
  const tontineId = useRef<string | null>(null);

  const [membreId, setMembreId] = useState("");
  const [tourId, setTourId] = useState("");
  const [montant, setMontant] = useState("");
  const [preuve, setPreuve] = useState("");
  const [rejet, setRejet] = useState<{ id: string; nom: string } | null>(null);
  const [motif, setMotif] = useState("");

  const pageSize = 20;

  const fetchAll = useCallback(async (p: number) => {
    setLoading(true);
    try {
      // The tontine first, then the payments of that very tontine.
      const resT = await fetch(tontineId.current ? `/api/tontine?tontineId=${tontineId.current}` : "/api/tontine");
      if (!resT.ok) {
        setEtat(resT.status === 403 ? "hors_bureau" : resT.status === 404 ? "aucune" : "erreur");
        return;
      }
      const t: TontineDto = await resT.json();
      tontineId.current = t.id;
      setTontine(t);
      setMontant((m) => m || String(t.montantCotisation));

      const resP = await fetch(`/api/paiements?page=${p}&pageSize=${pageSize}&tontineId=${t.id}`);
      if (!resP.ok) { setEtat("erreur"); return; }
      const data: PaiementsResponse = await resP.json();
      setPaiements(data.items ?? []);
      setTotalPages(data.totalPages);
      setEtat("ok");
    } catch {
      setEtat("erreur");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { fetchAll(page); }, [page, fetchAll]);

  const tourOuvert = tontine?.tours.find((t) => t.status === "Ouvert");
  const cotisation = tontine?.montantCotisation ?? 0;
  // A partly paid (or partly pending) round stays collectable for its rest, like an unpaid one.
  const membresAEncaisser = tontine?.membres.filter((m) => doitSurLeTour(m, cotisation) || (m.resteDu ?? 0) > 0) ?? [];
  const membreChoisi = tontine?.membres.find((m) => m.id === membreId);
  const libelleReste = (reste: number) => (reste < cotisation ? ` · reste ${formatMontant(reste)}` : "");
  /** Rounds that can be collected for a member (arrears first, then the open round), with what remains on each. */
  const toursDe = (m: TontineDto["membres"][number] | undefined) => m
    ? [
        ...(m.toursImpayes ?? []).map((n, i) => {
          const t = tontine!.tours.find((x) => x.numero === n);
          const reste = m.restesImpayes?.[i] ?? cotisation;
          return t ? { id: t.id, label: `Tour ${n} · rattrapage${libelleReste(reste)}`, reste } : null;
        }),
        ...(tourOuvert && doitSurLeTour(m, cotisation)
          ? [{ id: tourOuvert.id, label: `Tour ${tourOuvert.numero} · en cours${libelleReste(resteTourOuvert(m, cotisation))}`, reste: resteTourOuvert(m, cotisation) }]
          : []),
      ].filter((x): x is { id: string; label: string; reste: number } => x !== null)
    : [];
  const toursPossibles = toursDe(membreChoisi);

  /** Selects a round and proposes what remains on it (the full contribution, or the rest of a partial payment). */
  function choisirTour(id: string) {
    setTourId(id);
    const t = toursPossibles.find((x) => x.id === id);
    if (t) setMontant(String(t.reste));
  }

  /** Pre-selects the member's first collectable round and proposes its remainder; nothing due → no round, no amount. */
  function choisirMembre(id: string) {
    setMembreId(id);
    const premier = toursDe(tontine?.membres.find((x) => x.id === id))[0];
    setTourId(premier?.id ?? "");
    setMontant(premier && premier.reste > 0 ? String(premier.reste) : "");
  }

  async function enregistrer(e: React.FormEvent) {
    e.preventDefault();
    if (!tontine || !tourId || !membreId) return;
    setBusy("form");
    setMessage(null);
    const res = await fetch("/api/versements/manuel", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ tontineId: tontine.id, tourId, membreId, montant: Number(montant), descriptionPreuve: preuve }),
    });
    const data = await res.json().catch(() => ({}));
    setBusy(null);
    if (res.ok) {
      setMessage({ type: "ok", text: `Paiement de ${membreChoisi?.nom} enregistré. Il attend ${valide ? "votre validation ci-dessous" : "la validation du trésorier"}.` });
      setMembreId(""); setTourId(""); setPreuve(""); setFiltre("tous");
      fetchAll(page);
    } else {
      setMessage({ type: "err", text: data.error ?? "Échec de l'enregistrement." });
    }
  }

  async function agir(id: string, action: "valider" | "rejeter", raison?: string) {
    setBusy(id);
    setMessage(null);
    const res = await fetch(`/api/versements/${id}/${action}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(action === "rejeter" ? { raison } : {}),
    });
    const data = await res.json().catch(() => ({}));
    setBusy(null);
    if (res.ok) {
      setMessage({ type: "ok", text: action === "valider" ? `Paiement validé · référence ${data.reference}` : "Paiement rejeté. Le membre redevient « à payer »." });
      setRejet(null); setMotif("");
      fetchAll(page);
    } else {
      setMessage({ type: "err", text: data.error ?? "Action impossible." });
    }
  }

  // Validating is the trésorier's (or an admin's); the API refuses anyone else.
  const valide = peutValider(tontine);
  const roles = libelleRolesBureau(tontine);
  const aValider = paiements.filter((p) => p.status === "en_attente").length;

  if (etat === "hors_bureau" || etat === "aucune" || etat === "erreur") {
    const [icone, titre, texte] = etat === "hors_bureau"
      ? ["shield", "Vous n'êtes au bureau d'aucune tontine", MESSAGE_HORS_BUREAU]
      : etat === "aucune"
        ? ["users", "Aucune tontine pour le moment", "Créez votre tontine depuis l'application mobile : ses paiements apparaîtront ici."]
        : ["alert", "Impossible de charger les paiements", "Le serveur n'a pas répondu correctement. Réessayez dans un instant."];
    return (
      <>
        <PageHeader eyebrow="Espace du bureau" title="Paiements" />
        <div className="card"><EmptyState icon={icone} title={titre}>{texte}</EmptyState></div>
      </>
    );
  }
  // Role-dependent wording only once the tontine (and so the caller's roles) is known.
  const pret = etat === "ok";
  const visibles = filtre === "tous" ? paiements : paiements.filter((p) => p.status === filtre);

  return (
    <>
      <PageHeader eyebrow="Tontine" title="Paiements"
        badge={pret && roles ? <span data-testid="role-bureau" className="inline-flex items-center rounded-full h-6 px-2.5 text-[12px] font-medium bg-brand-soft text-brand">Vous : {roles}</span> : undefined}
        subtitle={!pret ? undefined : valide
          ? "Enregistrez les cotisations remises en espèces, puis validez-les après avoir vérifié la preuve. Saisie et validation sont deux étapes tracées dans le journal."
          : "Enregistrez les cotisations remises en espèces. Le trésorier de la tontine les valide après avoir vérifié la preuve ; chaque étape est tracée dans le journal."} />

      {message && <div data-testid="message" role="status" className={message.type === "ok" ? "notice-ok" : "notice-err"}>{message.text}</div>}

      <Card title="Encaisser une cotisation"
        subtitle={tourOuvert ? `Tour ${tourOuvert.numero} en cours. Les tours passés impayés sont proposés en rattrapage.` : undefined}>
        {!tourOuvert ? (
          <EmptyState icon="cash" title="Aucun tour ouvert">Activez la tontine pour commencer à encaisser.</EmptyState>
        ) : membresAEncaisser.length === 0 ? (
          <EmptyState icon="check" title="Tout le monde est à jour">Aucune cotisation n&apos;est attendue pour le moment.</EmptyState>
        ) : (
          <form onSubmit={enregistrer} className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-[1.3fr_1.1fr_0.8fr_1.1fr_auto] gap-4 items-end">
            <div>
              <label htmlFor="membre" className="label">Membre</label>
              <select id="membre" name="membre" value={membreId} onChange={(e) => choisirMembre(e.target.value)} required className="input">
                <option value="">Choisir un membre</option>
                {membresAEncaisser.map((m) => (
                  <option key={m.id} value={m.id}>
                    {m.nom}{m.statut === "en_retard" ? " · en retard" : ""}{libelleStatutMembre(m) ? ` · ${libelleStatutMembre(m)}` : ""}{(m.resteDu ?? 0) > 0 ? ` · reste dû ${formatMontant(m.resteDu ?? 0)}` : ""}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label htmlFor="tour" className="label">Tour</label>
              <select id="tour" name="tour" value={tourId} onChange={(e) => choisirTour(e.target.value)} required className="input" disabled={!membreChoisi}>
                <option value="">{membreChoisi ? "Choisir le tour" : "Selon le membre"}</option>
                {toursPossibles.map((t) => <option key={t.id} value={t.id}>{t.label}</option>)}
              </select>
            </div>
            <div>
              <label htmlFor="montant" className="label">Montant (FCFA)</label>
              <input id="montant" name="montant" type="number" min={1} value={montant} onChange={(e) => setMontant(e.target.value)} required className="input num" />
            </div>
            <div>
              <label htmlFor="preuve" className="label">Preuve</label>
              <input id="preuve" name="preuve" value={preuve} onChange={(e) => setPreuve(e.target.value)} required placeholder="N° du reçu, du bordereau…" className="input" />
            </div>
            <button type="submit" disabled={busy === "form"} className="btn-primary h-10"><Icon name="plus" />Enregistrer</button>
          </form>
        )}
      </Card>

      <Card title="Historique des paiements"
        subtitle={!pret ? undefined : aValider > 0 ? `${aValider} paiement(s) attendent ${valide ? "votre validation" : "la validation du trésorier"}.` : "Tous les paiements saisis ont été traités."}
        actions={
          <div className="inline-flex rounded-lg border border-line p-0.5 bg-surface-2" role="tablist" aria-label="Filtrer par statut">
            {([["tous", "Tous"], ["en_attente", "À valider"], ["confirme", "Validés"], ["rejete", "Rejetés"]] as [Filtre, string][]).map(([k, l]) => (
              <button key={k} role="tab" aria-selected={filtre === k} onClick={() => setFiltre(k)}
                className={`h-7 px-3 rounded-md text-[12.5px] font-medium ${filtre === k ? "bg-surface text-ink shadow-sm" : "text-ink-2 hover:text-ink"}`}>{l}</button>
            ))}
          </div>
        }
        bodyClassName="pt-4">
        {rejet && (
          <div className="mx-5 md:mx-6 mb-4 flex flex-wrap items-end gap-3 rounded-lg border border-bad/30 bg-bad-soft p-3">
            <div className="flex-1 min-w-[220px]">
              <label htmlFor="motif" className="label">Motif du rejet du paiement de {rejet.nom}</label>
              <input id="motif" name="motif" value={motif} onChange={(e) => setMotif(e.target.value)} autoFocus className="input" placeholder="Ex. : reçu illisible" />
            </div>
            <button disabled={!motif.trim() || busy === rejet.id} onClick={() => agir(rejet.id, "rejeter", motif)} className="btn-danger h-10">Confirmer le rejet</button>
            <button onClick={() => { setRejet(null); setMotif(""); }} className="btn-ghost h-10">Annuler</button>
          </div>
        )}
        <div className="overflow-x-auto border-t border-line">
          <table className="table">
            <thead><tr><th>Payeur</th><th>Tour</th><th className="text-right">Montant</th><th>Statut</th><th>Référence</th><th>Saisi le</th><th className="text-right">Actions</th></tr></thead>
            <tbody>
              {loading ? (
                <tr><td colSpan={7} className="text-center text-muted py-8">Chargement…</td></tr>
              ) : visibles.length === 0 ? (
                <tr><td colSpan={7}><EmptyState icon="receipt" title="Aucun paiement dans cette vue" /></td></tr>
              ) : (
                visibles.map((p) => (
                  <tr key={p.id}>
                    <td className="font-medium text-ink">{p.payeurNom}</td>
                    <td className="text-ink-2 num">{p.tourNumero ?? "—"}</td>
                    <td className="text-right num">{formatMontant(p.montant)}</td>
                    <td><Badge status={p.status} /></td>
                    <td className="text-[12px] text-muted font-mono">{p.reference ?? "—"}</td>
                    <td className="text-ink-2 whitespace-nowrap">{formatDate(p.dateCreation)}</td>
                    <td className="text-right whitespace-nowrap">
                      {valide && p.status === "en_attente" && (
                        <span className="inline-flex gap-2">
                          <button onClick={() => setRejet({ id: p.id, nom: p.payeurNom })} disabled={busy === p.id} className="btn-secondary btn-sm">Rejeter</button>
                          <button onClick={() => agir(p.id, "valider")} disabled={busy === p.id} className="btn-primary btn-sm"><Icon name="check" className="h-3.5 w-3.5" />Valider</button>
                        </span>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        <div className="px-5 md:px-6 border-t border-line">
          <Pagination page={page} totalPages={totalPages} onPageChange={setPage} />
        </div>
      </Card>
    </>
  );
}
