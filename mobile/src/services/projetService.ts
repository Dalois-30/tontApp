import apiClient from './apiClient';

export type StatutEcheance = 'soldee' | 'partielle' | 'en_retard' | 'a_venir';

export interface ProjetResume {
  id: string;
  nom: string;
  statut: string;
  role: 'gestionnaire' | 'participant';
  objectif: number;
  totalCollecte: number;
  progression: number;
  participants: number;
  monPalier: number | null;
  monResteDu: number | null;
}

export interface Echeance {
  numero: number;
  date: string;
  palier: number;
  paye: number;
  reste: number;
  statut: StatutEcheance;
}

export interface Participant {
  id: string;
  nom: string;
  palierActuel: number;
  baisseDemandee: number | null;
  totalPaye: number;
  engagementTotal: number;
  resteDuExigible: number;
  avance: number;
  echeances: Echeance[] | null;
}

export interface ProjetDetails {
  id: string;
  nom: string;
  description: string | null;
  objectif: number;
  paliers: number[];
  periodicite: string;
  dateDebut: string;
  nombreEcheances: number;
  statut: string;
  role: 'gestionnaire' | 'participant';
  monParticipantId: string | null;
  totalCollecte: number;
  totalDepense: number;
  solde: number;
  progression: number;
  participants: Participant[];
  depenses: { id: string; libelle: string; montant: number; statut: string; proposeeLe: string }[];
  paiements: { id: string; montant: number; statut: string; saisiLe: string; reference: string | null }[];
}

export interface ProjetApercu {
  nom: string;
  description: string | null;
  objectif: number;
  paliers: number[];
  periodicite: string;
  dateDebut: string;
  nombreEcheances: number;
  participants: number;
}

export const projetService = {
  async mesProjets(): Promise<ProjetResume[]> {
    return (await apiClient.get<ProjetResume[]>('/projets')).data;
  },
  async details(id: string): Promise<ProjetDetails> {
    return (await apiClient.get<ProjetDetails>(`/projets/${id}`)).data;
  },
  async apercu(code: string): Promise<ProjetApercu> {
    return (await apiClient.get<ProjetApercu>('/projets/apercu', { params: { code } })).data;
  },
  async rejoindre(code: string, nom: string, palier: number): Promise<ProjetDetails> {
    return (await apiClient.post<ProjetDetails>('/projets/rejoindre', { code, nom, palier })).data;
  },
  /** Raise: applied from the next instalment. Lower: waits for the gestionnaire's approval. */
  async changerMonPalier(id: string, palier: number): Promise<'applique' | 'en_attente_validation'> {
    return (await apiClient.put<{ statut: 'applique' | 'en_attente_validation' }>(`/projets/${id}/mon-palier`, { palier })).data.statut;
  },
} as const;

export function messageErreur(e: unknown): string {
  return (e as { response?: { data?: { error?: string } } }).response?.data?.error ?? (e as Error).message;
}
