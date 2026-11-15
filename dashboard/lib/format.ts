const nf = new Intl.NumberFormat("fr-FR", { maximumFractionDigits: 0 });

/** 1500000 → "1 500 000 FCFA" */
export function formatMontant(amount: number): string {
  return `${nf.format(amount)} FCFA`;
}

/** 1500000 → "1 500 000" (no currency, for tables with a currency header) */
export function formatNombre(amount: number): string {
  return nf.format(amount);
}

/** Compact amount for axes and tight spaces: 12 500 → "12,5 k", 1 200 000 → "1,2 M" */
export function formatCompact(amount: number): string {
  return new Intl.NumberFormat("fr-FR", { notation: "compact", maximumFractionDigits: 1 }).format(amount);
}

export function maskMontant(amount: number, authorized: boolean): string {
  return authorized ? formatMontant(amount) : "*** FCFA";
}

/** "15 nov. 2026, 14:56" */
export function formatDate(dateStr: string): string {
  return new Intl.DateTimeFormat("fr-FR", { dateStyle: "medium", timeStyle: "short" }).format(new Date(dateStr));
}

/** "15 nov. 2026" */
export function formatJour(dateStr: string): string {
  return new Intl.DateTimeFormat("fr-FR", { dateStyle: "medium" }).format(new Date(dateStr));
}

const STATUTS_TONTINE: Record<string, string> = {
  Draft: "Brouillon", Active: "Active", Suspended: "Suspendue", Completed: "Terminée", Cancelled: "Annulée",
  Brouillon: "Brouillon", Ouvert: "Ouvert", Clos: "Clos", Annule: "Annulé",
};
export const libelleStatutGroupe = (s: string) => STATUTS_TONTINE[s] ?? s;

const PERIODICITES: Record<string, string> = { Monthly: "Mensuelle", Biweekly: "Bimensuelle", Weekly: "Hebdomadaire" };
export const libellePeriodicite = (p: string) => PERIODICITES[p] ?? p;

const ACTIONS: Record<string, string> = {
  VersementCree: "Versement créé",
  VersementManuel: "Paiement en espèces saisi",
  VersementConfirme: "Paiement validé",
  VersementRejete: "Paiement rejeté",
  ProjetCree: "Projet créé",
  ProjetOuvert: "Projet ouvert",
  ProjetClos: "Projet clos",
  CodeInvitationGenere: "Code d'invitation généré",
  ParticipantAjoute: "Participant ajouté",
  PalierAugmente: "Palier augmenté",
  PalierBaisse: "Palier baissé",
  ChangementPalierDemande: "Changement de palier demandé",
  ChangementPalierApprouve: "Changement de palier approuvé",
  ChangementPalierRefuse: "Changement de palier refusé",
  BaissePalierDemandee: "Baisse de palier demandée",
  BaissePalierApprouvee: "Baisse de palier approuvée",
  BaissePalierRefusee: "Baisse de palier refusée",
  PaiementSaisi: "Paiement saisi",
  PaiementValide: "Paiement validé",
  PaiementRejete: "Paiement rejeté",
  DepenseProposee: "Dépense proposée",
  DepenseValidee: "Dépense validée",
  DepenseRejetee: "Dépense rejetée",
  ReglesModifiees: "Règles modifiées",
};
export const libelleAction = (a: string) => ACTIONS[a] ?? a;
