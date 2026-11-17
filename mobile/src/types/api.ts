/**
 * API response types matching the ASP.NET Core backend DTOs.
 */

/** Mirrors Application.IdentityManagement.DTOs.AuthResult */
export interface RegisterResult {
  utilisateurId: string;
  message: string;
}

export interface AuthResult {
  utilisateurId: string;
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
}

/** POST /api/v1/auth/register body */
export interface RegisterRequest {
  telephone: string;
  nom: string;
  motDePasse: string;
}

/** POST /api/v1/auth/login body */
export interface LoginRequest {
  telephone: string;
  motDePasse: string;
}

/** POST /api/v1/auth/refresh body */
export interface RefreshRequest {
  refreshToken: string;
}

/** Generic API error shape returned by the backend */
export interface ApiError {
  error: string;
}

/** Tontine summary for list views */
export interface TontineSummary {
  id: string;
  nom: string;
  description: string;
  status: TontineStatus;
  montantCotisation: number;
  devise: string;
  frequence: string;
  nombreMembres: number;
  dateCreation: string;
  /** Current tour info for badge display */
  tourActuel: TourInfo | null;
}

export type TontineStatus =
  | 'Draft'
  | 'Active'
  | 'Suspended'
  | 'Completed'
  | 'Cancelled';

/** Payment status for a member in a tour */
export type StatutPaiement = 'paye' | 'paye_partiel' | 'en_attente' | 'a_payer' | 'en_retard' | 'non_du' | 'adhesion_en_attente';

/** Tontine detail */
export interface TontineDetail extends TontineSummary {
  gestionnaireName: string;
  gestionnaireId: string;
  membres: TontineMember[];
  /** Every round opened so far (the open one and the closed ones), in order. */
  tours: TontineTour[];
}

export interface TontineTour {
  id: string;
  numero: number;
  estOuvert: boolean;
  beneficiaireNom: string;
}

export interface TontineMember {
  id: string;
  nom: string;
  telephone: string;
  dateAdhesion: string;
  /** Payment status in current tour */
  statutPaiement?: StatutPaiement;
  /** Contributions owed for earlier rounds (FCFA), including a late member's catch-up */
  resteDu?: number;
  toursImpayes?: number[];
  /** Remainder of each round in toursImpayes (same order), FCFA */
  restesImpayes?: number[];
  /** Still to declare on the open round (contribution − validated − pending), FCFA */
  resteTour?: number;
  /** Declared on the open round and waiting for the trésorier's validation, FCFA */
  enAttenteTour?: number;
  /** Validated surplus beyond every round owed so far: it will cover the next rounds, FCFA */
  avance?: number;
  /** All the member's money waiting for validation (any round), FCFA */
  enAttenteTotal?: number;
  /** Part of the pending money beyond what the rounds still owe, FCFA */
  enAttenteExcedent?: number;
  /** Validated money carried into the open round from other rounds' surplus, FCFA */
  reporteTour?: number;
  /** Pending money on the open round beyond what it still owes, FCFA */
  enAttenteExcedentTour?: number;
  /** Linked account, when the member has one */
  utilisateurId?: string | null;
  /** Raw status in the current round (paye, paye_partiel, en_attente, a_payer, en_retard, non_du, beneficiaire, suspendu, adhesion_en_attente) */
  statutTour?: string;
}

export interface TourInfo {
  id: string;
  numero: number;
  beneficiaireNom: string;
  dateOuverture: string;
  dateCloture: string | null;
  estOuvert: boolean;
  /** Members whose validated money (after carry-over) covers the round — same as /tours/actuel */
  nombrePaiementsRecus: number;
  /** Members owing this round (beneficiary and members not owing it excluded) */
  nombrePaiementsAttendus: number;
}

/** User profile */
export interface UserProfile {
  id: string;
  telephone: string;
  nom: string;
  dateInscription: string;
}

// ─── Payment types ─────────────────────────────────────────────────────────────

/** POST /api/v1/versements/initier body */
export interface InitierVersementRequest {
  tontineId: string;
  tourId: string;
  montant: number;
}

/** Payment initiation response */
export interface InitierVersementResponse {
  versementId: string;
  statut: VersementStatut;
  /** External transaction reference from the payment provider */
  transactionRef: string;
}

/** Versement status */
export type VersementStatut = 'en_attente' | 'confirme' | 'rejete';

/** GET /api/v1/versements/:id status response */
export interface VersementStatusResponse {
  versementId: string;
  statut: VersementStatut;
  montant: number;
  dateCreation: string;
  dateConfirmation: string | null;
}

// ─── Gestionnaire types ────────────────────────────────────────────────────────

/** Late member info for the gestionnaire screen */
export interface MembreRetardataire {
  membreId: string;
  nom: string;
  telephone: string;
  joursRetard: number;
}

/** POST /api/v1/tontines/:id/tours/:tourId/relancer body */
export interface RelancerSmsRequest {
  membreIds: string[];
}

/** POST /api/v1/tontines/:id/tours/:tourId/clore response */
export interface CloreTourResponse {
  tourId: string;
  estCloture: boolean;
}

// ─── Credit Scoring types ──────────────────────────────────────────────────────

/** GET /api/v1/membres/:id/profil-credit response */
export interface ProfilCreditResponse {
  membreId: string;
  score: number;
  niveau: ProfilCreditNiveau;
  donneesInsuffisantes: boolean;
  composantes: ComposantesScore;
  calculeLe: string;
}

export type ProfilCreditNiveau = 'Excellent' | 'Bon' | 'Moyen' | 'Faible';

export interface ComposantesScore {
  cyclesCompletes: number;
  tauxPonctualite: number;
  ancienneteEnMois: number;
  contributionCycles: number;
  contributionPonctualite: number;
  contributionAnciennete: number;
}

// ─── Create Tontine types ──────────────────────────────────────────────────────

/** POST /api/v1/tontines body */
export interface CreateTontineRequest {
  name: string;
  description: string;
  contributionAmount: number;
  periodicity: string;
  maxMembers: number;
}

/** POST /api/v1/tontines response */
export interface CreateTontineResponse {
  id: string;
}

// ─── OTP verification ──────────────────────────────────────────────────────────

/** POST /api/v1/auth/verifier-otp body */
export interface VerifierOtpRequest {
  telephone: string;
  code: string;
}

// ─── Invitation types ──────────────────────────────────────────────────────────

/** GET /api/v1/tontines/:id/invitation/generer response */
export interface GenererCodeInvitationResponse {
  code: string;
  deepLink: string;
  expiration: string;
}

/** POST /api/v1/tontines/rejoindre body */
export interface RejoindreParCodeRequest {
  code: string;
  memberName: string;
}

// ─── Versement history ─────────────────────────────────────────────────────────

/** GET /api/v1/membres/moi/versements item */
export interface VersementDto {
  id: string;
  tontineId: string;
  payeurId: string;
  tourId: string;
  montant: number;
  devise: string;
  statut: string;
  referenceExterne: string | null;
  createdAt: string;
  confirmedAt: string | null;
}

// ─── SMS preferences ──────────────────────────────────────────────────────────

/** PUT /api/v1/membres/moi/sms-preferences body */
export interface UpdateSmsPreferencesRequest {
  optOut: boolean;
}

// ─── Notifications ─────────────────────────────────────────────────────────────

/** In-app notification item */
export interface NotificationItem {
  id: string;
  titre: string;
  message: string;
  type: string;
  dateCreation: string;
  lu: boolean;
}

// ─── Tour actuel ───────────────────────────────────────────────────────────────

/** GET /api/v1/tontines/:id/tour-actuel response */
export interface TourActuelDto {
  tourId: string;
  numero: number;
  beneficiaireNom: string;
  dateOuverture: string;
  dateCloture: string | null;
  estOuvert: boolean;
  montantAttendu: number;
  montantCollecte: number;
  nombrePaiementsRecus: number;
  nombrePaiementsAttendus: number;
  membres: TourMembreStatutDto[];
}

export interface TourMembreStatutDto {
  membreId: string;
  nom: string;
  aPaye: boolean;
  montant: number | null;
  joursRetard: number;
}
