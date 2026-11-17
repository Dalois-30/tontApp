/** Shared data hooks of the tontine payment screens (the pure rules are in logique.ts). */
import { useCallback } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useFocusEffect } from '@react-navigation/native';
import { tontineService } from '../../../services/tontineService';
import { gouvernanceService } from '../../../services/gouvernanceService';

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

export function messageErreur(e: unknown): string {
  return (e as { response?: { data?: { error?: string } } }).response?.data?.error ?? (e as Error).message;
}
