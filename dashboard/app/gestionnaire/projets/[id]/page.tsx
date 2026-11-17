"use client";

import { use, useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { formatMontant, formatDate, formatJour, libelleAction, libellePeriodicite, libelleStatutGroupe } from "@/lib/format";
import type { ProjetDetails, ReglesProjet } from "@/lib/types";
import { Badge, Card, EmptyState, Icon, Meter, PageHeader } from "@/components/ui";
import ReglesProjetForm from "@/components/ReglesProjetForm";
import CollecteChart, { type CollectePoint } from "@/components/charts/CollecteChart";
import { useTontineChoisie } from "@/components/Sidebar";
import { avecTontine } from "@/lib/tontine";

const CASE: Record<string, string> = { soldee: "bg-good", partielle: "bg-warn-mark", en_retard: "bg-bad", a_venir: "bg-surface-2 ring-1 ring-inset ring-line" };
const CASE_LIBELLE: Record<string, string> = { soldee: "soldée", partielle: "partielle", en_retard: "en retard", a_venir: "à venir" };

export default function ProjetPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const tontine = useTontineChoisie();
  const [p, setP] = useState<ProjetDetails | null>(null);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [code, setCode] = useState<string | null>(null);
  const [ouvert, setOuvert] = useState<string | null>(null);

  const [payeur, setPayeur] = useState("");
  const [montant, setMontant] = useState("");
  const [preuve, setPreuve] = useState("");
  const [libelle, setLibelle] = useState("");
  const [montantDepense, setMontantDepense] = useState("");
  const [justificatif, setJustificatif] = useState("");
  const [motif, setMotif] = useState("");
  const [aRejeter, setARejeter] = useState<{ type: "paiements" | "depenses"; id: string; nom: string } | null>(null);
  const [regles, setRegles] = useState<ReglesProjet | null>(null);
  const [toutVoir, setToutVoir] = useState(false);
  const [filtrePayeur, setFiltrePayeur] = useState("");
  const [inscription, setInscription] = useState(false);
  const [nomInscrit, setNomInscrit] = useState("");
  const [palierInscrit, setPalierInscrit] = useState("");

  const charger = useCallback(async () => {
    const res = await fetch(`/api/projets/${id}`);
    if (res.ok) setP(await res.json());
    else setMessage({ ok: false, text: (await res.json().catch(() => ({}))).error ?? "Projet introuvable." });
  }, [id]);

  useEffect(() => { charger(); }, [charger]);

  async function action(path: string, body: unknown, ok: string, method = "POST") {
    setBusy(true);
    setMessage(null);
    const res = await fetch(`/api/projets/${id}${path}`, { method, headers: { "Content-Type": "application/json" }, body: JSON.stringify(body ?? {}) });
    const data = await res.json().catch(() => ({}));
    setBusy(false);
    if (!res.ok) { setMessage({ ok: false, text: data.error ?? "Action impossible." }); return false; }
    if (data.code) setCode(data.code);
    if (data.id) setP(data); else await charger();
    setMessage({ ok: true, text: ok });
    return true;
  }

  // Cumulative expected (sum of every participant's instalments due by that date) vs cumulative
  // validated payments, per instalment date, plus today's point.
  const serie = useMemo<CollectePoint[]>(() => {
    if (!p) return [];
    const aujourdhui = new Date();
    const dates = [...p.echeancier];
    const iso = aujourdhui.toISOString();
    if (dates.length && new Date(dates[0]) < aujourdhui && new Date(dates.at(-1)!) > aujourdhui) dates.push(iso);
    dates.sort();
    const dues = p.participants.flatMap((x) => x.echeances ?? []);
    const valides = p.paiements.filter((x) => x.statut === "valide");
    return dates.map((d) => {
      const t = new Date(d);
      const fin = new Date(t); fin.setHours(23, 59, 59, 999);
      return {
        date: d,
        attendu: dues.filter((e) => new Date(e.date) <= t).reduce((a, e) => a + e.palier, 0),
        collecte: t <= aujourdhui ? valides.filter((x) => new Date(x.decideLe ?? x.saisiLe) <= fin).reduce((a, x) => a + x.montant, 0) : null,
      };
    });
  }, [p]);

  if (!p) {
    return message ? <div className="notice-err">{message.text}</div> : <p className="text-muted">Chargement du projet…</p>;
  }

  const demandes = p.participants.filter((x) => x.palierDemande !== null);
  const enAttente = p.paiements.filter((x) => x.statut === "en_attente").length;
  const depensesEnAttente = p.depenses.filter((x) => x.statut === "en_attente").length;
  const choisi = p.participants.find((x) => x.id === payeur);
  // Pending first, then most recent; long histories (imported registers) are paged.
  const paiementsFiltres = p.paiements
    .filter((x) => !filtrePayeur || x.participantId === filtrePayeur)
    .sort((a, b) => Number(b.statut === "en_attente") - Number(a.statut === "en_attente") || b.saisiLe.localeCompare(a.saisiLe));
  const listePaiements = toutVoir ? paiementsFiltres : paiementsFiltres.slice(0, 10);
  const modifiable = p.statut === "Brouillon" || p.statut === "Ouvert";

  function choisirPayeur(pid: string) {
    setPayeur(pid);
    const x = p!.participants.find((y) => y.id === pid);
    if (x) setMontant(String(x.resteDuExigible > 0 ? x.resteDuExigible : x.palierActuel));
  }

  return (
    <>
      <PageHeader
        eyebrow={<Link href={avecTontine("/gestionnaire/projets", tontine)} className="inline-flex items-center gap-1 hover:text-ink"><Icon name="back" className="h-3.5 w-3.5" />Projets collectifs</Link>}
        title={p.nom}
        badge={<Badge status={p.statut} label={libelleStatutGroupe(p.statut)} />}
        subtitle={p.description ?? undefined}
        actions={<>
          {modifiable && <button disabled={busy} onClick={() => action("/invitation?expirationJours=30", {}, "Code d'invitation généré, valable 30 jours.")} className="btn-secondary"><Icon name="key" />Code d&apos;invitation</button>}
          {p.statut === "Brouillon" && <button disabled={busy} onClick={() => action("/ouvrir", {}, "Projet ouvert : vous pouvez enregistrer des paiements.")} className="btn-primary">Ouvrir le projet</button>}
          {p.statut === "Ouvert" && <button disabled={busy} onClick={() => action("/clore", {}, "Projet clos.")} className="btn-secondary">Clore</button>}
        </>}
      >
        <p className="text-[13px] text-muted">
          Paliers {p.paliers.map((x) => formatMontant(x)).join(" · ")} · {p.nombreEcheances} échéances {libellePeriodicite(p.periodicite).toLowerCase()}s · à partir du {formatJour(p.dateDebut)}
        </p>
      </PageHeader>

      {message && <div data-testid="message" role="status" className={message.ok ? "notice-ok" : "notice-err"}>{message.text}</div>}
      {code && (
        <div className="card p-5 flex flex-wrap items-center gap-x-6 gap-y-2 border-brand/40">
          <span className="text-ink-2">Code à transmettre aux participants</span>
          <span data-testid="code" className="font-mono text-[26px] font-semibold tracking-[0.25em] text-brand">{code}</span>
          <span className="text-[12.5px] text-muted">Utilisable plusieurs fois pendant 30 jours. Chacun choisit son palier en rejoignant depuis l&apos;application.</span>
        </div>
      )}

      <div className="grid grid-cols-1 xl:grid-cols-[1.4fr_1fr] gap-4">
        <div className="card p-6 space-y-4">
          <div className="flex items-end justify-between gap-4">
            <div>
              <p className="text-[13px] text-ink-2">Collecté sur l&apos;objectif</p>
              <p className="text-[44px] leading-none font-semibold tracking-tight text-ink mt-2">{formatMontant(p.totalCollecte)}</p>
              <p className="text-[13px] text-muted mt-2">sur {formatMontant(p.objectif)}</p>
            </div>
            <p className="text-[22px] font-semibold text-brand">{p.progression} %</p>
          </div>
          <Meter value={p.progression} label="Progression vers l'objectif" />
        </div>
        <div className="grid grid-cols-2 gap-4">
          <div className="card p-5"><p className="text-[12.5px] text-ink-2">Solde disponible</p><p className="text-[19px] font-semibold mt-1 whitespace-nowrap">{formatMontant(p.solde)}</p></div>
          <div className="card p-5"><p className="text-[12.5px] text-ink-2">Dépenses validées</p><p className="text-[19px] font-semibold mt-1 whitespace-nowrap">{formatMontant(p.totalDepense)}</p></div>
          <div className="card p-5"><p className="text-[12.5px] text-ink-2">Participants</p><p className="text-[22px] font-semibold mt-1">{p.participants.length}</p></div>
          <div className="card p-5"><p className="text-[12.5px] text-ink-2">Journal</p>
            <p className={`mt-1 inline-flex items-center gap-1.5 text-[16px] font-semibold ${p.journalValide ? "text-good" : "text-bad"}`}>
              <Icon name={p.journalValide ? "shield" : "alert"} className="h-5 w-5" />{p.journalValide ? "Intègre" : "Altéré"}
            </p></div>
        </div>
      </div>

      {serie.length > 0 && p.participants.length > 0 && (
        <Card title="Collecte et engagements" subtitle="Cumul des paiements validés comparé à ce que les paliers prévoient, échéance par échéance.">
          <CollecteChart data={serie} />
        </Card>
      )}

      {demandes.length > 0 && (
        <Card className="border-info/40" title={`Changements de palier à valider (${demandes.length})`} subtitle="Les règles du projet soumettent ces changements à votre accord.">
          <ul className="divide-y divide-line" data-testid="demandes-palier">
            {demandes.map((x) => (
              <li key={x.id} className="flex flex-wrap items-center justify-between gap-3 py-3 first:pt-0 last:pb-0">
                <span className="text-ink"><span className="font-medium">{x.nom}</span> · {formatMontant(x.palierActuel)} <Icon name="arrow" className="inline h-3.5 w-3.5 text-muted" /> <span className="font-medium">{formatMontant(x.palierDemande!)}</span></span>
                <span className="flex gap-2">
                  <button disabled={busy} onClick={() => action(`/participants/${x.id}/changement-palier/refuser`, {}, `Changement refusé : ${x.nom} garde son palier.`)} className="btn-secondary btn-sm">Refuser</button>
                  <button disabled={busy} onClick={() => action(`/participants/${x.id}/changement-palier/approuver`, {}, `Changement approuvé pour ${x.nom}.`)} className="btn-primary btn-sm"><Icon name="check" className="h-3.5 w-3.5" />Approuver</button>
                </span>
              </li>
            ))}
          </ul>
        </Card>
      )}

      <Card title={`Participants (${p.participants.length})`} bodyClassName="pt-3"
        actions={modifiable ? <button onClick={() => setInscription((v) => !v)} className="btn-secondary btn-sm"><Icon name="plus" className="h-3.5 w-3.5" />Inscrire un participant</button> : undefined}
        subtitle="Triés par reste dû. Une case par échéance ; un trop-perçu se reporte sur les suivantes. Cliquez sur une ligne pour le détail.">
        {inscription && (
          <form className="mx-5 md:mx-6 mb-4 grid grid-cols-1 md:grid-cols-[1.5fr_1fr_auto_auto] gap-3 items-end rounded-lg border border-line bg-surface-2 p-3"
            onSubmit={async (e) => {
              e.preventDefault();
              if (await action("/participants", { nom: nomInscrit, palier: Number(palierInscrit) }, `${nomInscrit} est inscrit au projet.`)) {
                setNomInscrit(""); setPalierInscrit(""); setInscription(false);
              }
            }}>
            <div><label htmlFor="nom-inscrit" className="label">Nom du participant (sans compte dans l&apos;application)</label>
              <input id="nom-inscrit" value={nomInscrit} onChange={(e) => setNomInscrit(e.target.value)} required className="input" /></div>
            <div><label htmlFor="palier-inscrit" className="label">Palier</label>
              <select id="palier-inscrit" value={palierInscrit} onChange={(e) => setPalierInscrit(e.target.value)} required className="input">
                <option value="">Choisir</option>
                {p.paliers.map((x) => <option key={x} value={x}>{formatMontant(x)}</option>)}
              </select></div>
            <button type="submit" disabled={busy} className="btn-primary h-10">Inscrire</button>
            <button type="button" onClick={() => setInscription(false)} className="btn-ghost h-10">Annuler</button>
          </form>
        )}
        <ul className="flex flex-wrap gap-x-5 gap-y-1 px-5 md:px-6 pb-3 text-[12.5px] text-ink-2">
          {Object.entries(CASE_LIBELLE).map(([k, l]) => (
            <li key={k} className="inline-flex items-center gap-2"><span className={`h-3 w-3 rounded-[3px] ${CASE[k]}`} />{l}</li>
          ))}
        </ul>
        <div className="overflow-x-auto border-t border-line">
          <table className="table">
            <thead><tr><th>Participant</th><th className="text-right">Palier</th><th className="text-right">Payé</th><th className="text-right">Reste dû</th><th className="text-right">Avance</th><th>Échéances</th></tr></thead>
            <tbody>
              {p.participants.length === 0 && (
                <tr><td colSpan={6}><EmptyState icon="users" title="Aucun participant">Générez un code d&apos;invitation et partagez-le.</EmptyState></td></tr>
              )}
              {[...p.participants].sort((a, b) => b.resteDuExigible - a.resteDuExigible).map((x) => (
                <tr key={x.id} className="cursor-pointer" onClick={() => setOuvert(ouvert === x.id ? null : x.id)} aria-expanded={ouvert === x.id}>
                  <td className="font-medium text-ink whitespace-nowrap">{x.nom}
                    {x.palierDemande !== null && <span className="block text-[12px] text-info font-normal">demande {formatMontant(x.palierDemande)}</span>}</td>
                  <td className="text-right num whitespace-nowrap">{formatMontant(x.palierActuel)}</td>
                  <td className="text-right num whitespace-nowrap">{formatMontant(x.totalPaye)}</td>
                  <td className={`text-right num whitespace-nowrap ${x.resteDuExigible > 0 ? "text-bad font-medium" : "text-muted"}`}>{formatMontant(x.resteDuExigible)}</td>
                  <td className="text-right num whitespace-nowrap text-ink-2">{formatMontant(x.avance)}</td>
                  <td>
                    <div className="flex gap-[2px]">
                      {(x.echeances ?? []).map((e) => (
                        <span key={e.numero} title={`Échéance ${e.numero} · ${formatJour(e.date)} · ${formatMontant(e.paye)} / ${formatMontant(e.palier)} (${CASE_LIBELLE[e.statut]})`}
                          className={`h-5 ${(x.echeances?.length ?? 0) > 24 ? "w-2 rounded-[2px]" : "w-3 rounded-[3px]"} ${CASE[e.statut]}`} />
                      ))}
                    </div>
                    {ouvert === x.id && (
                      <table className="mt-3 text-[12.5px]">
                        <tbody>{(x.echeances ?? []).map((e) => (
                          <tr key={e.numero} className="text-ink-2">
                            <td className="pr-4 py-0.5 text-muted num">n°{e.numero}</td>
                            <td className="pr-4 py-0.5">{formatJour(e.date)}</td>
                            <td className="pr-4 py-0.5 num text-ink">{formatMontant(e.paye)} / {formatMontant(e.palier)}</td>
                            <td className="py-0.5">{CASE_LIBELLE[e.statut]}</td>
                          </tr>
                        ))}</tbody>
                      </table>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      <Card title="Paiements" subtitle={enAttente > 0 ? `${enAttente} paiement(s) attendent votre validation.` : "Enregistrez un versement reçu, puis validez-le après vérification."}>
        {p.statut !== "Ouvert" ? (
          <EmptyState icon="cash" title="Projet non ouvert">Ouvrez le projet pour enregistrer des paiements.</EmptyState>
        ) : (
          <form onSubmit={async (e) => {
              e.preventDefault();
              if (await action("/paiements", { participantId: payeur, montant: Number(montant), preuve }, "Paiement enregistré : il attend votre validation.")) {
                setPayeur(""); setMontant(""); setPreuve("");
              }
            }} className="grid grid-cols-1 md:grid-cols-[1.3fr_1fr_1.2fr_auto] gap-4 items-end">
            <div>
              <label htmlFor="payeur" className="label">Participant</label>
              <select id="payeur" name="payeur" value={payeur} onChange={(e) => choisirPayeur(e.target.value)} required className="input">
                <option value="">Choisir un participant</option>
                {p.participants.map((x) => <option key={x.id} value={x.id}>{x.nom}</option>)}
              </select>
            </div>
            <div>
              <label htmlFor="montant" className="label">Montant (FCFA)</label>
              <input id="montant" name="montant" type="number" min={1} value={montant} onChange={(e) => setMontant(e.target.value)} required className="input num" />
            </div>
            <div>
              <label htmlFor="preuve" className="label">Preuve</label>
              <input id="preuve" name="preuve" value={preuve} onChange={(e) => setPreuve(e.target.value)} required placeholder="N° du reçu…" className="input" />
            </div>
            <button type="submit" disabled={busy} className="btn-primary h-10"><Icon name="plus" />Enregistrer</button>
            {choisi && <p className="hint md:col-span-4 -mt-2">Palier {formatMontant(choisi.palierActuel)} · reste dû aujourd&apos;hui {formatMontant(choisi.resteDuExigible)} · avance {formatMontant(choisi.avance)}</p>}
          </form>
        )}

        {aRejeter && (
          <div className="mt-4 flex flex-wrap items-end gap-3 rounded-lg border border-bad/30 bg-bad-soft p-3">
            <div className="flex-1 min-w-[220px]">
              <label htmlFor="motif" className="label">Motif du rejet ({aRejeter.nom})</label>
              <input id="motif" name="motif" value={motif} onChange={(e) => setMotif(e.target.value)} autoFocus className="input" placeholder="Obligatoire" />
            </div>
            <button disabled={busy || !motif.trim()} onClick={async () => {
              if (await action(`/${aRejeter.type}/${aRejeter.id}/rejeter`, { motif }, "Rejeté. Le motif est inscrit au journal.")) { setARejeter(null); setMotif(""); }
            }} className="btn-danger h-10">Confirmer le rejet</button>
            <button onClick={() => { setARejeter(null); setMotif(""); }} className="btn-ghost h-10">Annuler</button>
          </div>
        )}

        {p.paiements.length > 0 && (
          <>
          <div className="mt-6 flex flex-wrap items-center justify-between gap-3">
            <p className="text-[13px] text-ink-2">
              {listePaiements.length < paiementsFiltres.length
                ? `${listePaiements.length} sur ${paiementsFiltres.length} paiements`
                : `${paiementsFiltres.length} paiement(s)`}{filtrePayeur ? " pour ce participant" : ""}
            </p>
            <select aria-label="Filtrer par participant" value={filtrePayeur} onChange={(e) => setFiltrePayeur(e.target.value)} className="input h-9 w-64">
              <option value="">Tous les participants</option>
              {p.participants.map((x) => <option key={x.id} value={x.id}>{x.nom}</option>)}
            </select>
          </div>
          <div className="overflow-x-auto mt-3 -mx-5 md:-mx-6 border-t border-line">
            <table className="table">
              <thead><tr><th>Participant</th><th className="text-right">Montant</th><th>Preuve</th><th>Statut</th><th>Saisi le</th><th className="text-right">Actions</th></tr></thead>
              <tbody>{listePaiements.map((x) => (
                <tr key={x.id}>
                  <td className="font-medium text-ink">{x.participantNom}</td>
                  <td className="text-right num">{formatMontant(x.montant)}</td>
                  <td className="text-ink-2">{x.preuve}</td>
                  <td><Badge status={x.statut} /></td>
                  <td className="text-ink-2 whitespace-nowrap">{formatDate(x.saisiLe)}</td>
                  <td className="text-right whitespace-nowrap">
                    {x.statut === "en_attente" ? (
                      <span className="inline-flex gap-2">
                        <button disabled={busy} onClick={() => setARejeter({ type: "paiements", id: x.id, nom: x.participantNom })} className="btn-secondary btn-sm">Rejeter</button>
                        <button disabled={busy} onClick={() => action(`/paiements/${x.id}/valider`, {}, `Paiement de ${x.participantNom} validé.`)} className="btn-primary btn-sm"><Icon name="check" className="h-3.5 w-3.5" />Valider</button>
                      </span>
                    ) : x.statut === "rejete" ? <span className="text-[12px] text-muted">{x.motif}</span>
                      : <span className="text-[12px] text-muted font-mono">{x.reference}</span>}
                  </td>
                </tr>
              ))}</tbody>
            </table>
          </div>
          {paiementsFiltres.length > listePaiements.length && (
            <div className="-mx-5 md:-mx-6 -mb-5 md:-mb-6 border-t border-line px-5 md:px-6 py-3">
              <button onClick={() => setToutVoir(true)} className="btn-ghost btn-sm">Afficher les {paiementsFiltres.length} paiements</button>
            </div>
          )}
          </>
        )}
      </Card>

      <Card title="Dépenses" subtitle={`Une dépense ne peut pas dépasser le solde disponible (${formatMontant(p.solde)}).${depensesEnAttente ? ` ${depensesEnAttente} à valider.` : ""}`}>
        {p.statut === "Ouvert" && (
          <form onSubmit={async (e) => {
              e.preventDefault();
              if (await action("/depenses", { libelle, montant: Number(montantDepense), justificatif }, "Dépense proposée : validez-la après vérification du justificatif.")) {
                setLibelle(""); setMontantDepense(""); setJustificatif("");
              }
            }} className="grid grid-cols-1 md:grid-cols-[1.3fr_1fr_1.2fr_auto] gap-4 items-end">
            <div><label htmlFor="libelle" className="label">Libellé</label><input id="libelle" name="libelle" value={libelle} onChange={(e) => setLibelle(e.target.value)} required className="input" placeholder="Ex. : acompte moteur" /></div>
            <div><label htmlFor="montant-depense" className="label">Montant (FCFA)</label><input id="montant-depense" name="montant-depense" type="number" min={1} value={montantDepense} onChange={(e) => setMontantDepense(e.target.value)} required className="input num" /></div>
            <div><label htmlFor="justificatif" className="label">Justificatif</label><input id="justificatif" name="justificatif" value={justificatif} onChange={(e) => setJustificatif(e.target.value)} required className="input" placeholder="N° de facture, devis…" /></div>
            <button type="submit" disabled={busy} className="btn-secondary h-10"><Icon name="plus" />Proposer</button>
          </form>
        )}
        {p.depenses.length === 0 ? (
          <EmptyState icon="receipt" title="Aucune dépense" />
        ) : (
          <div className="overflow-x-auto mt-5 -mx-5 md:-mx-6 border-t border-line">
            <table className="table">
              <thead><tr><th>Libellé</th><th className="text-right">Montant</th><th>Justificatif</th><th>Statut</th><th className="text-right">Actions</th></tr></thead>
              <tbody>{p.depenses.map((d) => (
                <tr key={d.id}>
                  <td className="font-medium text-ink">{d.libelle}</td>
                  <td className="text-right num">{formatMontant(d.montant)}</td>
                  <td className="text-ink-2">{d.justificatif}</td>
                  <td><Badge status={d.statut} /></td>
                  <td className="text-right whitespace-nowrap">
                    {d.statut === "en_attente" ? (
                      <span className="inline-flex gap-2">
                        <button disabled={busy} onClick={() => setARejeter({ type: "depenses", id: d.id, nom: d.libelle })} className="btn-secondary btn-sm">Rejeter</button>
                        <button disabled={busy} onClick={() => action(`/depenses/${d.id}/valider`, {}, `Dépense « ${d.libelle} » validée.`)} className="btn-primary btn-sm"><Icon name="check" className="h-3.5 w-3.5" />Valider</button>
                      </span>
                    ) : d.statut === "rejete" ? <span className="text-[12px] text-muted">{d.motif}</span> : null}
                  </td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        )}
      </Card>

      <Card title="Règles du projet" id="regles"
        subtitle="Chaque modification est inscrite au journal. Un changement de palier déjà fait garde la règle en vigueur au moment de sa demande."
        actions={modifiable ? (regles === null
          ? <button onClick={() => setRegles(p.regles)} className="btn-secondary btn-sm"><Icon name="settings" className="h-3.5 w-3.5" />Modifier</button>
          : <span className="flex gap-2">
              <button onClick={() => setRegles(null)} className="btn-ghost btn-sm">Annuler</button>
              <button disabled={busy} onClick={async () => { if (await action("/regles", regles, "Règles enregistrées.", "PUT")) setRegles(null); }} className="btn-primary btn-sm">Enregistrer</button>
            </span>) : undefined}>
        <div data-testid="regles"><ReglesProjetForm value={regles ?? p.regles} onChange={setRegles} disabled={regles === null} /></div>
      </Card>

      {p.journal && (
        <Card title="Journal du projet" subtitle={`${p.journal.length} dernières actions, chaînées par empreinte SHA-256.`} bodyClassName="pt-2">
          <ol className="divide-y divide-line">
            {p.journal.slice(0, 12).map((j) => (
              <li key={j.sequence} className="flex items-center gap-4 px-5 md:px-6 py-2.5 text-[13px]">
                <span className="w-10 text-muted num">#{j.sequence}</span>
                <span className="flex-1 text-ink">{libelleAction(j.action)}</span>
                <span className="hidden md:inline font-mono text-[12px] text-muted">{j.hash.slice(0, 12)}…</span>
                <time className="text-muted whitespace-nowrap">{formatDate(j.horodatage)}</time>
              </li>
            ))}
          </ol>
        </Card>
      )}
    </>
  );
}
