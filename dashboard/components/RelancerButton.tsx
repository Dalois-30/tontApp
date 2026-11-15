"use client";

import { useState } from "react";
import { Icon } from "./ui";

/** Sends an SMS reminder to one member. */
export default function RelancerButton({ membreId, membreNom, tontineId }: { membreId: string; membreNom: string; tontineId: string }) {
  const [etat, setEtat] = useState<"idle" | "busy" | "sent" | "err">("idle");

  async function relancer() {
    setEtat("busy");
    const res = await fetch("/api/relancer", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ membreId, tontineId }),
    }).catch(() => null);
    setEtat(res?.ok ? "sent" : "err");
  }

  if (etat === "sent") return <span className="inline-flex items-center gap-1.5 text-[12.5px] text-good"><Icon name="check" />Relancé</span>;
  return (
    <button onClick={relancer} disabled={etat === "busy"} className="btn-secondary btn-sm" aria-label={`Relancer ${membreNom} par SMS`}>
      <Icon name="send" className="h-3.5 w-3.5" />{etat === "busy" ? "Envoi…" : etat === "err" ? "Réessayer" : "Relancer"}
    </button>
  );
}
