/** Rules of the tontine payment screens with no React Native dependency: the caller's roles, rounds a member owes. */
import type { Gouvernance } from '../../../services/gouvernanceService';
import type { VersementTontine } from '../../../services/tontineService';
import type { TontineDetail, TontineMember, TontineTour } from '../../../types/api';
import type { Ton } from '../../../components/kit';

/** What the caller may do: the responsable runs the tontine, the trésorier validates payments. */
export const rolesTontine = (g?: Gouvernance) => ({
  responsable: !!g && (g.mesRoles.includes('responsable') || g.mesRoles.includes('admin')),
  tresorier: !!g && (g.mesRoles.includes('tresorier') || g.mesRoles.includes('admin')),
});

/** The caller's own membership (linked account), if any. */
export const monMembre = (t: TontineDetail | undefined, userId: string | undefined): TontineMember | undefined =>
  t?.membres.find((m) => !!userId && m.utilisateurId === userId);

/** A round a member can still declare a payment for, with what remains to pay on it (FCFA). */
export type TourDu = TontineTour & { reste: number };

/**
 * Rounds a member can declare a payment for, from the server's remainders (after the surplus carry-over):
 * the open round while validated + pending does not cover the contribution, then the past rounds they still
 * owe (catch-up). A round with nothing left (covered, or not owed) is never offered.
 */
export function toursDus(t: TontineDetail | undefined, m: TontineMember | undefined): TourDu[] {
  if (!t || !m || m.statutTour === 'suspendu' || m.statutTour === 'adhesion_en_attente') return [];
  const nonDuOuvert = m.statutTour === 'non_du' || m.statutTour === 'beneficiaire';
  const dus: TourDu[] = [];
  const ouvert = t.tours.find((x) => x.estOuvert);
  // Older API without resteTour: fall back on the status.
  const resteOuvert = m.resteTour ?? (m.statutTour === 'a_payer' || m.statutTour === 'en_retard' ? t.montantCotisation : 0);
  if (ouvert && !nonDuOuvert && resteOuvert > 0) dus.push({ ...ouvert, reste: resteOuvert });
  const impayes = m.toursImpayes ?? [];
  // Remainders are read by position: when not aligned with the rounds, ignore them all (older-API fallback).
  const restes = m.restesImpayes?.length === impayes.length ? m.restesImpayes : undefined;
  const vus = new Set<number>();
  impayes.forEach((n, i) => {
    const tour = t.tours.find((x) => x.numero === n);
    if (!tour || tour.estOuvert || vus.has(n)) return; // a round is offered once
    vus.add(n);
    dus.push({ ...tour, reste: restes?.[i] ?? t.montantCotisation });
  });
  return dus.filter((x) => x.reste > 0);
}

export const STATUT_VERSEMENT: Record<VersementTontine['status'], { label: string; ton: Ton }> = {
  confirme: { label: 'Validé', ton: 'bon' },
  en_attente: { label: 'À valider', ton: 'attente' },
  rejete: { label: 'Rejeté', ton: 'mauvais' },
};

/** Round selected in the declaration form: the chosen one while it is still due, else the first one due. */
export const tourParDefaut = (dus: Pick<TourDu, 'id'>[], actuel: string | null): string | null =>
  actuel !== null && dus.some((x) => x.id === actuel) ? actuel : dus[0]?.id ?? null;

/** Context of an amount typed in the declaration form: the typed amount is dropped when the member or the round changes. */
export const cleSaisieVersement = (membreId: string | undefined, tourId: string | null): string =>
  `${membreId ?? ''}|${tourId ?? ''}`;

/**
 * Amount shown in the declaration form: what the user typed (even an emptied field), else what remains on
 * the chosen round (the full contribution, or the rest of a partial payment), else the contribution.
 */
export function montantPropose(saisi: string | null, resteChoisi: number | undefined, montantCotisation: number | undefined): string {
  if (saisi !== null) return saisi;
  if (resteChoisi !== undefined) return String(resteChoisi);
  return montantCotisation !== undefined ? String(montantCotisation) : '';
}
