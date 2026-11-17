/** Rules and status labels of the collective-project screens, with no React Native dependency. */
import type { Echeance, PaiementProjet, Participant, ProjetDetails } from '../../../services/projetService';
import type { Ton } from '../../../components/kit';

/** What the caller may do on the project: the responsable runs it, the trésorier validates payments (admin: both). */
export const roles = (p?: ProjetDetails) => ({
  responsable: !!p && (p.mesRoles.includes('responsable') || p.mesRoles.includes('admin')),
  tresorier: !!p && (p.mesRoles.includes('tresorier') || p.mesRoles.includes('admin')),
});

export const ECHEANCE: Record<Echeance['statut'], { label: string; ton: Ton }> = {
  soldee: { label: 'Soldée', ton: 'bon' },
  partielle: { label: 'Partielle', ton: 'attente' },
  en_retard: { label: 'En retard', ton: 'mauvais' },
  a_venir: { label: 'À venir', ton: 'neutre' },
};

export const PAIEMENT: Record<PaiementProjet['statut'], { label: string; ton: Ton }> = {
  valide: { label: 'Validé', ton: 'bon' },
  en_attente: { label: 'À valider', ton: 'attente' },
  rejete: { label: 'Rejeté', ton: 'mauvais' },
};

export const STATUT_PARTICIPANT: Record<Participant['statut'], { label: string; ton: Ton }> = {
  Actif: { label: 'Actif', ton: 'bon' },
  Suspendu: { label: 'Suspendu', ton: 'attente' },
  Demissionnaire: { label: 'Démissionnaire', ton: 'neutre' },
  Exclu: { label: 'Exclu', ton: 'mauvais' },
};

/** "Import du registre · OM" -> "OM" ; any other proof is shown as entered. */
export const moyen = (preuve: string) => preuve.replace(/^Import du registre · /, '');
