"use client";

import { useState, useEffect, useCallback } from "react";
import { formatMontant, formatDate } from "@/lib/format";
import type { TontineDto, VersementDto } from "@/lib/types";
import { Badge, Card, EmptyState, Icon, PageHeader } from "@/components/ui";
import Pagination from "@/components/Pagination";

interface PaiementsResponse { items: VersementDto[]; totalCount: number; page: number; pageSize: number; totalPages: number }

const STATUTS_A_ENCAISSER = new Set(["a_payer", "en_retard"]);
type Filtre = "tous" | "en_attente" | "confirme" | "rejete";

export default function PaiementsPage() {
  const [paiements, setPaiements] = useState<VersementDto[]>([]);
  const [tontine, setTontine] = useState<TontineDto | null>(null);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(true);
  const [message, setMessage] = useState<{ type: "ok" | "err"; text: string } | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [filtre, setFiltre] = useState<Filtre>("tous");

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
      const [resP, resT] = await Promise.all([fetch(`/api/paiements?page=${p}&pageSize=${pageSize}`), fetch(`/api/tontine`)]);
      if (resP.ok) {
        const data: PaiementsResponse = await resP.json();
        setPaiements(data.items ?? []);
        setTotalPages(data.totalPages);
      }
      if (resT.ok) {
        const t: TontineDto = await resT.json();
        setTontine(t);
        setMontant((m) => m || String(t.montantCotisation));
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { fetchAll(page); }, [page, fetchAll]);

  const tourOuvert = tontine?.tours.find((t) => t.status === "Ouvert");
  const membresAEncaisser = tontine?.membres.filter((m) => STATUTS_A_ENCAISSER.has(m.statut) || (m.resteDu ?? 0) > 0) ?? [];
  const membreChoisi = tontine?.membres.find((m) => m.id === membreId);
  const toursPossibles = membreChoisi
    ? [
        ...(membreChoisi.toursImpayes ?? []).map((n) => {
          const t = tontine!.tours.find((x) => x.numero === n);
          return t ? { id: t.id, label: `Tour ${n} · rattrapage` } : null;
        }),
        ...(tourOuvert && STATUTS_A_ENCAISSER.has(membreChoisi.statut) ? [{ id: tourOuvert.id, label: `Tour ${tourOuvert.numero} · en cours` }] : []),
      ].filter((x): x is { id: string; label: string } => x !== null)
    : [];

  function choisirMembre(id: string) {
    setMembreId(id);
    const m = tontine?.membres.find((x) => x.id === id);
    const premierImpaye = m?.toursImpayes?.[0];
    const t = premierImpaye !== undefined ? tontine?.tours.find((x) => x.numero === premierImpaye) : tourOuvert;
    setTourId(t?.id ?? "");
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
      setMessage({ type: "ok", text: `Paiement de ${membreChoisi?.nom} enregistré. Il attend votre validation ci-dessous.` });
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

  const aValider = paiements.filter((p) => p.status === "en_attente").length;
  const visibles = filtre === "tous" ? paiements : paiements.filter((p) => p.status === filtre);

  return (
    <>
      <PageHeader eyebrow="Tontine" title="Paiements"
        subtitle="Enregistrez les cotisations remises en espèces, puis validez-les après avoir vérifié la preuve. Saisie et validation sont deux étapes tracées dans le journal." />

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
                    {m.nom}{m.statut === "en_retard" ? " · en retard" : ""}{(m.resteDu ?? 0) > 0 ? ` · reste dû ${formatMontant(m.resteDu ?? 0)}` : ""}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label htmlFor="tour" className="label">Tour</label>
              <select id="tour" name="tour" value={tourId} onChange={(e) => setTourId(e.target.value)} required className="input" disabled={!membreChoisi}>
                <option value="">{membreChoisi ? "Choisir le tour" : "Selon le membre"}</option>
                {toursPossibles.map((t) => <option key={t.id} value={t.id}>{t.label}</option>)}
              </select>
            </div>
            <div>
              <label htmlFor="montant" className="label">Montant (FCFA)</label>
              <input id="montant" name="montant" type="number" min={100} value={montant} onChange={(e) => setMontant(e.target.value)} required className="input num" />
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
        subtitle={aValider > 0 ? `${aValider} paiement(s) attendent votre validation.` : "Tous les paiements saisis ont été traités."}
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
                      {p.status === "en_attente" && (
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
