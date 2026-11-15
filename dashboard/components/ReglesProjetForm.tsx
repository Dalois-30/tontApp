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
    <label htmlFor={id} className="flex gap-3 items-start cursor-pointer">
      <input id={id} type="checkbox" checked={checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)}
        className="mt-0.5 h-4 w-4 shrink-0 accent-[rgb(var(--brand))]" />
      <span>
        <span className="font-medium text-ink">{label}</span>
        <span className="block text-[12.5px] text-ink-2">{aide}</span>
      </span>
    </label>
  );
}

/** Editor for a project's configurable business rules. */
export default function ReglesProjetForm({ value, onChange, disabled }: Props) {
  const set = <K extends keyof ReglesProjet>(k: K, v: ReglesProjet[K]) => onChange({ ...value, [k]: v });
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-x-10 gap-y-7">
      <fieldset className="space-y-3">
        <legend className="eyebrow mb-3">Paiements</legend>
        <Case id="r-partiel" checked={value.paiementPartielAutorise} disabled={disabled}
          onChange={(v) => set("paiementPartielAutorise", v)}
          label="Accepter les paiements partiels"
          aide="Un versement inférieur au montant attendu est accepté ; le reste reste dû." />
        <div>
          <label htmlFor="r-trop" className="label">Trop-perçu</label>
          <select id="r-trop" value={value.tropPercu} disabled={disabled}
            onChange={(e) => set("tropPercu", e.target.value as ReglesProjet["tropPercu"])}
            className="input">
            <option value="Reporter">Reporter sur les échéances suivantes</option>
            <option value="Refuser">Refuser le paiement</option>
          </select>
        </div>
      </fieldset>

      <fieldset className="space-y-3">
        <legend className="eyebrow mb-3">Hausse de palier</legend>
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
        <legend className="eyebrow mb-3">Baisse de palier</legend>
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
        <legend className="eyebrow mb-3">Arrivée en cours de projet</legend>
        <Case id="r-rattrapage" checked={value.rattrapageAdhesionTardive} disabled={disabled}
          onChange={(v) => set("rattrapageAdhesionTardive", v)}
          label="Exiger le rattrapage"
          aide="Un participant arrivé tard doit aussi les échéances passées. Sinon, il doit à partir de son arrivée." />
      </fieldset>
    </div>
  );
}
