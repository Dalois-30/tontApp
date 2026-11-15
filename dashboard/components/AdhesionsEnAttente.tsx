"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import type { MembreDto } from "@/lib/types";

interface Props {
  tontineId: string;
  demandes: MembreDto[];
}

/** Membership requests made while the tontine was running: approve or refuse. */
export default function AdhesionsEnAttente({ tontineId, demandes }: Props) {
  const router = useRouter();
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function decider(membreId: string, decision: "accepter" | "refuser") {
    setBusy(membreId);
    setError(null);
    const res = await fetch(`/api/tontines/${tontineId}/membres/${membreId}/${decision}`, { method: "POST" });
    setBusy(null);
    if (!res.ok) {
      const data = await res.json().catch(() => ({}));
      setError(data.error ?? "Action impossible.");
      return;
    }
    router.refresh();
  }

  return (
    <div className="bg-white rounded-lg shadow p-6 border border-blue-200" data-testid="adhesions">
      <h2 className="text-lg font-semibold text-gray-900 mb-1">Demandes d&apos;adhésion</h2>
      <p className="text-sm text-gray-500 mb-4">
        La tontine a déjà démarré. Un membre accepté passe en dernier dans l&apos;ordre des bénéficiaires
        et doit les cotisations des tours déjà ouverts.
      </p>
      {error && <p className="text-sm text-red-700 mb-3">{error}</p>}
      <ul className="divide-y divide-gray-200">
        {demandes.map((m) => (
          <li key={m.id} className="py-3 flex items-center justify-between gap-4">
            <div>
              <p className="font-medium text-gray-900">{m.nom}</p>
              {m.telephone && <p className="text-sm text-gray-500">{m.telephone}</p>}
            </div>
            <div className="flex gap-2">
              <button
                onClick={() => decider(m.id, "accepter")}
                disabled={busy === m.id}
                className="bg-green-700 hover:bg-green-800 disabled:opacity-50 text-white text-sm font-medium rounded-md px-3 py-1.5"
              >
                Accepter
              </button>
              <button
                onClick={() => decider(m.id, "refuser")}
                disabled={busy === m.id}
                className="border border-gray-300 hover:border-red-400 hover:text-red-700 disabled:opacity-50 text-sm font-medium rounded-md px-3 py-1.5"
              >
                Refuser
              </button>
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}
