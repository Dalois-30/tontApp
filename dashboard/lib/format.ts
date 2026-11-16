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

/**
 * Label of a member's status in the open round when part of it is paid or pending. A partly paid round is
 * never shown as a plain "Payé": "Payé partiellement · reste 6 000 FCFA" (and/or "· 6 000 FCFA à valider");
 * a pending payment smaller than the contribution: "4 000 FCFA à valider · reste 6 000 FCFA".
 * undefined: the default label of the status applies.
 */
export function libelleStatutMembre(m: { statut: string; resteTour?: number; enAttenteTour?: number }): string | undefined {
  const reste = m.resteTour ?? 0;
  const aValider = m.enAttenteTour ?? 0;
  if (m.statut === "paye_partiel") {
    const parts = ["Payé partiellement"];
    if (reste > 0) parts.push(`reste ${formatMontant(reste)}`);
    if (aValider > 0) parts.push(`${formatMontant(aValider)} à valider`);
    return parts.join(" · ");
  }
  if (m.statut === "en_attente" && reste > 0)
    return `${aValider > 0 ? `${formatMontant(aValider)} à valider` : "En attente"} · reste ${formatMontant(reste)}`;
  return undefined;
}

/** Statuses of members who owe nothing on the open round, whatever the amounts. */
const SANS_DU = new Set(["beneficiaire", "suspendu", "adhesion_en_attente"]);

/** Whether the member still owes something on the open round (a reminder can be sent). */
export function doitSurLeTour(m: { statut: string; resteTour?: number }, cotisation: number): boolean {
  return !SANS_DU.has(m.statut) && resteTourOuvert(m, cotisation) > 0;
}

/** What remains to collect on the open round; falls back on the status for an older API. */
export function resteTourOuvert(m: { statut: string; resteTour?: number }, cotisation: number): number {
  if (m.resteTour !== undefined) return m.resteTour;
  return m.statut === "a_payer" || m.statut === "en_retard" ? cotisation : 0;
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
  ParticipantInscrit: "Participant inscrit",
  PaiementImporte: "Paiement importé du registre",
};
export const libelleAction = (a: string) => ACTIONS[a] ?? a;

// ── Bureau roles (same rights as the API: the responsable runs the tontine, the trésorier validates) ──

type AvecRoles = { mesRoles?: string[] | null } | null | undefined;

/** Responsable (or admin): adhesions, rules, reminders, activation. */
export function peutGerer(t: AvecRoles): boolean {
  const r = t?.mesRoles ?? [];
  return r.includes("responsable") || r.includes("admin");
}

/** Trésorier (or admin): validates or rejects payments. */
export function peutValider(t: AvecRoles): boolean {
  const r = t?.mesRoles ?? [];
  return r.includes("tresorier") || r.includes("admin");
}

/** "Responsable · Trésorier", "Trésorier", "Administrateur"… */
export function libelleRolesBureau(t: AvecRoles): string {
  const r = t?.mesRoles ?? [];
  const l = [r.includes("responsable") && "Responsable", r.includes("tresorier") && "Trésorier"].filter(Boolean) as string[];
  if (l.length === 0 && r.includes("admin")) l.push("Administrateur");
  return l.join(" · ");
}

/** Shown when the API refuses the bureau's space (403): the account sits on no bureau. */
export const MESSAGE_HORS_BUREAU =
  "Cet espace est réservé au bureau d'une tontine : son responsable et son trésorier. Vous n'êtes responsable ni trésorier d'aucune tontine ; suivez vos cotisations depuis l'application mobile.";
