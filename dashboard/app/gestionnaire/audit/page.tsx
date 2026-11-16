"use client";

import { useState, useEffect, useCallback } from "react";
import { formatDate, formatMontant, libelleAction, MESSAGE_HORS_BUREAU } from "@/lib/format";
import type { AuditEntriesResult, AuditEntryDto } from "@/lib/types";
import { Card, EmptyState, Icon, PageHeader } from "@/components/ui";
import Pagination from "@/components/Pagination";

const TON: Record<string, string> = {
  VersementConfirme: "bg-good-soft text-good", VersementRejete: "bg-bad-soft text-bad",
  VersementManuel: "bg-warn-soft text-warn", VersementCree: "bg-surface-2 text-ink-2",
};
const ICONE: Record<string, string> = { VersementConfirme: "check", VersementRejete: "x", VersementManuel: "receipt", VersementCree: "plus" };

function detail(e: AuditEntryDto): string | null {
  try {
    const p = JSON.parse(e.payload) as Record<string, unknown>;
    if (p.DescriptionPreuve) return `Preuve : ${p.DescriptionPreuve}`;
    if (p.ReferenceExterne) return `Référence ${p.ReferenceExterne}`;
    if (p.Raison) return `Motif : ${p.Raison}`;
    if (typeof p.Montant === "number") return formatMontant(p.Montant);
  } catch { /* payload is free text */ }
  return null;
}

export default function AuditTrailPage() {
  const [entries, setEntries] = useState<AuditEntryDto[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [chaineValide, setChaineValide] = useState<boolean | null>(null);
  /** Last load failure: 403 → hors_bureau, 404 → aucune, anything else → erreur. */
  const [echec, setEchec] = useState<"hors_bureau" | "aucune" | "erreur" | null>(null);

  const fetchAudit = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const res = await fetch(`/api/audit?page=${p}&pageSize=20`);
      setEchec(res.ok ? null : res.status === 403 ? "hors_bureau" : res.status === 404 ? "aucune" : "erreur");
      if (res.ok) {
        const data: AuditEntriesResult = await res.json();
        setEntries(data.entries);
        setChaineValide(data.chaineValide ?? null);
        setTotalPages(data.totalPages);
        setTotal(data.totalCount);
      }
    } catch {
      setEchec("erreur");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { fetchAudit(page); }, [page, fetchAudit]);

  if (echec) {
    const [icone, titre, texte] = echec === "hors_bureau"
      ? ["shield", "Vous n'êtes au bureau d'aucune tontine", MESSAGE_HORS_BUREAU]
      : echec === "aucune"
        ? ["users", "Aucune tontine pour le moment", "Créez votre tontine depuis l'application mobile : son journal apparaîtra ici."]
        : ["alert", "Impossible de charger le journal", "Le serveur n'a pas répondu correctement. Réessayez dans un instant."];
    return (
      <>
        <PageHeader eyebrow="Espace du bureau" title="Journal d'audit" />
        <div className="card"><EmptyState icon={icone} title={titre}>{texte}</EmptyState></div>
      </>
    );
  }

  return (
    <>
      <PageHeader eyebrow="Tontine" title="Journal d'audit"
        subtitle="Chaque action sur les paiements est inscrite ici, avec son auteur. Les entrées sont chaînées par empreinte SHA-256 : modifier ou supprimer une ligne casse la chaîne." />

      {chaineValide !== null && (
        <div data-testid="chaine" className={`card p-5 flex items-start gap-4 ${chaineValide ? "border-good/30" : "border-bad/40"}`}>
          <span className={`h-10 w-10 shrink-0 rounded-full grid place-items-center ${chaineValide ? "bg-good-soft text-good" : "bg-bad-soft text-bad"}`}>
            <Icon name={chaineValide ? "shield" : "alert"} className="h-5 w-5" />
          </span>
          <div>
            <p className="font-semibold text-ink">{chaineValide ? "Chaîne intègre" : "Chaîne rompue"}</p>
            <p className="text-ink-2 text-[13.5px]">
              {chaineValide
                ? `Les ${total} entrées ont été recalculées à l'instant : aucune n'a été modifiée.`
                : "Au moins une entrée a été modifiée ou supprimée après coup. Contactez l'administrateur."}
            </p>
          </div>
        </div>
      )}

      <Card title="Chronologie" subtitle="Du plus récent au plus ancien." bodyClassName="pt-2">
        {loading ? (
          <p className="text-muted px-6 py-8">Chargement…</p>
        ) : entries.length === 0 ? (
          <EmptyState icon="shield" title="Aucune entrée pour le moment">Les paiements saisis et validés apparaîtront ici.</EmptyState>
        ) : (
          <ol className="divide-y divide-line">
            {entries.map((e) => (
              <li key={e.id} className="flex items-start gap-4 px-5 md:px-6 py-3.5">
                <span className={`mt-0.5 h-8 w-8 shrink-0 rounded-full grid place-items-center ${TON[e.action] ?? "bg-surface-2 text-ink-2"}`}>
                  <Icon name={ICONE[e.action] ?? "list"} className="h-4 w-4" />
                </span>
                <div className="min-w-0 flex-1">
                  <p className="text-ink"><span className="font-medium">{libelleAction(e.action)}</span>
                    <span className="text-ink-2"> · {e.acteurNom ?? (e.acteurId === "system" ? "Système" : "Utilisateur")}</span></p>
                  {detail(e) && <p className="text-[12.5px] text-ink-2">{detail(e)}</p>}
                  <p className="text-[12px] text-muted font-mono truncate" title={e.hash}>empreinte {e.hash.slice(0, 20)}…</p>
                </div>
                <time className="text-[12.5px] text-muted whitespace-nowrap">{formatDate(e.timestamp)}</time>
              </li>
            ))}
          </ol>
        )}
        <div className="px-5 md:px-6 border-t border-line"><Pagination page={page} totalPages={totalPages} onPageChange={setPage} /></div>
      </Card>
    </>
  );
}
