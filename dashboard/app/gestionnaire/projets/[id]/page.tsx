"use client";

import { use, useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { formatMontant, formatDate } from "@/lib/format";
import type { ParticipantProjet, ProjetDetails } from "@/lib/types";
import StatusBadge from "@/components/StatusBadge";

const ECHEANCE_STYLE: Record<string, string> = {
  soldee: "bg-green-600",
  partielle: "bg-amber-400",
  en_retard: "bg-red-600",
  a_venir: "bg-gray-200",
};

const ECHEANCE_LABEL: Record<string, string> = {
  soldee: "soldée",
  partielle: "partielle",
  en_retard: "en retard",
  a_venir: "à venir",
};

export default function ProjetPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
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
  const [aRejeter, setARejeter] = useState<{ type: "paiements" | "depenses"; id: string } | null>(null);

  const charger = useCallback(async () => {
    const res = await fetch(`/api/projets/${id}`);
    if (res.ok) setP(await res.json());
    else setMessage({ ok: false, text: (await res.json().catch(() => ({}))).error ?? "Projet introuvable." });
  }, [id]);

  useEffect(() => {
    charger();
  }, [charger]);

  async function action(path: string, body: unknown, ok: string, method = "POST") {
    setBusy(true);
    setMessage(null);
    const res = await fetch(`/api/projets/${id}${path}`, {
      method,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body ?? {}),
    });
    const data = await res.json().catch(() => ({}));
    setBusy(false);
    if (!res.ok) {
      setMessage({ ok: false, text: data.error ?? "Action impossible." });
      return false;
    }
    if (data.code) setCode(data.code);
    if (data.id) setP(data);
    else await charger();
    setMessage({ ok: true, text: ok });
    return true;
  }

  if (!p) {
    return <div className="max-w-6xl text-sm text-gray-500">{message?.text ?? "Chargement..."}</div>;
  }

  const enAttente = p.paiements.filter((x) => x.statut === "en_attente");
  const depensesEnAttente = p.depenses.filter((x) => x.statut === "en_attente");
  const baisses = p.participants.filter((x) => x.baisseDemandee !== null);
  const choisi = p.participants.find((x) => x.id === payeur);

  function choisirPayeur(pid: string) {
    setPayeur(pid);
    const x = p!.participants.find((y) => y.id === pid);
    if (x) setMontant(String(x.resteDuExigible > 0 ? x.resteDuExigible : x.palierActuel));
  }

  return (
    <div className="max-w-6xl space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <Link href="/gestionnaire/projets" className="text-sm text-gray-500 hover:underline">← Projets</Link>
          <h1 className="text-2xl font-bold text-gray-900 mt-1">{p.nom}</h1>
          {p.description && <p className="text-gray-500 mt-1">{p.description}</p>}
          <p className="text-sm text-gray-500 mt-1">
            {p.statut} · paliers {p.paliers.map((x) => formatMontant(x)).join(" / ")} · {p.nombreEcheances} échéances
            ({p.periodicite === "Monthly" ? "mensuelles" : p.periodicite === "Weekly" ? "hebdomadaires" : "bimensuelles"})
            à partir du {new Date(p.dateDebut).toLocaleDateString("fr-FR")}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          {p.statut === "Brouillon" && (
            <button disabled={busy} onClick={() => action("/ouvrir", {}, "Projet ouvert : les paiements peuvent être enregistrés.")}
              className="bg-green-700 hover:bg-green-800 text-white text-sm font-medium rounded-md px-4 py-2 disabled:opacity-50">
              Ouvrir le projet
            </button>
          )}
          {(p.statut === "Brouillon" || p.statut === "Ouvert") && (
            <button disabled={busy} onClick={() => action("/invitation?expirationJours=30", {}, "Code d'invitation généré (30 jours).")}
              className="border border-gray-300 hover:border-green-700 text-sm font-medium rounded-md px-4 py-2 disabled:opacity-50">
              Code d&apos;invitation
            </button>
          )}
          {p.statut === "Ouvert" && (
            <button disabled={busy} onClick={() => action("/clore", {}, "Projet clos.")}
              className="border border-gray-300 hover:border-red-500 text-sm font-medium rounded-md px-4 py-2 disabled:opacity-50">
              Clore
            </button>
          )}
        </div>
      </div>

      {message && (
        <div data-testid="message" className={`px-4 py-3 rounded-md text-sm border ${message.ok ? "bg-green-50 border-green-200 text-green-800" : "bg-red-50 border-red-200 text-red-800"}`}>
          {message.text}
        </div>
      )}
      {code && (
        <div className="bg-white border border-green-300 rounded-lg p-4 flex flex-wrap items-center gap-4">
          <span className="text-sm text-gray-600">Code à partager avec les participants :</span>
          <span className="font-mono text-2xl font-bold tracking-widest text-green-800" data-testid="code">{code}</span>
          <span className="text-xs text-gray-500">Valable 30 jours, utilisable plusieurs fois. Chacun choisit son palier en rejoignant.</span>
        </div>
      )}

      {/* ── Chiffres clés ─────────────────────────────────────────── */}
      <div className="bg-white rounded-lg shadow border border-gray-200 p-6 space-y-4">
        <div className="flex flex-wrap items-end justify-between gap-2">
          <div>
            <p className="text-sm text-gray-500">Collecté sur l&apos;objectif</p>
            <p className="text-3xl font-bold tabular-nums">
              {formatMontant(p.totalCollecte)} <span className="text-base font-normal text-gray-500">/ {formatMontant(p.objectif)}</span>
            </p>
          </div>
          <p className="text-lg font-semibold text-green-800 tabular-nums">{p.progression}%</p>
        </div>
        <div className="h-3 bg-gray-200 rounded-full overflow-hidden">
          <div className="h-full bg-green-700" style={{ width: `${Math.min(100, p.progression)}%` }} />
        </div>
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
          <div><p className="text-gray-500">Participants</p><p className="text-lg font-semibold tabular-nums">{p.participants.length}</p></div>
          <div><p className="text-gray-500">Dépenses validées</p><p className="text-lg font-semibold tabular-nums">{formatMontant(p.totalDepense)}</p></div>
          <div><p className="text-gray-500">Solde disponible</p><p className="text-lg font-semibold tabular-nums">{formatMontant(p.solde)}</p></div>
          <div><p className="text-gray-500">Journal</p>
            <p className={`text-lg font-semibold ${p.journalValide ? "text-green-700" : "text-red-700"}`}>
              {p.journalValide ? "✔ intègre" : "✖ altéré"}
            </p></div>
        </div>
      </div>

      {/* ── Baisses de palier à approuver ─────────────────────────── */}
      {baisses.length > 0 && (
        <div className="bg-white rounded-lg shadow p-6 border border-blue-200" data-testid="baisses">
          <h2 className="text-lg font-semibold text-gray-900 mb-1">Demandes de baisse de palier</h2>
          <p className="text-sm text-gray-500 mb-4">Une baisse d&apos;engagement demande votre validation. Elle s&apos;applique aux échéances à venir.</p>
          <ul className="divide-y divide-gray-200">
            {baisses.map((x) => (
              <li key={x.id} className="py-3 flex flex-wrap items-center justify-between gap-3">
                <span><b>{x.nom}</b> : {formatMontant(x.palierActuel)} → {formatMontant(x.baisseDemandee!)}</span>
                <span className="flex gap-2">
                  <button disabled={busy} onClick={() => action(`/participants/${x.id}/baisse-palier/approuver`, {}, `Baisse approuvée pour ${x.nom}.`)}
                    className="bg-green-700 hover:bg-green-800 text-white text-sm font-medium rounded-md px-3 py-1.5 disabled:opacity-50">Approuver</button>
                  <button disabled={busy} onClick={() => action(`/participants/${x.id}/baisse-palier/refuser`, {}, `Baisse refusée : ${x.nom} garde son palier.`)}
                    className="border border-gray-300 hover:border-red-400 text-sm font-medium rounded-md px-3 py-1.5 disabled:opacity-50">Refuser</button>
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}

      {/* ── Participants ─────────────────────────────────────────── */}
      <div className="bg-white rounded-lg shadow border border-gray-200">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-semibold text-gray-900">Participants et échéancier</h2>
          <p className="text-sm text-gray-500">
            Chaque case est une échéance : <span className="inline-block w-3 h-3 align-middle rounded-sm bg-green-600" /> soldée,{" "}
            <span className="inline-block w-3 h-3 align-middle rounded-sm bg-amber-400" /> partielle,{" "}
            <span className="inline-block w-3 h-3 align-middle rounded-sm bg-red-600" /> en retard,{" "}
            <span className="inline-block w-3 h-3 align-middle rounded-sm bg-gray-200" /> à venir. Un trop-perçu est reporté sur les suivantes.
          </p>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                {["Participant", "Palier", "Payé", "Reste dû", "Avance", "Échéances"].map((h) => (
                  <th key={h} className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {p.participants.length === 0 && (
                <tr><td colSpan={6} className="px-6 py-8 text-center text-sm text-gray-400">Aucun participant : partagez un code d&apos;invitation.</td></tr>
              )}
              {p.participants.map((x: ParticipantProjet) => (
                <tr key={x.id}>
                  <td className="px-6 py-3 text-sm font-medium text-gray-900 whitespace-nowrap">{x.nom}</td>
                  <td className="px-6 py-3 text-sm tabular-nums whitespace-nowrap">{formatMontant(x.palierActuel)}</td>
                  <td className="px-6 py-3 text-sm tabular-nums whitespace-nowrap">{formatMontant(x.totalPaye)}</td>
                  <td className={`px-6 py-3 text-sm tabular-nums whitespace-nowrap ${x.resteDuExigible > 0 ? "text-red-700 font-semibold" : "text-gray-500"}`}>
                    {formatMontant(x.resteDuExigible)}
                  </td>
                  <td className="px-6 py-3 text-sm tabular-nums whitespace-nowrap text-gray-600">{formatMontant(x.avance)}</td>
                  <td className="px-6 py-3">
                    <button onClick={() => setOuvert(ouvert === x.id ? null : x.id)} className="flex gap-0.5" aria-label={`Détail des échéances de ${x.nom}`}>
                      {(x.echeances ?? []).map((e) => (
                        <span key={e.numero} title={`Échéance ${e.numero} – ${new Date(e.date).toLocaleDateString("fr-FR")} : ${ECHEANCE_LABEL[e.statut]}`}
                          className={`w-3 h-5 rounded-sm ${ECHEANCE_STYLE[e.statut]}`} />
                      ))}
                    </button>
                    {ouvert === x.id && (
                      <table className="mt-2 text-xs">
                        <tbody>
                          {(x.echeances ?? []).map((e) => (
                            <tr key={e.numero}>
                              <td className="pr-3 text-gray-500">n°{e.numero}</td>
                              <td className="pr-3">{new Date(e.date).toLocaleDateString("fr-FR")}</td>
                              <td className="pr-3 tabular-nums">{formatMontant(e.paye)} / {formatMontant(e.palier)}</td>
                              <td className="text-gray-600">{ECHEANCE_LABEL[e.statut]}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* ── Paiements ────────────────────────────────────────────── */}
      <div className="bg-white rounded-lg shadow border border-gray-200 p-6 space-y-4">
        <h2 className="text-lg font-semibold text-gray-900">Paiements</h2>
        {p.statut !== "Ouvert" ? (
          <p className="text-sm text-gray-500">Ouvrez le projet pour enregistrer des paiements.</p>
        ) : (
          <form
            onSubmit={async (e) => {
              e.preventDefault();
              const ok = await action("/paiements", { participantId: payeur, montant: Number(montant), preuve },
                "Paiement enregistré : il est en attente de validation.");
              if (ok) { setPayeur(""); setMontant(""); setPreuve(""); }
            }}
            className="grid grid-cols-1 md:grid-cols-4 gap-4 items-end"
          >
            <label className="block text-sm">
              <span className="text-gray-700">Participant</span>
              <select name="payeur" value={payeur} onChange={(e) => choisirPayeur(e.target.value)} required
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2">
                <option value="">— Choisir —</option>
                {p.participants.map((x) => <option key={x.id} value={x.id}>{x.nom}</option>)}
              </select>
            </label>
            <label className="block text-sm">
              <span className="text-gray-700">Montant (FCFA)</span>
              <input name="montant" type="number" min={1} value={montant} onChange={(e) => setMontant(e.target.value)} required
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2" />
              {choisi && (
                <span className="text-xs text-gray-500">
                  Palier {formatMontant(choisi.palierActuel)} · reste dû {formatMontant(choisi.resteDuExigible)}
                </span>
              )}
            </label>
            <label className="block text-sm">
              <span className="text-gray-700">Preuve (n° de reçu…)</span>
              <input name="preuve" value={preuve} onChange={(e) => setPreuve(e.target.value)} required
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2" />
            </label>
            <button type="submit" disabled={busy}
              className="bg-green-700 hover:bg-green-800 disabled:opacity-50 text-white text-sm font-medium rounded-md px-4 py-2">
              Enregistrer
            </button>
          </form>
        )}

        {aRejeter && (
          <div className="flex flex-wrap items-end gap-3 bg-red-50 border border-red-200 rounded-md p-3">
            <label className="block text-sm flex-1 min-w-[200px]">
              <span className="text-gray-700">Motif du rejet (obligatoire)</span>
              <input name="motif" value={motif} onChange={(e) => setMotif(e.target.value)}
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2" />
            </label>
            <button disabled={busy || !motif.trim()} onClick={async () => {
              const ok = await action(`/${aRejeter.type}/${aRejeter.id}/rejeter`, { motif }, "Rejeté.");
              if (ok) { setARejeter(null); setMotif(""); }
            }} className="bg-red-600 hover:bg-red-700 text-white text-sm font-medium rounded-md px-4 py-2 disabled:opacity-50">Confirmer le rejet</button>
            <button onClick={() => { setARejeter(null); setMotif(""); }} className="text-sm text-gray-600 px-2 py-2">Annuler</button>
          </div>
        )}

        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                {["Participant", "Montant", "Preuve", "Statut", "Saisi le", "Actions"].map((h) => (
                  <th key={h} className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {p.paiements.length === 0 && (
                <tr><td colSpan={6} className="px-4 py-6 text-center text-sm text-gray-400">Aucun paiement.</td></tr>
              )}
              {p.paiements.map((x) => (
                <tr key={x.id}>
                  <td className="px-4 py-2 text-sm font-medium">{x.participantNom}</td>
                  <td className="px-4 py-2 text-sm tabular-nums">{formatMontant(x.montant)}</td>
                  <td className="px-4 py-2 text-sm text-gray-600">{x.preuve}</td>
                  <td className="px-4 py-2"><StatusBadge status={x.statut} /></td>
                  <td className="px-4 py-2 text-sm text-gray-500">{formatDate(x.saisiLe)}</td>
                  <td className="px-4 py-2 text-sm space-x-2 whitespace-nowrap">
                    {x.statut === "en_attente" && (
                      <>
                        <button disabled={busy} onClick={() => action(`/paiements/${x.id}/valider`, {}, `Paiement de ${x.participantNom} validé.`)}
                          className="text-green-700 hover:text-green-900 font-medium">Valider</button>
                        <button disabled={busy} onClick={() => setARejeter({ type: "paiements", id: x.id })}
                          className="text-red-600 hover:text-red-800 font-medium">Rejeter</button>
                      </>
                    )}
                    {x.statut === "rejete" && x.motif && <span className="text-xs text-gray-500">{x.motif}</span>}
                    {x.statut === "valide" && x.reference && <span className="text-xs text-gray-500 font-mono">{x.reference}</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {enAttente.length > 0 && <p className="text-xs text-gray-500">{enAttente.length} paiement(s) en attente de validation.</p>}
      </div>

      {/* ── Dépenses ─────────────────────────────────────────────── */}
      <div className="bg-white rounded-lg shadow border border-gray-200 p-6 space-y-4">
        <div>
          <h2 className="text-lg font-semibold text-gray-900">Dépenses</h2>
          <p className="text-sm text-gray-500">Une dépense ne peut pas dépasser le solde disponible ({formatMontant(p.solde)}).</p>
        </div>
        {p.statut === "Ouvert" && (
          <form
            onSubmit={async (e) => {
              e.preventDefault();
              const ok = await action("/depenses", { libelle, montant: Number(montantDepense), justificatif },
                "Dépense proposée : validez-la après vérification du justificatif.");
              if (ok) { setLibelle(""); setMontantDepense(""); setJustificatif(""); }
            }}
            className="grid grid-cols-1 md:grid-cols-4 gap-4 items-end"
          >
            <label className="block text-sm">
              <span className="text-gray-700">Libellé</span>
              <input name="libelle" value={libelle} onChange={(e) => setLibelle(e.target.value)} required
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2" />
            </label>
            <label className="block text-sm">
              <span className="text-gray-700">Montant (FCFA)</span>
              <input name="montant-depense" type="number" min={1} value={montantDepense} onChange={(e) => setMontantDepense(e.target.value)} required
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2" />
            </label>
            <label className="block text-sm">
              <span className="text-gray-700">Justificatif</span>
              <input name="justificatif" value={justificatif} onChange={(e) => setJustificatif(e.target.value)} required
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2" />
            </label>
            <button type="submit" disabled={busy}
              className="border border-green-700 text-green-800 hover:bg-green-50 disabled:opacity-50 text-sm font-medium rounded-md px-4 py-2">
              Proposer la dépense
            </button>
          </form>
        )}
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                {["Libellé", "Montant", "Justificatif", "Statut", "Actions"].map((h) => (
                  <th key={h} className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {p.depenses.length === 0 && (
                <tr><td colSpan={5} className="px-4 py-6 text-center text-sm text-gray-400">Aucune dépense.</td></tr>
              )}
              {p.depenses.map((d) => (
                <tr key={d.id}>
                  <td className="px-4 py-2 text-sm font-medium">{d.libelle}</td>
                  <td className="px-4 py-2 text-sm tabular-nums">{formatMontant(d.montant)}</td>
                  <td className="px-4 py-2 text-sm text-gray-600">{d.justificatif}</td>
                  <td className="px-4 py-2"><StatusBadge status={d.statut} /></td>
                  <td className="px-4 py-2 text-sm space-x-2 whitespace-nowrap">
                    {d.statut === "en_attente" && (
                      <>
                        <button disabled={busy} onClick={() => action(`/depenses/${d.id}/valider`, {}, `Dépense « ${d.libelle} » validée.`)}
                          className="text-green-700 hover:text-green-900 font-medium">Valider</button>
                        <button disabled={busy} onClick={() => setARejeter({ type: "depenses", id: d.id })}
                          className="text-red-600 hover:text-red-800 font-medium">Rejeter</button>
                      </>
                    )}
                    {d.statut === "rejete" && d.motif && <span className="text-xs text-gray-500">{d.motif}</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {depensesEnAttente.length > 0 && <p className="text-xs text-gray-500">{depensesEnAttente.length} dépense(s) à valider.</p>}
      </div>

      {/* ── Journal ──────────────────────────────────────────────── */}
      {p.journal && (
        <details className="bg-white rounded-lg shadow border border-gray-200 p-6">
          <summary className="cursor-pointer text-lg font-semibold text-gray-900">Journal du projet ({p.journal.length} dernières actions)</summary>
          <div className="overflow-x-auto mt-4">
            <table className="min-w-full text-sm">
              <tbody className="divide-y divide-gray-100">
                {p.journal.map((j) => (
                  <tr key={j.sequence}>
                    <td className="py-1.5 pr-4 text-gray-400 tabular-nums">#{j.sequence}</td>
                    <td className="py-1.5 pr-4 whitespace-nowrap">{formatDate(j.horodatage)}</td>
                    <td className="py-1.5 pr-4 font-medium">{j.action}</td>
                    <td className="py-1.5 font-mono text-xs text-gray-400">{j.hash.slice(0, 16)}…</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      )}
    </div>
  );
}
