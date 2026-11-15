"use client";

import { useCallback, useEffect, useState } from "react";
import { formatJour, formatMontant, libellePeriodicite } from "@/lib/format";
import { Badge, Card, EmptyState, PageHeader } from "@/components/ui";
import Pagination from "@/components/Pagination";

interface TontineListItem { id: string; nom: string; status: string; membresCount: number; montantCotisation: number; periodicite: string; createdAt: string }

export default function TontinesAdminPage() {
  const [items, setItems] = useState<TontineListItem[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [q, setQ] = useState("");

  const charger = useCallback(async (p: number) => {
    setLoading(true);
    const res = await fetch(`/api/admin/tontines?page=${p}&pageSize=20`);
    if (res.ok) { const d = await res.json(); setItems(d.items ?? []); setTotalPages(d.totalPages); setTotal(d.totalCount); }
    setLoading(false);
  }, []);
  useEffect(() => { charger(page); }, [page, charger]);

  const visibles = q ? items.filter((t) => t.nom.toLowerCase().includes(q.toLowerCase())) : items;

  return (
    <>
      <PageHeader eyebrow="Plateforme" title="Tontines" subtitle={`${total} tontine(s) sur la plateforme.`} />
      <Card bodyClassName="" title="Toutes les tontines"
        actions={<input aria-label="Rechercher une tontine" placeholder="Rechercher par nom" value={q} onChange={(e) => setQ(e.target.value)} className="input h-9 w-56" />}>
        <div className="overflow-x-auto mt-4 border-t border-line">
          <table className="table">
            <thead><tr><th>Nom</th><th>Statut</th><th className="text-right">Membres</th><th className="text-right">Cotisation</th><th>Périodicité</th><th>Créée le</th></tr></thead>
            <tbody>
              {loading ? <tr><td colSpan={6} className="text-center text-muted py-8">Chargement…</td></tr>
                : visibles.length === 0 ? <tr><td colSpan={6}><EmptyState icon="list" title="Aucune tontine trouvée" /></td></tr>
                : visibles.map((t) => (
                  <tr key={t.id}>
                    <td className="font-medium text-ink">{t.nom}</td>
                    <td><Badge status={t.status} /></td>
                    <td className="text-right num">{t.membresCount}</td>
                    <td className="text-right num">{formatMontant(t.montantCotisation)}</td>
                    <td className="text-ink-2">{libellePeriodicite(t.periodicite)}</td>
                    <td className="text-ink-2 whitespace-nowrap">{formatJour(t.createdAt)}</td>
                  </tr>
                ))}
            </tbody>
          </table>
        </div>
        <div className="px-5 md:px-6 border-t border-line"><Pagination page={page} totalPages={totalPages} onPageChange={setPage} /></div>
      </Card>
    </>
  );
}
