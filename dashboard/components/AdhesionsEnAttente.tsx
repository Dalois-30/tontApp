"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import type { MembreDto } from "@/lib/types";
import { Card, Icon } from "./ui";

/** Membership requests made while the tontine was running: the responsable approves or refuses, the rest of the bureau sees them. */
export default function AdhesionsEnAttente({ tontineId, demandes, rattrapage, peutDecider = true }: {
  tontineId: string; demandes: MembreDto[]; rattrapage: boolean; peutDecider?: boolean;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function decider(membreId: string, decision: "accepter" | "refuser") {
    setBusy(membreId);
    setError(null);
    const res = await fetch(`/api/tontines/${tontineId}/membres/${membreId}/${decision}`, { method: "POST" });
    setBusy(null);
    if (!res.ok) { setError((await res.json().catch(() => ({}))).error ?? "Action impossible."); return; }
    router.refresh();
  }

  return (
    <Card className="border-info/40" title={`Demandes d'adhésion (${demandes.length})`}
      subtitle={`La tontine a démarré. Un membre accepté passe en dernier dans l'ordre des bénéficiaires${rattrapage ? " et rattrape les tours déjà ouverts" : ", sans rattrapage des tours passés"}.${peutDecider ? "" : " Le responsable de la tontine décide."}`}>
      {error && <p className="notice-err mb-3">{error}</p>}
      <ul className="divide-y divide-line" data-testid="adhesions">
        {demandes.map((m) => (
          <li key={m.id} className="flex flex-wrap items-center justify-between gap-3 py-3 first:pt-0 last:pb-0">
            <div className="flex items-center gap-3">
              <span className="h-8 w-8 rounded-full bg-info-soft text-info grid place-items-center"><Icon name="user" /></span>
              <div>
                <p className="font-medium text-ink">{m.nom}</p>
                {m.telephone && <p className="text-[12.5px] text-muted num">{m.telephone}</p>}
              </div>
            </div>
            {peutDecider && <div className="flex gap-2">
              <button onClick={() => decider(m.id, "refuser")} disabled={busy === m.id} className="btn-secondary btn-sm">Refuser</button>
              <button onClick={() => decider(m.id, "accepter")} disabled={busy === m.id} className="btn-primary btn-sm">
                <Icon name="check" className="h-3.5 w-3.5" />Accepter
              </button>
            </div>}
          </li>
        ))}
      </ul>
    </Card>
  );
}
