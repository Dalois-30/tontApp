/**
 * Navigation type definitions for React Navigation v7.
 * Defines the param lists for AuthStack and AppStack.
 */

export type AuthStackParamList = {
  Onboarding: undefined;
  Login: undefined;
  Register: undefined;
  VerifierOtp: { telephone: string; nom: string };
};

export type AppStackParamList = {
  Home: undefined;
  TontineDetail: { tontineId: string };
  DeclarerVersement: { tontineId: string };
  TontineVersements: { tontineId: string; aValider?: boolean };
  Paiement: { tontineId: string; tourId: string; montant: number };
  Gestionnaire: { tontineId: string; tourId: string };
  CreateTontine: undefined;
  Profil: undefined;
  RejoindreParCode: undefined;
  Invitation: { tontineId: string; tontineNom: string; montant: number };
  HistoriqueVersements: { tontineId?: string } | undefined;
  Notifications: undefined;
  ProjetDetail: { projetId: string };
  RejoindreProjet: undefined;
  ProjetMembres: { projetId: string };
  ProjetMembre: { projetId: string; participantId: string };
  ProjetPaiements: { projetId: string };
  DeclarerPaiement: { projetId: string };
  ProjetValidations: { projetId: string };
  ProjetDepenses: { projetId: string };
  Gouvernance: { type: 'projets' | 'tontines'; id: string };
  Seances: { type: 'projets' | 'tontines'; id: string };
  SeanceForm: { type: 'projets' | 'tontines'; id: string };
  Chronologie: { type: 'projets' | 'tontines'; id: string; participantId?: string; titre?: string };
};
