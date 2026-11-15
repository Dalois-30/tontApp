"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { formatMontant } from "@/lib/format";
import type { ProjetResume, ReglesProjet } from "@/lib/types";
import ReglesProjetForm, { REGLES_PAR_DEFAUT } from "@/components/ReglesProjetForm";

const PERIODICITES = [
  { value: "Monthly", label: "Mensuelle" },
  { value: "Biweekly", label: "Toutes les 2 semaines" },
  { value: "Weekly", label: "Hebdomadaire" },
];

function premierDuMoisProchain(): string {
  const d = new Date();
  return new Date(d.getFullYear(), d.getMonth() + 1, 1).toISOString().slice(0, 10);
}

export default function ProjetsPage() {
  const [projets, setProjets] = useState<ProjetResume[]>([]);
  const [loading, setLoading] = useState(true);
  const [ouvert, setOuvert] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [nom, setNom] = useState("");
  const [description, setDescription] = useState("");
  const [objectif, setObjectif] = useState("600000");
  const [paliers, setPaliers] = useState("10000, 15000, 20000");
  const [periodicite, setPeriodicite] = useState("Monthly");
  const [dateDebut, setDateDebut] = useState(premierDuMoisProchain());
  const [nombreEcheances, setNombreEcheances] = useState("12");
  const [regles, setRegles] = useState<ReglesProjet>(REGLES_PAR_DEFAUT);

  const charger = useCallback(async () => {
    setLoading(true);
    const res = await fetch("/api/projets");
    if (res.ok) setProjets(await res.json());
    setLoading(false);
  }, []);

  useEffect(() => {
    charger();
  }, [charger]);

  const listePaliers = paliers
    .split(/[,;\s]+/)
    .map((p) => Number(p.replace(/\D/g, "")))
    .filter((p) => p > 0);

  async function creer(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    const res = await fetch("/api/projets", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        nom,
        description,
        objectif: Number(objectif),
        paliers: listePaliers,
        periodicite,
        dateDebut,
        nombreEcheances: Number(nombreEcheances),
        regles,
      }),
    });
    const data = await res.json().catch(() => ({}));
    setBusy(false);
    if (!res.ok) {
      setError(data.error ?? "Création impossible.");
      return;
    }
    window.location.href = `/gestionnaire/projets/${data.id}`;
  }

  return (
    <div className="max-w-6xl space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Projets collectifs</h1>
          <p className="text-sm text-gray-500">
            Un objectif commun, des paliers d&apos;engagement, un échéancier. Les dépenses sont prélevées sur le solde.
          </p>
        </div>
        <button
          onClick={() => setOuvert((o) => !o)}
          className="bg-green-700 hover:bg-green-800 text-white text-sm font-medium rounded-md px-4 py-2"
        >
          {ouvert ? "Fermer" : "Nouveau projet"}
        </button>
      </div>

      {ouvert && (
        <form onSubmit={creer} className="bg-white rounded-lg shadow border border-gray-200 p-6 space-y-4">
          <h2 className="text-lg font-semibold text-gray-900">Nouveau projet</h2>
          {error && <p className="text-sm text-red-700">{error}</p>}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <label className="block text-sm">
              <span className="text-gray-700">Nom du projet</span>
              <input id="projet-nom" value={nom} onChange={(e) => setNom(e.target.value)} required
                placeholder="Ex. : Moulin à grains du groupement"
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2" />
            </label>
            <label className="block text-sm">
              <span className="text-gray-700">Objectif (FCFA)</span>
              <input id="projet-objectif" type="number" min={1} value={objectif} onChange={(e) => setObjectif(e.target.value)} required
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2" />
            </label>
            <label className="block text-sm md:col-span-2">
              <span className="text-gray-700">Description</span>
              <input id="projet-description" value={description} onChange={(e) => setDescription(e.target.value)}
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2" />
            </label>
            <label className="block text-sm">
              <span className="text-gray-700">Paliers proposés (FCFA par échéance)</span>
              <input id="projet-paliers" value={paliers} onChange={(e) => setPaliers(e.target.value)} required
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2" />
              <span className="text-xs text-gray-500">
                {listePaliers.length > 0 ? listePaliers.map((p) => formatMontant(p)).join(" · ") : "Séparez les montants par des virgules"}
              </span>
            </label>
            <label className="block text-sm">
              <span className="text-gray-700">Périodicité</span>
              <select id="projet-periodicite" value={periodicite} onChange={(e) => setPeriodicite(e.target.value)}
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2">
                {PERIODICITES.map((p) => <option key={p.value} value={p.value}>{p.label}</option>)}
              </select>
            </label>
            <label className="block text-sm">
              <span className="text-gray-700">Première échéance</span>
              <input id="projet-debut" type="date" value={dateDebut} onChange={(e) => setDateDebut(e.target.value)} required
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2" />
            </label>
            <label className="block text-sm">
              <span className="text-gray-700">Nombre d&apos;échéances</span>
              <input id="projet-echeances" type="number" min={1} max={120} value={nombreEcheances}
                onChange={(e) => setNombreEcheances(e.target.value)} required
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2" />
            </label>
          </div>
          <div className="border-t border-gray-200 pt-4">
            <h3 className="text-sm font-semibold text-gray-900 mb-3">Règles du projet</h3>
            <ReglesProjetForm value={regles} onChange={setRegles} />
          </div>
          <button type="submit" disabled={busy}
            className="bg-green-700 hover:bg-green-800 disabled:opacity-50 text-white text-sm font-medium rounded-md px-4 py-2">
            Créer le projet
          </button>
        </form>
      )}

      <div className="bg-white rounded-lg shadow border border-gray-200 overflow-x-auto">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              {["Projet", "Statut", "Participants", "Collecté", "Objectif", "Progression"].map((h) => (
                <th key={h} className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {loading ? (
              <tr><td colSpan={6} className="px-6 py-8 text-center text-sm text-gray-400">Chargement...</td></tr>
            ) : projets.length === 0 ? (
              <tr><td colSpan={6} className="px-6 py-8 text-center text-sm text-gray-400">
                Aucun projet. Créez le premier avec « Nouveau projet ».
              </td></tr>
            ) : (
              projets.map((p) => (
                <tr key={p.id} className="hover:bg-gray-50">
                  <td className="px-6 py-4 text-sm font-medium">
                    <Link href={`/gestionnaire/projets/${p.id}`} className="text-green-800 hover:underline">{p.nom}</Link>
                  </td>
                  <td className="px-6 py-4 text-sm text-gray-600">{p.statut}</td>
                  <td className="px-6 py-4 text-sm text-gray-600 tabular-nums">{p.participants}</td>
                  <td className="px-6 py-4 text-sm tabular-nums">{formatMontant(p.totalCollecte)}</td>
                  <td className="px-6 py-4 text-sm tabular-nums text-gray-600">{formatMontant(p.objectif)}</td>
                  <td className="px-6 py-4 text-sm">
                    <div className="flex items-center gap-2">
                      <div className="w-28 h-2 bg-gray-200 rounded-full overflow-hidden">
                        <div className="h-full bg-green-700" style={{ width: `${Math.min(100, p.progression)}%` }} />
                      </div>
                      <span className="tabular-nums text-gray-600">{p.progression}%</span>
                    </div>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
