// Tontine chosen in the bureau's space: carried by the URL (?tontine=<id>) so that it follows the user
// across pages and a link can be shared. No parameter = the API's default tontine.
import { MESSAGE_HORS_BUREAU } from "./format";
import type { RoleBureau } from "./types";

export const PARAM_TONTINE = "tontine";

export const UUID_REGEX =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** A tontine where the caller sits on the bureau (GET /dashboard/gestionnaire/tontines). */
export interface TontineBureau {
  id: string;
  nom: string;
  status: string;
  mesRoles: RoleBureau[];
}

/**
 * The tontine asked by the URL: null when none is asked (default tontine), "invalide" when the parameter
 * is not an id (shown as an unknown tontine rather than silently falling back to the default one).
 */
export function tontineDemandee(valeur: string | string[] | null | undefined): string | null | "invalide" {
  const v = Array.isArray(valeur) ? valeur[0] : valeur;
  if (v === undefined || v === null || v === "") return null;
  return UUID_REGEX.test(v) ? v : "invalide";
}

/** The href with the chosen tontine kept (unchanged when none is chosen). */
export function avecTontine(href: string, tontineId: string | null | undefined): string {
  if (!tontineId) return href;
  return `${href}${href.includes("?") ? "&" : "?"}${PARAM_TONTINE}=${encodeURIComponent(tontineId)}`;
}

/** "&tontineId=<id>" for the dashboard's proxies, empty for the default tontine. */
export function requeteTontine(tontineId: string | null | undefined): string {
  return tontineId ? `&tontineId=${encodeURIComponent(tontineId)}` : "";
}

/** Why the bureau's space cannot show a tontine. */
export type EchecTontine = "hors_bureau" | "aucune" | "erreur";

/** 403 → hors_bureau, 404 → aucune, anything else → erreur. */
export function echecDuStatut(status: number): EchecTontine {
  return status === 403 ? "hors_bureau" : status === 404 ? "aucune" : "erreur";
}

/**
 * Icon, title and text of a failure screen. With a tontine chosen by the URL, a 403 means "not on the bureau
 * of this tontine" and a 404 an unknown tontine; without, they mean no bureau at all / no tontine yet.
 */
export function ecranEchec(echec: EchecTontine, choisie: boolean, quoi: string /* e.g. "son journal apparaîtra ici" */): [string, string, string] {
  if (echec === "hors_bureau")
    return choisie
      ? ["shield", "Accès réservé au bureau de cette tontine",
        "Seuls le responsable et le trésorier de cette tontine peuvent l'ouvrir ici. Choisissez une de vos tontines dans le menu."]
      : ["shield", "Vous n'êtes au bureau d'aucune tontine", MESSAGE_HORS_BUREAU];
  if (echec === "aucune")
    return choisie
      ? ["alert", "Tontine introuvable", "Le lien ne correspond à aucune tontine. Choisissez une de vos tontines dans le menu."]
      : ["users", "Aucune tontine pour le moment", `Créez votre tontine depuis l'application mobile : ${quoi}.`];
  return ["alert", "Impossible de charger la tontine", "Le serveur n'a pas répondu correctement. Réessayez dans un instant."];
}
