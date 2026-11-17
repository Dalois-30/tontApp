/**
 * Form fields pre-filled with a proposal the user can overwrite, with no React Native dependency.
 * A typed value is tagged with the context it was typed in (member, round…): it is shown only while that
 * context is current, so a change of member or round drops it and the proposal comes back, while a mere
 * refetch keeps it. An emptied field stays empty ('' is a typed value, not a missing one).
 */
export type Saisie = { cle: string; valeur: string };

/** The typed value while its context is current, else null (show the proposal). */
export const saisieCourante = (saisie: Saisie | null, cle: string): string | null =>
  saisie !== null && saisie.cle === cle ? saisie.valeur : null;

/** Name to show in a form pre-filled from the profile: what the user typed, else the profile name. */
export const nomPrerempli = (saisi: string | null, nomProfil: string | null | undefined): string =>
  saisi ?? nomProfil ?? '';
