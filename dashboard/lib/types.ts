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
