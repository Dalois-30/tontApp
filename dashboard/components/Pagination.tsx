"use client";

export default function Pagination({ page, totalPages, onPageChange }: { page: number; totalPages: number; onPageChange: (p: number) => void }) {
  if (totalPages <= 1) return <div className="py-3 text-[12.5px] text-muted">Page 1 sur 1</div>;
  return (
    <nav className="flex items-center justify-between py-3" aria-label="Pagination">
      <p className="text-[12.5px] text-muted">Page <span className="text-ink font-medium num">{page}</span> sur <span className="num">{totalPages}</span></p>
      <div className="flex gap-2">
        <button onClick={() => onPageChange(page - 1)} disabled={page <= 1} className="btn-secondary btn-sm">Précédent</button>
        <button onClick={() => onPageChange(page + 1)} disabled={page >= totalPages} className="btn-secondary btn-sm">Suivant</button>
      </div>
    </nav>
  );
}
