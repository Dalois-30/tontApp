/** Member's round status from the API (`statut`) to the mobile payment status; no React Native dependency. */
import type { StatutPaiement } from '../types/api';

/** undefined for the statuses with no payment to show (beneficiaire, suspendu, anything unknown). */
export function mapStatutPaiement(statut: string): StatutPaiement | undefined {
  if (statut === 'paye') return 'paye';
  if (statut === 'paye_partiel') return 'paye_partiel';
  if (statut === 'en_retard') return 'en_retard';
  if (statut === 'en_attente') return 'en_attente';
  if (statut === 'a_payer') return 'a_payer';
  if (statut === 'adhesion_en_attente') return 'adhesion_en_attente';
  if (statut === 'non_du') return 'non_du';
  return undefined; // beneficiaire, suspendu
}
