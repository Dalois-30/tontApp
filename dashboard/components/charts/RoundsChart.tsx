"use client";

import { useState } from "react";
import { formatCompact, formatMontant } from "@/lib/format";
import { ticks, useWidth } from "./useWidth";

export interface RoundPoint { numero: number; collecte: number; attendu: number; enCours: boolean }

const H = 220, M = { t: 16, r: 12, b: 28, l: 44 };

/**
 * Collected per round (columns, brand) against what was expected (a short hairline marker).
 * Single measure + its target, so one legend row; hover/focus shows the exact figures.
 */
export default function RoundsChart({ data }: { data: RoundPoint[] }) {
  const [ref, w] = useWidth<HTMLDivElement>();
  const [hover, setHover] = useState<number | null>(null);
  const [table, setTable] = useState(false);

  const max = Math.max(1, ...data.map((d) => Math.max(d.collecte, d.attendu)));
  const yt = ticks(max);
  const top = yt[yt.length - 1];
  const iw = w - M.l - M.r, ih = H - M.t - M.b;
  const band = iw / Math.max(1, data.length);
  const bw = Math.min(24, band * 0.5);
  const y = (v: number) => M.t + ih - (v / top) * ih;
  const cx = (i: number) => M.l + band * i + band / 2;
  const h = hover !== null ? data[hover] : null;

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3 text-[12.5px] text-ink-2">
        <ul className="flex gap-5">
          <li className="inline-flex items-center gap-2"><span className="h-2.5 w-2.5 rounded-sm bg-chart-1" />Collecté (validé)</li>
          <li className="inline-flex items-center gap-2"><span className="h-0.5 w-3.5 bg-ink-2" />Attendu</li>
        </ul>
        <button className="btn-ghost btn-sm" onClick={() => setTable((t) => !t)} aria-pressed={table}>
          {table ? "Voir le graphique" : "Voir le tableau"}
        </button>
      </div>

      {table ? (
        <table className="table">
          <thead><tr><th>Tour</th><th className="text-right">Collecté</th><th className="text-right">Attendu</th></tr></thead>
          <tbody>{data.map((d) => (
            <tr key={d.numero}><td>Tour {d.numero}{d.enCours ? " (en cours)" : ""}</td>
              <td className="text-right num">{formatMontant(d.collecte)}</td><td className="text-right num">{formatMontant(d.attendu)}</td></tr>
          ))}</tbody>
        </table>
      ) : (
        <div ref={ref} className="relative">
          <svg width={w} height={H} role="img" aria-label="Collecte par tour" className="block">
            {yt.map((t) => (
              <g key={t}>
                <line x1={M.l} x2={w - M.r} y1={y(t)} y2={y(t)} className="stroke-grid" strokeWidth={1} />
                <text x={M.l - 8} y={y(t)} dy="0.32em" textAnchor="end" className="fill-muted text-[11px] num">{formatCompact(t)}</text>
              </g>
            ))}
            <line x1={M.l} x2={w - M.r} y1={y(0)} y2={y(0)} className="stroke-line" strokeWidth={1} />
            {data.map((d, i) => {
              const hBar = y(0) - y(d.collecte);
              return (
                <g key={d.numero} tabIndex={0} onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)}
                  onFocus={() => setHover(i)} onBlur={() => setHover(null)} className="outline-none"
                  aria-label={`Tour ${d.numero} : ${formatMontant(d.collecte)} collectés sur ${formatMontant(d.attendu)}`}>
                  <rect x={cx(i) - band / 2} y={M.t} width={band} height={ih} fill="transparent" />
                  {hover === i && <rect x={cx(i) - band / 2 + 2} y={M.t} width={band - 4} height={ih} className="fill-surface-2" rx={6} />}
                  {d.collecte > 0 && (
                    <path className={d.enCours ? "fill-chart-1/70" : "fill-chart-1"}
                      d={`M${cx(i) - bw / 2},${y(0)} v${-Math.max(0, hBar - 4)} q0,-4 4,-4 h${bw - 8} q4,0 4,4 v${Math.max(0, hBar - 4)} z`} />
                  )}
                  <line x1={cx(i) - bw / 2 - 5} x2={cx(i) + bw / 2 + 5} y1={y(d.attendu)} y2={y(d.attendu)}
                    className="stroke-ink-2" strokeWidth={2} strokeLinecap="round" />
                  <text x={cx(i)} y={H - 8} textAnchor="middle" className="fill-muted text-[11px]">
                    {`T${d.numero}`}
                  </text>
                </g>
              );
            })}
          </svg>
          {h && hover !== null && (
            <div className="pointer-events-none absolute z-10 rounded-lg border border-line bg-surface px-3 py-2 text-[12.5px] shadow-lg"
              style={{ left: Math.min(Math.max(cx(hover) - 90, 0), w - 180), top: 0, width: 180 }}>
              <p className="font-medium text-ink">Tour {h.numero}{h.enCours ? " · en cours" : ""}</p>
              <p className="flex justify-between text-ink-2"><span>Collecté</span><span className="num text-ink">{formatMontant(h.collecte)}</span></p>
              <p className="flex justify-between text-ink-2"><span>Attendu</span><span className="num text-ink">{formatMontant(h.attendu)}</span></p>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
