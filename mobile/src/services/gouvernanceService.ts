/**
 * Governance of a group (tontine or collective project): bureau (responsable, trésorier), votes,
 * séance reports and the readable history of the group. Every member reads everything.
 */
import apiClient from './apiClient';
import type { Bureau } from './projetService';

export type TypeGroupe = 'projets' | 'tontines';

export interface MembreGroupe {
  utilisateurId: string;
  nom: string;
  actif: boolean;
  roles: ('responsable' | 'tresorier')[];
}

export interface Vote {
  id: string;
  objet: 'ChangementResponsable';
  candidatId: string;
  candidatNom: string;
  proposeParNom: string;
  ouvertLe: string;
  fermeLe: string;
  statut: 'Ouvert' | 'Adopte' | 'Rejete';
  pour: number;
  contre: number;
  electeurs: number;
  monVote: boolean | null;
  peutVoter: boolean;
  mode: 'MajoriteDesVotants' | 'MajoriteDesMembres' | 'DeuxTiersDesMembres';
  quorumPourcent: number;
  closLe: string | null;
}

export interface Seance {
  id: string;
  date: string;
  titre: string;
  lieu: string | null;
  presents: string[];
  ordreDuJour: string | null;
  compteRendu: string;
  decisions: string[];
  auteurNom: string;
  redigeeLe: string;
}

export interface Gouvernance {
  type: string;
  groupeId: string;
  groupeNom: string;
  bureau: Bureau;
  regles: { modeVote: Vote['mode']; quorumPourcent: number; dureeVoteJours: number };
  mesRoles: string[];
  membres: MembreGroupe[];
  votes: Vote[];
  seances: Seance[];
  journalValide: boolean;
}

export type CategorieEvenement = 'paiements' | 'paliers' | 'depenses' | 'membres' | 'gouvernance' | 'votes' | 'seances' | 'projet';

export interface Evenement {
  horodatage: string;
  categorie: CategorieEvenement;
  action: string;
  resume: string;
  acteurNom: string;
  participantId: string | null;
  source: 'projet' | 'bureau' | 'versements';
  sequence: number;
}

const base = (type: TypeGroupe, id: string) => `/${type}/${id}`;

export const gouvernanceService = {
  async consulter(type: TypeGroupe, id: string): Promise<Gouvernance> {
    return (await apiClient.get<Gouvernance>(`${base(type, id)}/gouvernance`)).data;
  },
  async nommerTresorier(type: TypeGroupe, id: string, utilisateurId: string): Promise<Gouvernance> {
    return (await apiClient.put<Gouvernance>(`${base(type, id)}/gouvernance/tresorier`, { utilisateurId })).data;
  },
  async proposerResponsable(type: TypeGroupe, id: string, utilisateurId: string): Promise<Gouvernance> {
    return (await apiClient.post<Gouvernance>(`${base(type, id)}/votes/responsable`, { utilisateurId })).data;
  },
  async voter(type: TypeGroupe, id: string, voteId: string, pour: boolean): Promise<Gouvernance> {
    return (await apiClient.post<Gouvernance>(`${base(type, id)}/votes/${voteId}`, { pour })).data;
  },
  async redigerSeance(type: TypeGroupe, id: string, s: {
    date: string; titre: string; lieu?: string; presents: string[]; ordreDuJour?: string; compteRendu: string; decisions: string[];
  }): Promise<Gouvernance> {
    return (await apiClient.post<Gouvernance>(`${base(type, id)}/seances`, s)).data;
  },
  async chronologie(type: TypeGroupe, id: string, f: { categorie?: string; participantId?: string; q?: string } = {}): Promise<Evenement[]> {
    return (await apiClient.get<Evenement[]>(`${base(type, id)}/chronologie`, { params: f })).data;
  },
} as const;
