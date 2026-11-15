"use client";

import { useState } from "react";
import { formatCompact, formatJour, formatMontant } from "@/lib/format";
import { ticks, useWidth } from "./useWidth";

export interface CollectePoint { date: string; attendu: number; collecte: number | null }

const H = 240, M = { t: 18, r: 96, b: 30, l: 48 };

/**
 * Emphasis line chart: what was collected (brand) against what was due (de-emphasis gray),
 * both cumulative per instalment date. Crosshair + tooltip on hover/focus, table view toggle.
 */
export default function CollecteChart({ data }: { data: CollectePoint[] }) {
  const [ref, w] = useWidth<HTMLDivElement>();
  const [i, setI] = useState<number | null>(null);
  const [table, setTable] = useState(false);
  if (data.length === 0) return null;

  const max = Math.max(1, ...data.map((d) => Math.max(d.attendu, d.collecte ?? 0)));
  const yt = ticks(max);
  const top = yt[yt.length - 1];
  const iw = w - M.l - M.r, ih = H - M.t - M.b;
  const x = (k: number) => M.l + (data.length === 1 ? iw / 2 : (k / (data.length - 1)) * iw);
  const y = (v: number) => M.t + ih - (v / top) * ih;
  const ligne = (vals: (number | null)[]) =>
    vals.map((v, k) => (v === null ? null : `${x(k)},${y(v)}`)).filter(Boolean).join(" L");
  const derniersCollecte = data.map((d) => d.collecte).lastIndexOf(data.filter((d) => d.collecte !== null).at(-1)?.collecte ?? null);
  const aCollecte = data.findLastIndex((d) => d.collecte !== null);
  const h = i !== null ? data[i] : null;

  const surSouris = (e: React.MouseEvent<SVGRectElement>) => {
    const box = (e.target as SVGRectElement).getBoundingClientRect();
    const k = Math.round(((e.clientX - box.left) / box.width) * (data.length - 1));
    setI(Math.max(0, Math.min(data.length - 1, k)));
  };

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3 text-[12.5px] text-ink-2">
        <ul className="flex gap-5">
          <li className="inline-flex items-center gap-2"><span className="h-0.5 w-4 rounded bg-chart-1" />Collecté (validé)</li>
          <li className="inline-flex items-center gap-2"><span className="h-0.5 w-4 rounded bg-chart-2" />Attendu selon les paliers</li>
        </ul>
        <button className="btn-ghost btn-sm" onClick={() => setTable((t) => !t)} aria-pressed={table}>{table ? "Voir le graphique" : "Voir le tableau"}</button>
      </div>

      {table ? (
        <div className="overflow-x-auto">
          <table className="table">
            <thead><tr><th>Échéance</th><th className="text-right">Attendu cumulé</th><th className="text-right">Collecté cumulé</th></tr></thead>
            <tbody>{data.map((d) => (
              <tr key={d.date}><td>{formatJour(d.date)}</td><td className="text-right num">{formatMontant(d.attendu)}</td>
                <td className="text-right num">{d.collecte === null ? "—" : formatMontant(d.collecte)}</td></tr>
            ))}</tbody>
          </table>
        </div>
      ) : (
        <div ref={ref} className="relative">
          <svg width={w} height={H} role="img" aria-label="Collecte cumulée et montant attendu par échéance" className="block">
            {yt.map((t) => (
              <g key={t}>
                <line x1={M.l} x2={w - M.r} y1={y(t)} y2={y(t)} className="stroke-grid" />
                <text x={M.l - 8} y={y(t)} dy="0.32em" textAnchor="end" className="fill-muted text-[11px] num">{formatCompact(t)}</text>
              </g>
            ))}
            {data.map((d, k) => (k === 0 || k === data.length - 1 || k % Math.ceil(data.length / 6) === 0) && (
              <text key={d.date} x={x(k)} y={H - 8} textAnchor="middle" className="fill-muted text-[11px]">
                {new Intl.DateTimeFormat("fr-FR", { month: "short", year: "2-digit" }).format(new Date(d.date))}
              </text>
            ))}
            <path d={`M${ligne(data.map((d) => d.attendu))}`} fill="none" className="stroke-chart-2" strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
            {aCollecte >= 0 && (
              <>
                <path d={`M${x(0)},${y(0)} L${ligne(data.map((d) => d.collecte))} L${x(aCollecte)},${y(0)} Z`} className="fill-chart-1/10" />
                <path d={`M${ligne(data.map((d) => d.collecte))}`} fill="none" className="stroke-chart-1" strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
              </>
            )}
            {/* end labels: last expected point, last collected point */}
            <text x={x(data.length - 1) + 8} y={y(data.at(-1)!.attendu)} dy="0.32em" className="fill-ink-2 text-[11.5px] num">{formatCompact(data.at(-1)!.attendu)}</text>
            {aCollecte >= 0 && derniersCollecte >= 0 && (
              <>
                <circle cx={x(aCollecte)} cy={y(data[aCollecte].collecte!)} r={4.5} className="fill-chart-1 stroke-surface" strokeWidth={2} />
                <text x={x(aCollecte) + 8} y={y(data[aCollecte].collecte!) - 10} className="fill-ink text-[11.5px] font-semibold num">{formatCompact(data[aCollecte].collecte!)}</text>
              </>
            )}
            {h && i !== null && (
              <g>
                <line x1={x(i)} x2={x(i)} y1={M.t} y2={M.t + ih} className="stroke-line" />
                <circle cx={x(i)} cy={y(h.attendu)} r={4} className="fill-chart-2 stroke-surface" strokeWidth={2} />
                {h.collecte !== null && <circle cx={x(i)} cy={y(h.collecte)} r={4} className="fill-chart-1 stroke-surface" strokeWidth={2} />}
              </g>
            )}
            <rect x={M.l} y={M.t} width={iw} height={ih} fill="transparent" tabIndex={0} aria-label="Parcourir les échéances (flèches)"
              onMouseMove={surSouris} onMouseLeave={() => setI(null)} onFocus={() => setI(0)} onBlur={() => setI(null)}
              onKeyDown={(e) => {
                if (e.key === "ArrowRight") setI((v) => Math.min(data.length - 1, (v ?? -1) + 1));
                if (e.key === "ArrowLeft") setI((v) => Math.max(0, (v ?? 1) - 1));
              }} className="outline-none" />
          </svg>
          {h && i !== null && (
            <div className="pointer-events-none absolute z-10 rounded-lg border border-line bg-surface px-3 py-2 text-[12.5px] shadow-lg"
              style={{ left: Math.min(Math.max(x(i) - 100, 0), w - 200), top: 0, width: 200 }}>
              <p className="font-medium text-ink">Échéance du {formatJour(h.date)}</p>
              <p className="flex justify-between text-ink-2"><span>Attendu cumulé</span><span className="num text-ink">{formatMontant(h.attendu)}</span></p>
              <p className="flex justify-between text-ink-2"><span>Collecté cumulé</span><span className="num text-ink">{h.collecte === null ? "à venir" : formatMontant(h.collecte)}</span></p>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
