/**
 * Tontine API service.
 * Communicates with /api/v1/tontines/* endpoints.
 * Maps backend DTOs (English camelCase) to mobile types (French camelCase).
 */

import apiClient from './apiClient';
import type {
  TontineSummary,
  TontineDetail,
  TontineStatus,
  StatutPaiement,
  MembreRetardataire,
  RelancerSmsRequest,
  CloreTourResponse,
  CreateTontineRequest,
  CreateTontineResponse,
  GenererCodeInvitationResponse,
  RejoindreParCodeRequest,
  VersementDto,
  UpdateSmsPreferencesRequest,
  TourActuelDto,
} from '../types/api';

// ─── Backend DTO shapes (actual JSON from ASP.NET Core) ────────────────────────

/** Matches Application.TontineManagement.Queries.GetTontineById.TontineDto */
interface BackendTontineDto {
  id: string;
  name: string;
  description: string | null;
  contributionAmount: number;
  currency: string;
  periodicity: string;
  status: string;
  maxMembers: number;
  currentMemberCount: number;
  gestionnaireId: string;
  createdAt: string;
}

// ─── Mappers ───────────────────────────────────────────────────────────────────

function mapToTontineSummary(dto: BackendTontineDto): TontineSummary {
  return {
    id: dto.id,
    nom: dto.name,
    description: dto.description ?? '',
    status: dto.status as TontineStatus,
    montantCotisation: dto.contributionAmount,
    devise: dto.currency,
    frequence: dto.periodicity,
    nombreMembres: dto.currentMemberCount,
    dateCreation: dto.createdAt,
    tourActuel: null, // Tour info not included in list endpoint yet
  };
}

/** GET /tontines/{id}/details — tontine with members (status in the current round) and rounds */
interface BackendTontineDetailsDto {
  id: string;
  nom: string;
  description: string;
  montantCotisation: number;
  devise: string;
  periodicite: string;
  maxMembres: number;
  status: string;
  membres: {
    id: string;
    nom: string;
    telephone: string | null;
    statut: string; // paye | en_attente | a_payer | en_retard | beneficiaire | suspendu
    utilisateurId: string | null;
    rang: number;
  }[];
  tours: {
    id: string;
    numero: number;
    beneficiaire: { nom: string } | null;
    status: string; // Ouvert | Cloture
    dateOuverture: string;
    dateCloture: string | null;
    dateLimite: string;
  }[];
  createdAt: string;
  gestionnaireId: string;
  gestionnaireNom: string | null;
}

function mapStatutPaiement(statut: string): StatutPaiement | undefined {
  if (statut === 'paye') return 'paye';
  if (statut === 'en_retard') return 'en_retard';
  if (statut === 'en_attente' || statut === 'a_payer') return 'en_attente';
  return undefined; // beneficiaire, suspendu
}

function mapToTontineDetail(dto: BackendTontineDetailsDto): TontineDetail {
  const tour = dto.tours.find((t) => t.status === 'Ouvert');
  const payeurs = dto.membres.filter((m) => m.statut !== 'beneficiaire' && m.statut !== 'suspendu');
  return {
    id: dto.id,
    nom: dto.nom,
    description: dto.description,
    status: dto.status as TontineStatus,
    montantCotisation: dto.montantCotisation,
    devise: dto.devise,
    frequence: dto.periodicite,
    nombreMembres: dto.membres.length,
    dateCreation: dto.createdAt,
    gestionnaireId: dto.gestionnaireId,
    gestionnaireName: dto.gestionnaireNom ?? '',
    membres: dto.membres.map((m) => ({
      id: m.id,
      nom: m.nom,
      telephone: m.telephone ?? '',
      dateAdhesion: '',
      statutPaiement: mapStatutPaiement(m.statut),
    })),
    tourActuel: tour
      ? {
          id: tour.id,
          numero: tour.numero,
          beneficiaireNom: tour.beneficiaire?.nom ?? '',
          dateOuverture: tour.dateOuverture,
          dateCloture: tour.dateLimite,
          estOuvert: true,
          nombrePaiementsRecus: payeurs.filter((m) => m.statut === 'paye').length,
          nombrePaiementsAttendus: payeurs.length,
        }
      : null,
  };
}

// ─── Service ───────────────────────────────────────────────────────────────────

export const tontineService = {
  /**
   * GET /tontines/mes-tontines
   * Fetch user's active tontines.
   */
  async getMyTontines(): Promise<TontineSummary[]> {
    const response = await apiClient.get<BackendTontineDto[]>('/tontines/mes-tontines');
    return response.data.map(mapToTontineSummary);
  },

  /**
   * GET /tontines/:id
   * Get detailed tontine information.
   */
  async getTontineById(id: string): Promise<TontineDetail> {
    const response = await apiClient.get<BackendTontineDetailsDto>(`/tontines/${id}/details`);
    return mapToTontineDetail(response.data);
  },

  /**
   * GET /tontines/:id/tours/:tourId/retardataires
   * Get late members for the current tour.
   */
  async getRetardataires(
    tontineId: string,
    tourId: string,
  ): Promise<MembreRetardataire[]> {
    const response = await apiClient.get<MembreRetardataire[]>(
      `/tontines/${tontineId}/tours/${tourId}/retardataires`,
    );
    return response.data;
  },

  /**
   * POST /tontines/:id/tours/:tourId/relancer
   * Send SMS reminders to late members.
   */
  async relancerSms(
    tontineId: string,
    tourId: string,
    data: RelancerSmsRequest,
  ): Promise<void> {
    await apiClient.post(
      `/tontines/${tontineId}/tours/${tourId}/relancer`,
      data,
    );
  },

  /**
   * POST /tontines/:id/tours/:tourId/clore
   * Close the current tour (gestionnaire only).
   */
  async cloreTour(
    tontineId: string,
    tourId: string,
  ): Promise<CloreTourResponse> {
    await apiClient.post(`/tontines/${tontineId}/rounds/${tourId}/close`);
    return { tourId, estCloture: true };
  },

  /** Draft → Active: opens round 1 (requires the minimum number of members). */
  async activerTontine(tontineId: string): Promise<void> {
    await apiClient.post(`/tontines/${tontineId}/activate`);
  },

  async createTontine(data: CreateTontineRequest): Promise<CreateTontineResponse> {
    const response = await apiClient.post<CreateTontineResponse>('/tontines', data);
    return response.data;
  },

  /**
   * GET /tontines/:id/invitation/generer
   * Generate an invitation code (gestionnaire only).
   */
  async genererCodeInvitation(tontineId: string): Promise<GenererCodeInvitationResponse> {
    const response = await apiClient.get<GenererCodeInvitationResponse>(
      `/tontines/${tontineId}/invitation/generer`,
    );
    return response.data;
  },

  /**
   * POST /tontines/rejoindre
   * Join a tontine using an invitation code.
   */
  async rejoindreParCode(data: RejoindreParCodeRequest): Promise<void> {
    await apiClient.post('/tontines/rejoindre', data);
  },

  /**
   * GET /membres/moi/versements
   * Get all payments made by the authenticated user.
   */
  async getMesVersements(tontineId?: string): Promise<VersementDto[]> {
    const params = tontineId ? { tontineId } : {};
    const response = await apiClient.get<VersementDto[]>('/membres/moi/versements', { params });
    return response.data;
  },

  /**
   * PUT /membres/moi/sms-preferences
   * Update SMS opt-out preference.
   */
  async updateSmsPreferences(data: UpdateSmsPreferencesRequest): Promise<void> {
    await apiClient.put('/membres/moi/sms-preferences', data);
  },

  /**
   * GET /tontines/:id/tour-actuel
   * Get the current tour info for a tontine.
   */
  async getTourActuel(tontineId: string): Promise<TourActuelDto | null> {
    try {
      const response = await apiClient.get<TourActuelDto>(`/tontines/${tontineId}/tours/actuel`);
      return response.data;
    } catch {
      return null;
    }
  },
} as const;
