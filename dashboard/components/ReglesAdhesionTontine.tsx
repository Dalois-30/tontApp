"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";

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
    <div className="bg-white rounded-lg shadow p-6 border border-gray-200 space-y-4" data-testid="regles-tontine">
      <div>
        <h2 className="text-lg font-semibold text-gray-900">Règles d&apos;adhésion en cours de cycle</h2>
        <p className="text-sm text-gray-500">S&apos;appliquent aux personnes qui rejoignent après l&apos;activation. Les membres déjà acceptés gardent leurs conditions.</p>
      </div>
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-sm">
        <label htmlFor="t-mode" className="block">
          <span className="font-medium text-gray-900">Adhésion après le démarrage</span>
          <select id="t-mode" value={m} onChange={(e) => setM(e.target.value as Mode)}
            className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2">
            <option value="AvecValidation">Possible, avec validation du gestionnaire</option>
            <option value="Libre">Possible, sans validation</option>
            <option value="Interdite">Interdite</option>
          </select>
        </label>
        <label htmlFor="t-rattrapage" className="flex gap-3 items-start cursor-pointer md:pt-6">
          <input id="t-rattrapage" type="checkbox" checked={r} disabled={m === "Interdite"} onChange={(e) => setR(e.target.checked)}
            className="mt-1 h-4 w-4 accent-green-700" />
          <span>
            <span className="font-medium text-gray-900">Rattrapage des tours passés</span>
            <span className="block text-gray-500">Le nouveau membre doit les cotisations des tours ouverts avant son arrivée.</span>
          </span>
        </label>
      </div>
      <div className="flex items-center gap-3">
        <button disabled={!modifie} onClick={enregistrer}
          className="bg-green-700 hover:bg-green-800 disabled:opacity-40 text-white text-sm font-medium rounded-md px-4 py-2">
          Enregistrer
        </button>
        {etat && <span className={`text-sm ${etat.ok ? "text-green-700" : "text-red-700"}`}>{etat.text}</span>}
      </div>
    </div>
  );
}
