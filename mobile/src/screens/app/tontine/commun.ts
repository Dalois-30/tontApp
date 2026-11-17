/** Shared helpers of the tontine payment screens: data hooks, the caller's roles, rounds a member owes. */
import { useCallback } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useFocusEffect } from '@react-navigation/native';
import { tontineService, type VersementTontine } from '../../../services/tontineService';
import { gouvernanceService, type Gouvernance } from '../../../services/gouvernanceService';
import type { TontineDetail, TontineMember, TontineTour } from '../../../types/api';
import type { Ton } from '../../../components/kit';

/** The tontine and its bureau, refreshed when the screen regains focus. */
export function useTontine(tontineId: string) {
  const tontine = useQuery({ queryKey: ['tontine', tontineId], queryFn: () => tontineService.getTontineById(tontineId) });
  const gouvernance = useQuery({ queryKey: ['gouvernance', 'tontines', tontineId], queryFn: () => gouvernanceService.consulter('tontines', tontineId) });
  const refetchT = tontine.refetch;
  const refetchG = gouvernance.refetch;
  const refetch = useCallback(async () => { await Promise.all([refetchT(), refetchG()]); }, [refetchT, refetchG]);
  useFocusEffect(useCallback(() => { void refetch(); }, [refetch]));
  return {
    tontine: tontine.data,
    gouvernance: gouvernance.data,
    error: tontine.error ?? gouvernance.error,
    isRefetching: tontine.isRefetching || gouvernance.isRefetching,
    refetch,
  };
}

/** Every payment of the tontine, refreshed on focus. */
export function useVersements(tontineId: string) {
  const q = useQuery({ queryKey: ['versements-tontine', tontineId], queryFn: () => tontineService.getVersementsTontine(tontineId) });
  const { refetch } = q;
  useFocusEffect(useCallback(() => { void refetch(); }, [refetch]));
  return q;
}

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
  (m.toursImpayes ?? []).forEach((n, i) => {
    const tour = t.tours.find((x) => x.numero === n);
    if (tour && !tour.estOuvert) dus.push({ ...tour, reste: m.restesImpayes?.[i] ?? t.montantCotisation });
  });
  return dus.filter((x) => x.reste > 0);
}

export const STATUT_VERSEMENT: Record<VersementTontine['status'], { label: string; ton: Ton }> = {
  confirme: { label: 'Validé', ton: 'bon' },
  en_attente: { label: 'À valider', ton: 'attente' },
  rejete: { label: 'Rejeté', ton: 'mauvais' },
};

export function messageErreur(e: unknown): string {
  return (e as { response?: { data?: { error?: string } } }).response?.data?.error ?? (e as Error).message;
}
