import { describe, expect, it } from 'vitest';
import type { ProjetDetails } from '../../../services/projetService';
import { ECHEANCE, moyen, PAIEMENT, roles, STATUT_PARTICIPANT } from './statuts';

describe('statuts des projets', () => {
  it('échéances', () => {
    expect(ECHEANCE.soldee).toEqual({ label: 'Soldée', ton: 'bon' });
    expect(ECHEANCE.partielle).toEqual({ label: 'Partielle', ton: 'attente' });
    expect(ECHEANCE.en_retard).toEqual({ label: 'En retard', ton: 'mauvais' });
    expect(ECHEANCE.a_venir).toEqual({ label: 'À venir', ton: 'neutre' });
  });
  it('paiements', () => {
    expect(PAIEMENT.valide.label).toBe('Validé');
    expect(PAIEMENT.en_attente.label).toBe('À valider');
    expect(PAIEMENT.rejete.label).toBe('Rejeté');
  });
  it('participants', () => {
    expect(STATUT_PARTICIPANT.Actif.label).toBe('Actif');
    expect(STATUT_PARTICIPANT.Suspendu.label).toBe('Suspendu');
    expect(STATUT_PARTICIPANT.Demissionnaire.label).toBe('Démissionnaire');
    expect(STATUT_PARTICIPANT.Exclu.label).toBe('Exclu');
  });
  it('moyen : retire le préfixe d’import du registre, garde toute autre preuve', () => {
    expect(moyen('Import du registre · OM')).toBe('OM');
    expect(moyen('Reçu 123')).toBe('Reçu 123');
  });
});

describe('roles (projet)', () => {
  const p = (mesRoles: string[]) => ({ mesRoles }) as unknown as ProjetDetails;
  it('sans projet chargé : aucun droit', () => {
    expect(roles(undefined)).toEqual({ responsable: false, tresorier: false });
  });
  it('responsable seul, trésorier seul, cumul, admin, simple participant', () => {
    expect(roles(p(['responsable']))).toEqual({ responsable: true, tresorier: false });
    expect(roles(p(['tresorier']))).toEqual({ responsable: false, tresorier: true });
    expect(roles(p(['responsable', 'tresorier']))).toEqual({ responsable: true, tresorier: true });
    expect(roles(p(['admin']))).toEqual({ responsable: true, tresorier: true });
    expect(roles(p(['participant']))).toEqual({ responsable: false, tresorier: false });
    expect(roles(p([]))).toEqual({ responsable: false, tresorier: false });
  });
});
