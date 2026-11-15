"use client";

import type { ReglesProjet } from "@/lib/types";

export const REGLES_PAR_DEFAUT: ReglesProjet = {
  paiementPartielAutorise: true,
  tropPercu: "Reporter",
  hausseRetroactive: true,
  hausseSoumiseAValidation: false,
  baisseAutorisee: true,
  baisseSoumiseAValidation: true,
  baisseRetroactive: false,
  rattrapageAdhesionTardive: false,
};

interface Props {
  value: ReglesProjet;
  onChange: (r: ReglesProjet) => void;
  disabled?: boolean;
}

function Case({ id, checked, onChange, disabled, label, aide }: {
  id: string; checked: boolean; onChange: (v: boolean) => void; disabled?: boolean; label: string; aide: string;
}) {
  return (
    <label htmlFor={id} className="flex gap-3 items-start text-sm cursor-pointer">
      <input id={id} type="checkbox" checked={checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)}
        className="mt-1 h-4 w-4 accent-green-700" />
      <span>
        <span className="font-medium text-gray-900">{label}</span>
        <span className="block text-gray-500">{aide}</span>
      </span>
    </label>
  );
}

/** Editor for a project's configurable business rules. */
export default function ReglesProjetForm({ value, onChange, disabled }: Props) {
  const set = <K extends keyof ReglesProjet>(k: K, v: ReglesProjet[K]) => onChange({ ...value, [k]: v });
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-x-8 gap-y-5">
      <fieldset className="space-y-3">
        <legend className="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-2">Paiements</legend>
        <Case id="r-partiel" checked={value.paiementPartielAutorise} disabled={disabled}
          onChange={(v) => set("paiementPartielAutorise", v)}
          label="Accepter les paiements partiels"
          aide="Un versement inférieur au montant attendu est accepté ; le reste reste dû." />
        <div className="text-sm">
          <label htmlFor="r-trop" className="font-medium text-gray-900">Trop-perçu</label>
          <select id="r-trop" value={value.tropPercu} disabled={disabled}
            onChange={(e) => set("tropPercu", e.target.value as ReglesProjet["tropPercu"])}
            className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2">
            <option value="Reporter">Reporter sur les échéances suivantes</option>
            <option value="Refuser">Refuser le paiement</option>
          </select>
        </div>
      </fieldset>

      <fieldset className="space-y-3">
        <legend className="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-2">Hausse de palier</legend>
        <Case id="r-h-retro" checked={value.hausseRetroactive} disabled={disabled}
          onChange={(v) => set("hausseRetroactive", v)}
          label="Compléter les anciennes échéances"
          aide="La hausse s'applique aussi aux échéances passées : la différence devient due et est réglée en premier." />
        <Case id="r-h-valid" checked={value.hausseSoumiseAValidation} disabled={disabled}
          onChange={(v) => set("hausseSoumiseAValidation", v)}
          label="Soumettre la hausse à validation"
          aide="Sinon, la hausse s'applique dès la demande du participant." />
      </fieldset>

      <fieldset className="space-y-3">
        <legend className="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-2">Baisse de palier</legend>
        <Case id="r-b-ok" checked={value.baisseAutorisee} disabled={disabled}
          onChange={(v) => set("baisseAutorisee", v)}
          label="Autoriser la baisse"
          aide="Sinon, le palier choisi ne peut que rester ou augmenter." />
        <Case id="r-b-valid" checked={value.baisseSoumiseAValidation} disabled={disabled || !value.baisseAutorisee}
          onChange={(v) => set("baisseSoumiseAValidation", v)}
          label="Soumettre la baisse à validation"
          aide="Deuxième validation par le gestionnaire avant que la baisse s'applique." />
        <Case id="r-b-retro" checked={value.baisseRetroactive} disabled={disabled || !value.baisseAutorisee}
          onChange={(v) => set("baisseRetroactive", v)}
          label="Appliquer la baisse aux échéances passées"
          aide="L'argent déjà reçu est alors redistribué. Sinon, la baisse ne vaut qu'à partir de sa prise d'effet." />
      </fieldset>

      <fieldset className="space-y-3">
        <legend className="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-2">Arrivée en cours de projet</legend>
        <Case id="r-rattrapage" checked={value.rattrapageAdhesionTardive} disabled={disabled}
          onChange={(v) => set("rattrapageAdhesionTardive", v)}
          label="Exiger le rattrapage"
          aide="Un participant arrivé tard doit aussi les échéances passées. Sinon, il doit à partir de son arrivée." />
      </fieldset>
    </div>
  );
}
