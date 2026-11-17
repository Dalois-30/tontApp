import type { ReactNode } from "react";

// ── Icons (24px grid, 1.75 stroke, currentColor) ───────────────────

const paths: Record<string, ReactNode> = {
  home: <path d="M4 10.5 12 4l8 6.5V20a1 1 0 0 1-1 1h-4.5v-6h-5v6H5a1 1 0 0 1-1-1z" />,
  cash: <><rect x="3" y="6" width="18" height="12" rx="2" /><circle cx="12" cy="12" r="2.6" /><path d="M6.5 9.5v5M17.5 9.5v5" /></>,
  target: <><circle cx="12" cy="12" r="8" /><circle cx="12" cy="12" r="4" /><circle cx="12" cy="12" r="0.6" fill="currentColor" /></>,
  shield: <><path d="M12 3 5 6v5.5c0 4.3 3 7.6 7 9.5 4-1.9 7-5.2 7-9.5V6z" /><path d="m9 12 2.2 2.2L15.5 10" /></>,
  grid: <><rect x="4" y="4" width="7" height="7" rx="1.5" /><rect x="13" y="4" width="7" height="7" rx="1.5" /><rect x="4" y="13" width="7" height="7" rx="1.5" /><rect x="13" y="13" width="7" height="7" rx="1.5" /></>,
  list: <path d="M9 6h11M9 12h11M9 18h11M4.5 6h.01M4.5 12h.01M4.5 18h.01" />,
  bell: <><path d="M6 16V11a6 6 0 1 1 12 0v5l1.5 2h-15z" /><path d="M10 20.5a2.2 2.2 0 0 0 4 0" /></>,
  logout: <path d="M14 4h4a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-4M10 16l-4-4 4-4M6 12h10" />,
  check: <path d="m5 12.5 4.2 4.2L19 7" />,
  clock: <><circle cx="12" cy="12" r="8" /><path d="M12 7.5V12l3 2" /></>,
  alert: <><path d="M12 4 2.8 19.5h18.4z" /><path d="M12 10v4M12 17h.01" /></>,
  x: <path d="M6.5 6.5 17.5 17.5M17.5 6.5 6.5 17.5" />,
  minus: <path d="M6 12h12" />,
  plus: <path d="M12 5v14M5 12h14" />,
  user: <><circle cx="12" cy="8.5" r="3.5" /><path d="M5 20c.8-3.6 3.6-5.5 7-5.5s6.2 1.9 7 5.5" /></>,
  arrow: <path d="M5 12h14M13 6l6 6-6 6" />,
  back: <path d="M19 12H5M11 6l-6 6 6 6" />,
  key: <><circle cx="8" cy="15" r="4" /><path d="m11 12 8-8M16 7l2 2" /></>,
  send: <path d="M4 12 20 4l-6 16-3-7z" />,
  settings: <><circle cx="12" cy="12" r="3" /><path d="M12 3v2.5M12 18.5V21M3 12h2.5M18.5 12H21M5.6 5.6l1.8 1.8M16.6 16.6l1.8 1.8M5.6 18.4l1.8-1.8M16.6 7.4l1.8-1.8" /></>,
  receipt: <><path d="M6 3h12v18l-3-2-3 2-3-2-3 2z" /><path d="M9 8h6M9 12h6" /></>,
  users: <><circle cx="9" cy="9" r="3.2" /><path d="M3.5 19c.6-3 2.8-4.7 5.5-4.7s4.9 1.7 5.5 4.7" /><circle cx="17" cy="8" r="2.6" /><path d="M16.5 13.3c2.3.2 3.8 1.6 4.3 4.2" /></>,
};

export function Icon({ name, className = "h-4 w-4" }: { name: keyof typeof paths | string; className?: string }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.75} strokeLinecap="round"
      strokeLinejoin="round" className={className} aria-hidden="true">
      {paths[name]}
    </svg>
  );
}

// ── Page header ──────────────────────────────────────────────────────

export function PageHeader({ eyebrow, title, badge, subtitle, actions, children }: {
  eyebrow?: ReactNode; title: ReactNode; badge?: ReactNode; subtitle?: ReactNode; actions?: ReactNode; children?: ReactNode;
}) {
  return (
    <header className="flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0 space-y-1.5">
        {eyebrow && <div className="eyebrow">{eyebrow}</div>}
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-[26px] leading-tight font-semibold text-ink">{title}</h1>
          {badge}
        </div>
        {subtitle && <p className="text-[14px] text-ink-2 max-w-[70ch]">{subtitle}</p>}
        {children}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </header>
  );
}

export function Card({ title, subtitle, actions, children, className = "", bodyClassName = "card-pad", id }: {
  title?: ReactNode; subtitle?: ReactNode; actions?: ReactNode; children: ReactNode; className?: string;
  bodyClassName?: string; id?: string;
}) {
  return (
    <section className={`card ${className}`} id={id}>
      {(title || actions) && (
        <div className="flex flex-wrap items-start justify-between gap-3 px-5 md:px-6 pt-5">
          <div className="min-w-0">
            {title && <h2 className="section-title">{title}</h2>}
            {subtitle && <p className="section-sub">{subtitle}</p>}
          </div>
          {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
        </div>
      )}
      <div className={bodyClassName}>{children}</div>
    </section>
  );
}

// ── Status badge: icon + label, never color alone ───────────────────

type Tone = "good" | "warn" | "bad" | "info" | "neutral" | "serious";

const TONES: Record<Tone, string> = {
  good: "bg-good-soft text-good",
  warn: "bg-warn-soft text-warn",
  serious: "bg-serious-soft text-serious",
  bad: "bg-bad-soft text-bad",
  info: "bg-info-soft text-info",
  neutral: "bg-surface-2 text-ink-2",
};
const TONE_ICON: Record<Tone, string> = { good: "check", warn: "clock", serious: "alert", bad: "alert", info: "clock", neutral: "minus" };

/** Every status string used across the API, mapped to a French label and a tone. */
const STATUTS: Record<string, [string, Tone]> = {
  paye: ["Payé", "good"], paye_partiel: ["Payé partiellement", "serious"], confirme: ["Validé", "good"], valide: ["Validé", "good"], soldee: ["Soldée", "good"],
  en_attente: ["En attente", "warn"], partielle: ["Partielle", "warn"], a_payer: ["À payer", "neutral"],
  a_venir: ["À venir", "neutral"], en_retard: ["En retard", "bad"], rejete: ["Rejeté", "bad"],
  beneficiaire: ["Bénéficiaire", "info"], non_du: ["Non dû", "neutral"], suspendu: ["Suspendu", "neutral"], adhesion_en_attente: ["À valider", "info"],
  Active: ["Active", "good"], Ouvert: ["Ouvert", "good"], Draft: ["Brouillon", "neutral"], Brouillon: ["Brouillon", "neutral"],
  Completed: ["Terminée", "info"], Clos: ["Clos", "info"], Suspended: ["Suspendue", "warn"], Cancelled: ["Annulée", "bad"],
  Annule: ["Annulé", "bad"], critical: ["Critique", "bad"], warning: ["Attention", "serious"], info: ["Info", "info"],
};

export function Badge({ status, label }: { status: string; label?: string }) {
  const [texte, tone] = STATUTS[status] ?? [status, "neutral" as Tone];
  return (
    <span className={`inline-flex items-center gap-1 rounded-full pl-1.5 pr-2 h-6 text-[12px] font-medium whitespace-nowrap ${TONES[tone]}`}>
      <Icon name={TONE_ICON[tone]} className="h-3.5 w-3.5" />
      {label ?? texte}
    </span>
  );
}

// ── Figures ──────────────────────────────────────────────────────────

/** Stat tile: label · value · optional context line and meter. */
export function Stat({ label, value, context, meter, tone }: {
  label: string; value: ReactNode; context?: ReactNode; meter?: number; tone?: "good" | "bad" | "warn";
}) {
  const contextTone = tone === "good" ? "text-good" : tone === "bad" ? "text-bad" : tone === "warn" ? "text-warn" : "text-ink-2";
  return (
    <div className="card p-5 flex flex-col gap-1.5 min-w-0">
      <span className="text-[12.5px] text-ink-2">{label}</span>
      <span className="text-[26px] leading-none font-semibold text-ink tracking-tight">{value}</span>
      {meter !== undefined && <Meter value={meter} className="mt-2" />}
      {context && <span className={`text-[12.5px] ${contextTone}`}>{context}</span>}
    </div>
  );
}

/** Ratio against a whole: brand fill on a lighter step of the same hue. */
export function Meter({ value, className = "", label }: { value: number; className?: string; label?: string }) {
  const v = Math.max(0, Math.min(100, value));
  return (
    <div className={`h-2 w-full rounded-full bg-brand-soft overflow-hidden ${className}`} role="meter"
      aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(v)} aria-label={label}>
      <div className="h-full rounded-full bg-brand" style={{ width: `${v}%` }} />
    </div>
  );
}

export interface Segment { key: string; label: string; value: number; className: string; textClass?: string }

/** Tall segmented bar (one whole split into parts) with its legend. */
export function SegmentedBar({ segments, total, unit = "" }: { segments: Segment[]; total: number; unit?: string }) {
  const visibles = segments.filter((s) => s.value > 0);
  return (
    <div className="space-y-3">
      <div className="flex h-7 w-full gap-[2px]" role="img"
        aria-label={segments.map((s) => `${s.label} : ${s.value}${unit}`).join(", ")}>
        {total === 0 && <div className="h-full w-full rounded bg-surface-2" />}
        {visibles.map((s) => (
          <div key={s.key} title={`${s.label} : ${s.value}${unit}`}
            className={`h-full rounded first:rounded-l-md last:rounded-r-md flex items-center px-2 text-[12px] font-semibold ${s.className} ${s.textClass ?? "text-white"}`}
            style={{ width: `${(s.value / total) * 100}%`, minWidth: 4 }}>
            {(s.value / total) >= 0.12 && <span>{s.value}</span>}
          </div>
        ))}
      </div>
      <ul className="flex flex-wrap gap-x-5 gap-y-1.5 text-[12.5px] text-ink-2">
        {segments.map((s) => (
          <li key={s.key} className="inline-flex items-center gap-2">
            <span className={`h-2.5 w-2.5 rounded-sm ${s.className}`} aria-hidden="true" />
            {s.label} <span className="text-ink font-medium num">{s.value}{unit}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

export function EmptyState({ icon = "list", title, children }: { icon?: string; title: string; children?: ReactNode }) {
  return (
    <div className="flex flex-col items-center text-center gap-2 py-10 px-6">
      <span className="h-10 w-10 rounded-full bg-surface-2 text-muted grid place-items-center"><Icon name={icon} className="h-5 w-5" /></span>
      <p className="text-[14px] font-medium text-ink">{title}</p>
      {children && <p className="text-[13px] text-ink-2 max-w-[48ch]">{children}</p>}
    </div>
  );
}
