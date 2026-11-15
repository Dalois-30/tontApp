"use client";

import { useCallback, useEffect, useState } from "react";
import type { AlerteDto } from "@/lib/types";
import { Card, EmptyState, PageHeader } from "@/components/ui";
import { AlerteLigne } from "@/components/AlerteLigne";
import Pagination from "@/components/Pagination";

type Filtre = "toutes" | "critical" | "warning" | "info";

export default function AlertesPage() {
  const [items, setItems] = useState<AlerteDto[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(true);
  const [filtre, setFiltre] = useState<Filtre>("toutes");

  const charger = useCallback(async (p: number) => {
    setLoading(true);
    const res = await fetch(`/api/admin/alertes?page=${p}&pageSize=20`);
    if (res.ok) { const d = await res.json(); setItems(d.items ?? []); setTotalPages(d.totalPages); }
    setLoading(false);
  }, []);
  useEffect(() => { charger(page); }, [page, charger]);

  const visibles = filtre === "toutes" ? items : items.filter((a) => a.severity === filtre);

  return (
    <>
      <PageHeader eyebrow="Plateforme" title="Alertes"
        subtitle="Calculées à partir des données : chaîne d'audit rompue (critique), tour dont l'échéance est dépassée (attention), SMS en échec (info)." />
      <Card title="Toutes les alertes" bodyClassName="pt-2"
        actions={
          <div className="inline-flex rounded-lg border border-line p-0.5 bg-surface-2" role="tablist" aria-label="Filtrer par gravité">
            {([["toutes", "Toutes"], ["critical", "Critiques"], ["warning", "Attention"], ["info", "Info"]] as [Filtre, string][]).map(([k, l]) => (
              <button key={k} role="tab" aria-selected={filtre === k} onClick={() => setFiltre(k)}
                className={`h-7 px-3 rounded-md text-[12.5px] font-medium ${filtre === k ? "bg-surface text-ink shadow-sm" : "text-ink-2 hover:text-ink"}`}>{l}</button>
            ))}
          </div>
        }>
        {loading ? <p className="text-muted px-6 py-8">Chargement…</p>
          : visibles.length === 0 ? <EmptyState icon="check" title="Aucune alerte">Tout fonctionne normalement.</EmptyState>
          : <ul className="divide-y divide-line">{visibles.map((a) => <AlerteLigne key={a.id} a={a} />)}</ul>}
        <div className="px-5 md:px-6 border-t border-line"><Pagination page={page} totalPages={totalPages} onPageChange={setPage} /></div>
      </Card>
    </>
  );
}
