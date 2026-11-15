"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Card } from "./ui";

type Mode = "Interdite" | "AvecValidation" | "Libre";

interface Props {
  tontineId: string;
  mode: Mode;
  rattrapage: boolean;
}

/** Late-joining rules of the tontine, editable by the gestionnaire. */
export default function ReglesAdhesionTontine({ tontineId, mode, rattrapage }: Props) {
  const router = useRouter();
  const [m, setM] = useState<Mode>(mode);
  const [r, setR] = useState(rattrapage);
  const [etat, setEtat] = useState<{ ok: boolean; text: string } | null>(null);
  const modifie = m !== mode || r !== rattrapage;

  async function enregistrer() {
    setEtat(null);
    const res = await fetch(`/api/tontines/${tontineId}/regles`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ adhesionEnCours: m, rattrapageAdhesionTardive: r }),
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) { setEtat({ ok: false, text: data.error ?? "Enregistrement impossible." }); return; }
    setEtat({ ok: true, text: "Règles enregistrées." });
    router.refresh();
  }

  return (
    <Card title="Règles d'adhésion en cours de cycle" id="regles-tontine"
      subtitle="Pour les personnes qui rejoignent après l'activation. Les membres déjà acceptés gardent leurs conditions.">
      <div data-testid="regles-tontine" className="grid grid-cols-1 md:grid-cols-2 gap-5">
        <div>
          <label htmlFor="t-mode" className="label">Adhésion après le démarrage</label>
          <select id="t-mode" value={m} onChange={(e) => setM(e.target.value as Mode)} className="input">
            <option value="AvecValidation">Possible, avec validation du gestionnaire</option>
            <option value="Libre">Possible, sans validation</option>
            <option value="Interdite">Interdite</option>
          </select>
        </div>
        <label htmlFor="t-rattrapage" className="flex gap-3 items-start cursor-pointer md:pt-6">
          <input id="t-rattrapage" type="checkbox" checked={r} disabled={m === "Interdite"} onChange={(e) => setR(e.target.checked)}
            className="mt-0.5 h-4 w-4 accent-[rgb(var(--brand))]" />
          <span>
            <span className="font-medium text-ink">Rattrapage des tours passés</span>
            <span className="block text-[12.5px] text-ink-2">Le nouveau membre doit les cotisations des tours ouverts avant son arrivée.</span>
          </span>
        </label>
      </div>
      <div className="flex items-center gap-3 mt-5">
        <button disabled={!modifie} onClick={enregistrer} className="btn-primary">Enregistrer</button>
        {etat && <span className={`text-[13px] ${etat.ok ? "text-good" : "text-bad"}`}>{etat.text}</span>}
      </div>
    </Card>
  );
}
