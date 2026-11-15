"use client";

import { useState, useEffect, useCallback } from "react";
import { formatMontant, formatDate } from "@/lib/format";
import type { TontineDto, VersementDto } from "@/lib/types";
import StatusBadge from "@/components/StatusBadge";
import Pagination from "@/components/Pagination";

interface PaiementsResponse {
  items: VersementDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

const STATUTS_A_ENCAISSER = new Set(["a_payer", "en_retard"]);

export default function PaiementsPage() {
  const [paiements, setPaiements] = useState<VersementDto[]>([]);
  const [tontine, setTontine] = useState<TontineDto | null>(null);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(true);
  const [message, setMessage] = useState<{ type: "ok" | "err"; text: string } | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  // Formulaire d'encaissement en espèces
  const [membreId, setMembreId] = useState("");
  const [tourId, setTourId] = useState("");
  const [montant, setMontant] = useState("");
  const [preuve, setPreuve] = useState("");

  const pageSize = 20;

  const fetchAll = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const [resP, resT] = await Promise.all([
        fetch(`/api/paiements?page=${p}&pageSize=${pageSize}`),
        fetch(`/api/tontine`),
      ]);
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

  useEffect(() => {
    fetchAll(page);
  }, [page, fetchAll]);

  const tourOuvert = tontine?.tours.find((t) => t.status === "Ouvert");
  const membresAEncaisser =
    tontine?.membres.filter(
      (m) => STATUTS_A_ENCAISSER.has(m.statut) || (m.resteDu ?? 0) > 0
    ) ?? [];

  // Rounds the selected member can pay: arrears first (oldest), then the current round.
  const membreChoisi = tontine?.membres.find((m) => m.id === membreId);
  const toursPossibles = membreChoisi
    ? [
        ...(membreChoisi.toursImpayes ?? []).map((n) => {
          const t = tontine!.tours.find((x) => x.numero === n);
          return t ? { id: t.id, label: `Tour ${n} — rattrapage` } : null;
        }),
        ...(tourOuvert && STATUTS_A_ENCAISSER.has(membreChoisi.statut)
          ? [{ id: tourOuvert.id, label: `Tour ${tourOuvert.numero} — en cours` }]
          : []),
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
      body: JSON.stringify({
        tontineId: tontine.id,
        tourId,
        membreId,
        montant: Number(montant),
        descriptionPreuve: preuve,
      }),
    });
    const data = await res.json().catch(() => ({}));
    setBusy(null);
    if (res.ok) {
      setMessage({ type: "ok", text: "Paiement enregistré : il est en attente de validation." });
      setMembreId("");
      setTourId("");
      setPreuve("");
      fetchAll(page);
    } else {
      setMessage({ type: "err", text: data.error ?? "Échec de l'enregistrement." });
    }
  }

  async function agir(id: string, action: "valider" | "rejeter") {
    let raison: string | null = null;
    if (action === "rejeter") {
      raison = window.prompt("Motif du rejet :");
      if (!raison) return;
    }
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
      setMessage({
        type: "ok",
        text: action === "valider" ? `Paiement validé (réf. ${data.reference}).` : "Paiement rejeté.",
      });
      fetchAll(page);
    } else {
      setMessage({ type: "err", text: data.error ?? "Action impossible." });
    }
  }

  return (
    <div className="max-w-6xl space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Paiements</h1>
        <p className="text-sm text-gray-500">
          Enregistrez les cotisations remises en espèces, puis validez-les après vérification de la preuve.
        </p>
      </div>

      {message && (
        <div
          data-testid="message"
          className={`px-4 py-3 rounded-md text-sm border ${
            message.type === "ok"
              ? "bg-green-50 border-green-200 text-green-800"
              : "bg-red-50 border-red-200 text-red-800"
          }`}
        >
          {message.text}
        </div>
      )}

      {/* ── Encaissement en espèces ───────────────────────────────── */}
      <form
        onSubmit={enregistrer}
        className="bg-white rounded-lg shadow border border-gray-200 p-6 space-y-4"
      >
        <h2 className="text-lg font-semibold text-gray-900">
          Enregistrer un paiement en espèces
          {tourOuvert && (
            <span className="ml-2 text-sm font-normal text-gray-500">— tour n°{tourOuvert.numero}</span>
          )}
        </h2>
        {!tourOuvert ? (
          <p className="text-sm text-gray-500">Aucun tour ouvert : activez la tontine pour encaisser.</p>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-5 gap-4 items-end">
            <label className="block text-sm">
              <span className="text-gray-700">Membre</span>
              <select
                name="membre"
                value={membreId}
                onChange={(e) => choisirMembre(e.target.value)}
                required
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2"
              >
                <option value="">— Choisir —</option>
                {membresAEncaisser.map((m) => (
                  <option key={m.id} value={m.id}>
                    {m.nom}
                    {m.statut === "en_retard" ? " (en retard)" : ""}
                    {(m.resteDu ?? 0) > 0 ? ` (reste dû ${formatMontant(m.resteDu ?? 0)})` : ""}
                  </option>
                ))}
              </select>
            </label>
            <label className="block text-sm">
              <span className="text-gray-700">Tour</span>
              <select
                name="tour"
                value={tourId}
                onChange={(e) => setTourId(e.target.value)}
                required
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2"
              >
                <option value="">— Choisir —</option>
                {toursPossibles.map((t) => (
                  <option key={t.id} value={t.id}>{t.label}</option>
                ))}
              </select>
            </label>
            <label className="block text-sm">
              <span className="text-gray-700">Montant (FCFA)</span>
              <input
                name="montant"
                type="number"
                min={100}
                value={montant}
                onChange={(e) => setMontant(e.target.value)}
                required
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2"
              />
            </label>
            <label className="block text-sm">
              <span className="text-gray-700">Preuve (n° de reçu…)</span>
              <input
                name="preuve"
                value={preuve}
                onChange={(e) => setPreuve(e.target.value)}
                required
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2"
              />
            </label>
            <button
              type="submit"
              disabled={busy === "form"}
              className="bg-green-700 hover:bg-green-800 disabled:opacity-50 text-white text-sm font-medium rounded-md px-4 py-2"
            >
              Enregistrer
            </button>
          </div>
        )}
      </form>

      {/* ── Historique ────────────────────────────────────────────── */}
      <div className="bg-white rounded-lg shadow border border-gray-200">
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                {["Payeur", "Tour", "Montant", "Statut", "Référence", "Date", "Actions"].map((h) => (
                  <th key={h} className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {loading ? (
                <tr>
                  <td colSpan={7} className="px-6 py-8 text-center text-sm text-gray-400">
                    Chargement...
                  </td>
                </tr>
              ) : paiements.length === 0 ? (
                <tr>
                  <td colSpan={7} className="px-6 py-8 text-center text-sm text-gray-400">
                    Aucun paiement enregistré.
                  </td>
                </tr>
              ) : (
                paiements.map((p) => (
                  <tr key={p.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">{p.payeurNom}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{p.tourNumero ?? "—"}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{formatMontant(p.montant)}</td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <StatusBadge status={p.status} />
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-xs text-gray-500 font-mono">{p.reference ?? "—"}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatDate(p.dateCreation)}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm space-x-2">
                      {p.status === "en_attente" && (
                        <>
                          <button
                            onClick={() => agir(p.id, "valider")}
                            disabled={busy === p.id}
                            className="text-green-700 hover:text-green-900 font-medium disabled:opacity-50"
                          >
                            Valider
                          </button>
                          <button
                            onClick={() => agir(p.id, "rejeter")}
                            disabled={busy === p.id}
                            className="text-red-600 hover:text-red-800 font-medium disabled:opacity-50"
                          >
                            Rejeter
                          </button>
                        </>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        <div className="px-6 border-t border-gray-200">
          <Pagination page={page} totalPages={totalPages} onPageChange={setPage} />
        </div>
      </div>
    </div>
  );
}
