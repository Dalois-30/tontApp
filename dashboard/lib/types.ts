// ── API response types matching the ASP.NET API DTOs ──────────────────

export interface TontineDto {
  id: string;
  nom: string;
  description: string;
  montantCotisation: number;
  periodicite: string;
  maxMembres: number;
  status: string;
  membres: MembreDto[];
  tours: TourDto[];
  createdAt: string;
  adhesionEnCours?: "Interdite" | "AvecValidation" | "Libre";
  rattrapageAdhesionTardive?: boolean;
}

export interface MembreDto {
  id: string;
  nom: string;
  telephone?: string;
  statut: string;
  utilisateurId?: string;
  rang?: number;
  /** Arrears for earlier rounds (FCFA) */
  resteDu?: number;
  toursImpayes?: number[];
}

export interface TourDto {
  id: string;
  numero: number;
  beneficiaire?: MembreDto;
  status: string;
  dateOuverture: string;
  dateCloture?: string;
  dateLimite?: string;
}

export interface VersementDto {
  id: string;
  montant: number;
  payeurId: string;
  payeurNom: string;
  tourId: string;
  status: string;
  dateCreation: string;
  tourNumero?: number | null;
  reference?: string | null;
  dateConfirmation?: string | null;
}

export interface AuditEntryDto {
  id: string;
  action: string;
  acteurId: string;
  acteurNom?: string;
  timestamp: string;
  payload: string;
  hash: string;
}

export interface AuditEntriesResult {
  entries: AuditEntryDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  chaineValide?: boolean;
}

// ── Admin metrics ──────────────────────────────────────────────────────

export interface AdminMetrics {
  tontinesActives: number;
  totalMembres: number;
  volumeFcfaSemaine: number;
  smsEnvoyesSemaine: number;
  smsEchecsSemaine: number;
  tauxErreurApi: number;
  alertes: AlerteDto[];
}

export interface AlerteDto {
  id: string;
  type: string;
  message: string;
  severity: "info" | "warning" | "critical";
  timestamp: string;
  resolved: boolean;
}

// ── Paginated response ─────────────────────────────────────────────────

export interface PaginatedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// ── Auth ────────────────────────────────────────────────────────────────

export interface AuthResult {
  utilisateurId: string;
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
}

export type UserRole = "Membre" | "Gestionnaire" | "Admin";

// ── Projets collectifs ─────────────────────────────────────────────

export interface ProjetResume {
  id: string;
  nom: string;
  statut: string;
  role: "gestionnaire" | "participant";
  objectif: number;
  totalCollecte: number;
  progression: number;
  participants: number;
  monPalier: number | null;
  monResteDu: number | null;
  dateDebut: string;
}

export interface EcheanceProjet {
  numero: number;
  date: string;
  palier: number;
  paye: number;
  reste: number;
  statut: "soldee" | "partielle" | "en_retard" | "a_venir";
}

export interface ReglesProjet {
  paiementPartielAutorise: boolean;
  tropPercu: "Reporter" | "Refuser";
  hausseRetroactive: boolean;
  hausseSoumiseAValidation: boolean;
  baisseAutorisee: boolean;
  baisseSoumiseAValidation: boolean;
  baisseRetroactive: boolean;
  rattrapageAdhesionTardive: boolean;
}

export interface ParticipantProjet {
  id: string;
  nom: string;
  palierActuel: number;
  palierDemande: number | null;
  totalPaye: number;
  engagementTotal: number;
  resteDuExigible: number;
  avance: number;
  dateAdhesion: string;
  echeances: EcheanceProjet[] | null;
}

export interface PaiementProjet {
  id: string;
  participantId: string;
  participantNom: string;
  montant: number;
  preuve: string;
  reference: string | null;
  statut: "en_attente" | "valide" | "rejete";
  saisiLe: string;
  decideLe: string | null;
  motif: string | null;
}

export interface DepenseProjet {
  id: string;
  libelle: string;
  montant: number;
  justificatif: string;
  statut: "en_attente" | "valide" | "rejete";
  proposeeLe: string;
  decideeLe: string | null;
  motif: string | null;
}

export interface ProjetDetails {
  id: string;
  nom: string;
  description: string | null;
  objectif: number;
  devise: string;
  paliers: number[];
  periodicite: string;
  dateDebut: string;
  nombreEcheances: number;
  echeancier: string[];
  statut: "Brouillon" | "Ouvert" | "Clos" | "Annule";
  role: "gestionnaire" | "participant";
  monParticipantId: string | null;
  totalCollecte: number;
  totalDepense: number;
  solde: number;
  progression: number;
  journalValide: boolean | null;
  codeInvitationExpireLe: string | null;
  regles: ReglesProjet;
  participants: ParticipantProjet[];
  paiements: PaiementProjet[];
  depenses: DepenseProjet[];
  journal: { sequence: number; action: string; acteur: string; horodatage: string; hash: string }[] | null;
}
