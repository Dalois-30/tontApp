/**
 * Formatting utilities.
 * Montants always displayed with thousands separator and "FCFA".
 */

/**
 * Format a monetary amount with thousands separator and FCFA suffix.
 * @example formatMontant(5000) → "5 000 FCFA"
 * @example formatMontant(150000) → "150 000 FCFA"
 */
export function formatMontant(amount: number): string {
  const formatted = Math.round(amount)
    .toString()
    .replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
  return `${formatted} FCFA`;
}

/**
 * Calculate the number of days remaining until a target date.
 * Returns 0 if the target date has passed.
 */
export function daysUntil(targetDate: string | Date): number {
  const target = typeof targetDate === 'string' ? new Date(targetDate) : targetDate;
  const now = new Date();
  const diffMs = target.getTime() - now.getTime();
  const diffDays = Math.ceil(diffMs / (1000 * 60 * 60 * 24));
  return Math.max(0, diffDays);
}

/**
 * Calculate the remaining time until a target date in hours, minutes, seconds (from `now`, default: the current time).
 */
export function timeRemaining(targetDate: string | Date, now: Date = new Date()): {
  hours: number;
  minutes: number;
  seconds: number;
  totalSeconds: number;
  isExpired: boolean;
} {
  const target = typeof targetDate === 'string' ? new Date(targetDate) : targetDate;
  const diffMs = target.getTime() - now.getTime();

  if (diffMs <= 0) {
    return { hours: 0, minutes: 0, seconds: 0, totalSeconds: 0, isExpired: true };
  }

  const totalSeconds = Math.floor(diffMs / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  return { hours, minutes, seconds, totalSeconds, isExpired: false };
}

const STATUTS: Record<string, string> = {
  Draft: 'Brouillon', Active: 'Active', Suspended: 'Suspendue', Completed: 'Terminée', Cancelled: 'Annulée',
  Brouillon: 'Brouillon', Ouvert: 'Ouvert', Clos: 'Clos', Annule: 'Annulé',
};
/**
 * Label of a member's open round when part of it is paid or pending; undefined otherwise (default label).
 * A partly paid round is never shown as a plain "Payé":
 * "Payé partiellement · reste 6 000 FCFA" (and/or "· 6 000 FCFA à valider");
 * a pending payment smaller than the contribution: "4 000 FCFA à valider · reste 6 000 FCFA";
 * a round covered by carry-over: "Payé · dont 3 000 FCFA reportés"; pending beyond what the round owes:
 * "8 000 FCFA à valider (dont 3 000 FCFA en excédent)".
 */
export function libelleStatutTour(
  statut: string | undefined, reste?: number, aValider?: number,
  extra?: { reporte?: number; excedent?: number },
): string | undefined {
  const r = reste ?? 0;
  const v = aValider ?? 0;
  const rep = extra?.reporte ?? 0;
  const exc = extra?.excedent ?? 0;
  const attente = v > 0 ? `${formatMontant(v)} à valider${exc > 0 ? ` (dont ${formatMontant(exc)} en excédent)` : ''}` : '';
  const reportes = rep > 0 ? `dont ${formatMontant(rep)} reportés` : '';
  if (statut === 'paye') {
    const parts = ['Payé', reportes, attente].filter(Boolean);
    return parts.length > 1 ? parts.join(' · ') : undefined;
  }
  if (statut === 'paye_partiel') return ['Payé partiellement', r > 0 ? `reste ${formatMontant(r)}` : '', reportes, attente].filter(Boolean).join(' · ');
  if (statut === 'en_attente' && (r > 0 || exc > 0)) return [attente || 'À valider', r > 0 ? `reste ${formatMontant(r)}` : ''].filter(Boolean).join(' · ');
  return undefined;
}

/** French label of a tontine/project status coming from the API. */
export const libelleStatut = (s: string): string => STATUTS[s] ?? s;

const PERIODICITES: Record<string, string> = { Monthly: 'Mensuelle', Biweekly: 'Bimensuelle', Weekly: 'Hebdomadaire' };
/** French label of a periodicity coming from the API. */
export const libellePeriodicite = (p: string): string => PERIODICITES[p] ?? p;
