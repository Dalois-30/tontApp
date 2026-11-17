import { describe, expect, it } from 'vitest';
import { formatMontant, libellePeriodicite, libelleStatut, libelleStatutTour } from './format';

describe('formatMontant', () => {
  it('sépare les milliers et ajoute FCFA', () => {
    expect(formatMontant(0)).toBe('0 FCFA');
    expect(formatMontant(5000)).toBe('5 000 FCFA');
    expect(formatMontant(1500000)).toBe('1 500 000 FCFA');
  });
  it('arrondit au franc', () => {
    expect(formatMontant(2499.6)).toBe('2 500 FCFA');
  });
});

describe('libelleStatutTour', () => {
  it('payé sans détail : libellé par défaut', () => {
    expect(libelleStatutTour('paye', 0, 0)).toBeUndefined();
  });
  it('payé grâce à un report : « dont … reportés »', () => {
    expect(libelleStatutTour('paye', 0, 0, { reporte: 3000 })).toBe('Payé · dont 3 000 FCFA reportés');
  });
  it('payé avec un excédent en attente', () => {
    expect(libelleStatutTour('paye', 0, 8000, { excedent: 8000 }))
      .toBe('Payé · 8 000 FCFA à valider (dont 8 000 FCFA en excédent)');
  });
  it('payé partiellement : jamais un simple « Payé », avec le reste', () => {
    expect(libelleStatutTour('paye_partiel', 6000, 0)).toBe('Payé partiellement · reste 6 000 FCFA');
  });
  it('payé partiellement avec une part à valider et un report', () => {
    expect(libelleStatutTour('paye_partiel', 2000, 4000, { reporte: 1000 }))
      .toBe('Payé partiellement · reste 2 000 FCFA · dont 1 000 FCFA reportés · 4 000 FCFA à valider');
  });
  it('payé partiellement, reste couvert par l’attente', () => {
    expect(libelleStatutTour('paye_partiel', 0, 6000)).toBe('Payé partiellement · 6 000 FCFA à valider');
  });
  it('attente partielle : montant à valider et reste', () => {
    expect(libelleStatutTour('en_attente', 6000, 4000)).toBe('4 000 FCFA à valider · reste 6 000 FCFA');
  });
  it('attente au-delà de ce que le tour doit : excédent signalé', () => {
    expect(libelleStatutTour('en_attente', 0, 13000, { excedent: 3000 }))
      .toBe('13 000 FCFA à valider (dont 3 000 FCFA en excédent)');
  });
  it('attente couvrant exactement le tour : libellé par défaut', () => {
    expect(libelleStatutTour('en_attente', 0, 10000)).toBeUndefined();
  });
  it('autres statuts (non dû, à payer, en retard, inconnu) : libellé par défaut', () => {
    for (const s of ['non_du', 'a_payer', 'en_retard', 'beneficiaire', undefined]) {
      expect(libelleStatutTour(s, 10000, 0)).toBeUndefined();
    }
  });
});

describe('libelleStatut / libellePeriodicite', () => {
  it('traduit les statuts de tontine et de projet, garde l’inconnu', () => {
    expect(libelleStatut('Active')).toBe('Active');
    expect(libelleStatut('Draft')).toBe('Brouillon');
    expect(libelleStatut('Completed')).toBe('Terminée');
    expect(libelleStatut('Annule')).toBe('Annulé');
    expect(libelleStatut('Inconnu')).toBe('Inconnu');
  });
  it('traduit les périodicités', () => {
    expect(libellePeriodicite('Monthly')).toBe('Mensuelle');
    expect(libellePeriodicite('Weekly')).toBe('Hebdomadaire');
    expect(libellePeriodicite('Daily')).toBe('Daily');
  });
});
