"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { formatMontant, formatJour, libelleStatutGroupe } from "@/lib/format";
import type { ProjetResume, ReglesProjet } from "@/lib/types";
import ReglesProjetForm, { REGLES_PAR_DEFAUT } from "@/components/ReglesProjetForm";
import { Badge, Card, EmptyState, Icon, Meter, PageHeader } from "@/components/ui";

const PERIODICITES = [
  { value: "Monthly", label: "Mensuelle" },
  { value: "Biweekly", label: "Toutes les deux semaines" },
  { value: "Weekly", label: "Hebdomadaire" },
];

function premierDuMoisProchain(): string {
  const d = new Date();
  return new Date(Date.UTC(d.getFullYear(), d.getMonth() + 1, 1)).toISOString().slice(0, 10);
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

  useEffect(() => { charger(); }, [charger]);

  const listePaliers = paliers.split(/[,;]+/).map((p) => Number(p.replace(/\D/g, ""))).filter((p) => p > 0);
  const engagementMax = listePaliers.length ? Math.max(...listePaliers) * Number(nombreEcheances || 0) : 0;

  async function creer(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    const res = await fetch("/api/projets", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ nom, description, objectif: Number(objectif), paliers: listePaliers, periodicite, dateDebut, nombreEcheances: Number(nombreEcheances), regles }),
    });
    const data = await res.json().catch(() => ({}));
    setBusy(false);
    if (!res.ok) { setError(data.error ?? "Création impossible."); return; }
    window.location.href = `/gestionnaire/projets/${data.id}`;
  }

  return (
    <>
      <PageHeader eyebrow="Gestion" title="Projets collectifs"
        subtitle="Un objectif commun financé par paliers d'engagement, selon un échéancier. Les dépenses sont prélevées sur le solde, après validation."
        actions={<button onClick={() => setOuvert((o) => !o)} className={ouvert ? "btn-secondary" : "btn-primary"} aria-expanded={ouvert}>
          {ouvert ? "Fermer le formulaire" : <><Icon name="plus" />Nouveau projet</>}
        </button>} />

      {ouvert && (
        <Card title="Nouveau projet" subtitle="Vous pourrez ajuster les règles plus tard ; chaque modification est journalisée.">
          <form onSubmit={creer} className="space-y-8">
            {error && <p className="notice-err">{error}</p>}
            <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
              <div className="md:col-span-2 grid grid-cols-1 md:grid-cols-[2fr_1fr] gap-5">
                <div><label htmlFor="projet-nom" className="label">Nom du projet</label>
                  <input id="projet-nom" value={nom} onChange={(e) => setNom(e.target.value)} required placeholder="Ex. : moulin à grains du groupement" className="input" /></div>
                <div><label htmlFor="projet-objectif" className="label">Objectif (FCFA)</label>
                  <input id="projet-objectif" type="number" min={1} value={objectif} onChange={(e) => setObjectif(e.target.value)} required className="input num" /></div>
              </div>
              <div className="md:col-span-2"><label htmlFor="projet-description" className="label">Description</label>
                <input id="projet-description" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="À quoi servira l'argent collecté" className="input" /></div>
              <div><label htmlFor="projet-paliers" className="label">Paliers proposés, par échéance</label>
                <input id="projet-paliers" value={paliers} onChange={(e) => setPaliers(e.target.value)} required className="input num" />
                <p className="hint">{listePaliers.length ? listePaliers.map((p) => formatMontant(p)).join(" · ") : "Séparez les montants par des virgules"}</p></div>
              <div><label htmlFor="projet-periodicite" className="label">Périodicité des échéances</label>
                <select id="projet-periodicite" value={periodicite} onChange={(e) => setPeriodicite(e.target.value)} className="input">
                  {PERIODICITES.map((p) => <option key={p.value} value={p.value}>{p.label}</option>)}
                </select></div>
              <div><label htmlFor="projet-debut" className="label">Première échéance</label>
                <input id="projet-debut" type="date" value={dateDebut} onChange={(e) => setDateDebut(e.target.value)} required className="input" /></div>
              <div><label htmlFor="projet-echeances" className="label">Nombre d&apos;échéances</label>
                <input id="projet-echeances" type="number" min={1} max={120} value={nombreEcheances} onChange={(e) => setNombreEcheances(e.target.value)} required className="input num" />
                <p className="hint">Au palier le plus haut, un participant s&apos;engage sur {formatMontant(engagementMax)}.</p></div>
            </div>
            <div className="border-t border-line pt-6 space-y-4">
              <h3 className="section-title">Règles du projet</h3>
              <ReglesProjetForm value={regles} onChange={setRegles} />
            </div>
            <div className="flex gap-3"><button type="submit" disabled={busy} className="btn-primary">Créer le projet</button>
              <button type="button" onClick={() => setOuvert(false)} className="btn-ghost">Annuler</button></div>
          </form>
        </Card>
      )}

      {loading ? (
        <p className="text-muted">Chargement…</p>
      ) : projets.length === 0 ? (
        <div className="card"><EmptyState icon="target" title="Aucun projet pour le moment">Créez le premier avec « Nouveau projet », puis partagez son code d&apos;invitation.</EmptyState></div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {projets.map((p) => (
            <Link key={p.id} href={`/gestionnaire/projets/${p.id}`}
              className="card p-5 space-y-4 transition-colors hover:border-brand/50 focus-visible:border-brand">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <h2 className="text-[16px] font-semibold text-ink truncate">{p.nom}</h2>
                  <p className="text-[12.5px] text-muted">{p.participants} participant(s) · depuis le {formatJour(p.dateDebut)}</p>
                </div>
                <Badge status={p.statut} label={libelleStatutGroupe(p.statut)} />
              </div>
              <div className="space-y-2">
                <div className="flex items-baseline justify-between">
                  <span className="text-[20px] font-semibold text-ink">{formatMontant(p.totalCollecte)}</span>
                  <span className="text-[13px] text-brand font-medium">{p.progression} %</span>
                </div>
                <Meter value={p.progression} label={`Progression de ${p.nom}`} />
                <p className="text-[12.5px] text-muted">Objectif {formatMontant(p.objectif)}</p>
              </div>
            </Link>
          ))}
        </div>
      )}
    </>
  );
}
