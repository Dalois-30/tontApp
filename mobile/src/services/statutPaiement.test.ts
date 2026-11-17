import { describe, expect, it } from 'vitest';
import { mapStatutPaiement } from './statutPaiement';

describe('mapStatutPaiement', () => {
  it('garde les statuts de paiement du tour, dont le partiel et le non dû', () => {
    for (const s of ['paye', 'paye_partiel', 'en_retard', 'en_attente', 'a_payer', 'adhesion_en_attente', 'non_du'] as const) {
      expect(mapStatutPaiement(s)).toBe(s);
    }
  });
  it('aucun statut de paiement pour le bénéficiaire, le suspendu ou un statut inconnu', () => {
    expect(mapStatutPaiement('beneficiaire')).toBeUndefined();
    expect(mapStatutPaiement('suspendu')).toBeUndefined();
    expect(mapStatutPaiement('autre')).toBeUndefined();
  });
});
