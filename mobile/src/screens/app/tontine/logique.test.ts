import { describe, expect, it } from 'vitest';
import type { TontineDetail, TontineMember } from '../../../types/api';
import { cleSaisieVersement, monMembre, montantPropose, rolesTontine, STATUT_VERSEMENT, tourParDefaut, toursDus } from './logique';
import { saisieCourante, type Saisie } from '../../../utils/saisie';

const COTISATION = 10000;

/** A tontine with rounds 1 and 2 closed and round 3 open. */
function tontine(): TontineDetail {
  return {
    id: 't1', nom: 'Tontine', description: '', status: 'Active', montantCotisation: COTISATION, devise: 'XAF',
    frequence: 'Monthly', nombreMembres: 3, dateCreation: '', tourActuel: null,
    gestionnaireName: '', gestionnaireId: '', membres: [],
    tours: [
      { id: 'r1', numero: 1, estOuvert: false, beneficiaireNom: 'A' },
      { id: 'r2', numero: 2, estOuvert: false, beneficiaireNom: 'B' },
      { id: 'r3', numero: 3, estOuvert: true, beneficiaireNom: 'C' },
    ],
  };
}

const membre = (m: Partial<TontineMember>): TontineMember =>
  ({ id: 'm1', nom: 'Awa', telephone: '', dateAdhesion: '', ...m });

const resume = (dus: ReturnType<typeof toursDus>) => dus.map((d) => [d.numero, d.reste]);

describe('toursDus', () => {
  it('rien sans tontine ou sans membre', () => {
    expect(toursDus(undefined, membre({}))).toEqual([]);
    expect(toursDus(tontine(), undefined)).toEqual([]);
  });

  it('rien pour un membre suspendu ou dont l’adhésion attend, même avec des arriérés', () => {
    for (const statutTour of ['suspendu', 'adhesion_en_attente']) {
      expect(toursDus(tontine(), membre({ statutTour, resteTour: COTISATION, toursImpayes: [1], restesImpayes: [COTISATION] })))
        .toEqual([]);
    }
  });

  it('tour ouvert à payer en entier', () => {
    expect(resume(toursDus(tontine(), membre({ statutTour: 'a_payer', resteTour: COTISATION })))).toEqual([[3, COTISATION]]);
  });

  it('tour ouvert payé partiellement : seul le reste est proposé', () => {
    const dus = toursDus(tontine(), membre({ statutTour: 'paye_partiel', resteTour: 6000 }));
    expect(resume(dus)).toEqual([[3, 6000]]);
    expect(dus[0].id).toBe('r3');
  });

  it('tour ouvert couvert par un paiement en attente : non proposé', () => {
    expect(toursDus(tontine(), membre({ statutTour: 'en_attente', resteTour: 0, enAttenteTour: COTISATION }))).toEqual([]);
  });

  it('attente partielle sur le tour ouvert : le reste est proposé', () => {
    expect(resume(toursDus(tontine(), membre({ statutTour: 'en_attente', resteTour: 6000, enAttenteTour: 4000 }))))
      .toEqual([[3, 6000]]);
  });

  it('tour ouvert payé : non proposé', () => {
    expect(toursDus(tontine(), membre({ statutTour: 'paye', resteTour: 0 }))).toEqual([]);
  });

  it('repli ancienne API sans resteTour : à payer ou en retard doit la cotisation, les autres statuts rien', () => {
    expect(resume(toursDus(tontine(), membre({ statutTour: 'a_payer' })))).toEqual([[3, COTISATION]]);
    expect(resume(toursDus(tontine(), membre({ statutTour: 'en_retard' })))).toEqual([[3, COTISATION]]);
    for (const statutTour of ['paye', 'paye_partiel', 'en_attente']) {
      expect(toursDus(tontine(), membre({ statutTour }))).toEqual([]);
    }
  });

  it('non dû ou bénéficiaire du tour ouvert : le tour ouvert n’est pas proposé, même avec un reste', () => {
    for (const statutTour of ['non_du', 'beneficiaire']) {
      expect(toursDus(tontine(), membre({ statutTour, resteTour: COTISATION }))).toEqual([]);
    }
  });

  it('non dû sur le tour ouvert mais des arriérés : les tours passés restent proposés', () => {
    expect(resume(toursDus(tontine(), membre({ statutTour: 'non_du', resteTour: 0, toursImpayes: [1], restesImpayes: [4000] }))))
      .toEqual([[1, 4000]]);
  });

  it('arriérés : chaque tour impayé porte le reste aligné sur sa position, après le tour ouvert', () => {
    const m = membre({ statutTour: 'en_retard', resteTour: COTISATION, toursImpayes: [1, 2], restesImpayes: [4000, 7000] });
    expect(resume(toursDus(tontine(), m))).toEqual([[3, COTISATION], [1, 4000], [2, 7000]]);
  });

  it('arriérés sans restesImpayes (ancienne API) : la cotisation entière par tour', () => {
    const m = membre({ statutTour: 'paye', resteTour: 0, toursImpayes: [2] });
    expect(resume(toursDus(tontine(), m))).toEqual([[2, COTISATION]]);
  });

  it('arriéré couvert (reste 0), tour inconnu ou tour ouvert listé en impayé : jamais proposé ni en double', () => {
    const m = membre({ statutTour: 'a_payer', resteTour: 3000, toursImpayes: [1, 9, 3, 2], restesImpayes: [0, 5000, 3000, 2500] });
    expect(resume(toursDus(tontine(), m))).toEqual([[3, 3000], [2, 2500]]);
  });

  it('tour impayé listé deux fois : proposé une seule fois, avec son premier reste', () => {
    const m = membre({ statutTour: 'paye', resteTour: 0, toursImpayes: [1, 2, 1], restesImpayes: [4000, 7000, 9000] });
    expect(resume(toursDus(tontine(), m))).toEqual([[1, 4000], [2, 7000]]);
  });

  it('restesImpayes non alignés sur toursImpayes : ignorés pour tous les tours (cotisation entière), jamais mélangés', () => {
    const plusCourt = membre({ statutTour: 'paye', resteTour: 0, toursImpayes: [1, 2], restesImpayes: [4000] });
    expect(resume(toursDus(tontine(), plusCourt))).toEqual([[1, COTISATION], [2, COTISATION]]);
    const plusLong = membre({ statutTour: 'paye', resteTour: 0, toursImpayes: [2], restesImpayes: [4000, 7000] });
    expect(resume(toursDus(tontine(), plusLong))).toEqual([[2, COTISATION]]);
  });
});

describe('rolesTontine', () => {
  it('sans bureau chargé : aucun droit', () => {
    expect(rolesTontine(undefined)).toEqual({ responsable: false, tresorier: false });
  });
  it('responsable seul, trésorier seul, cumul, admin', () => {
    const g = (mesRoles: string[]) => ({ mesRoles }) as unknown as Parameters<typeof rolesTontine>[0];
    expect(rolesTontine(g(['responsable']))).toEqual({ responsable: true, tresorier: false });
    expect(rolesTontine(g(['tresorier']))).toEqual({ responsable: false, tresorier: true });
    expect(rolesTontine(g(['responsable', 'tresorier']))).toEqual({ responsable: true, tresorier: true });
    expect(rolesTontine(g(['admin']))).toEqual({ responsable: true, tresorier: true });
    expect(rolesTontine(g(['membre']))).toEqual({ responsable: false, tresorier: false });
  });
});

describe('monMembre', () => {
  it('trouve le membre lié au compte, jamais sans compte', () => {
    const t = { ...tontine(), membres: [membre({ id: 'a', utilisateurId: null }), membre({ id: 'b', utilisateurId: 'u2' })] };
    expect(monMembre(t, 'u2')?.id).toBe('b');
    expect(monMembre(t, undefined)).toBeUndefined();
    expect(monMembre(t, 'u9')).toBeUndefined();
    expect(monMembre(undefined, 'u2')).toBeUndefined();
  });
});

describe('STATUT_VERSEMENT', () => {
  it('libellés des versements', () => {
    expect(STATUT_VERSEMENT.confirme.label).toBe('Validé');
    expect(STATUT_VERSEMENT.en_attente.label).toBe('À valider');
    expect(STATUT_VERSEMENT.rejete.label).toBe('Rejeté');
  });
});

describe('formulaire de déclaration : tour et montant proposés', () => {
  const dus = [{ id: 'r3' }, { id: 'r1' }];

  it('garde le tour choisi tant qu’il est dû', () => {
    expect(tourParDefaut(dus, 'r1')).toBe('r1');
  });
  it('se replie sur le premier tour dû quand le tour choisi ne l’est plus, ou sans choix', () => {
    expect(tourParDefaut(dus, 'r2')).toBe('r3');
    expect(tourParDefaut(dus, null)).toBe('r3');
    expect(tourParDefaut([], 'r1')).toBeNull();
  });

  it('propose le reste du tour, sinon la cotisation, sinon rien', () => {
    expect(montantPropose(null, 4000, COTISATION)).toBe('4000');
    expect(montantPropose(null, undefined, COTISATION)).toBe(String(COTISATION));
    expect(montantPropose(null, undefined, undefined)).toBe('');
  });

  it('garde le montant saisi après une actualisation (même membre, même tour, reste recalculé)', () => {
    const saisie: Saisie = { cle: cleSaisieVersement('m1', 'r3'), valeur: '2500' };
    expect(montantPropose(saisieCourante(saisie, cleSaisieVersement('m1', 'r3')), 7000, COTISATION)).toBe('2500');
  });
  it('repropose le reste quand le tour change', () => {
    const saisie: Saisie = { cle: cleSaisieVersement('m1', 'r3'), valeur: '2500' };
    expect(montantPropose(saisieCourante(saisie, cleSaisieVersement('m1', 'r1')), 10000, COTISATION)).toBe('10000');
  });
  it('repropose le reste quand le membre change, même tour', () => {
    const saisie: Saisie = { cle: cleSaisieVersement('m1', 'r3'), valeur: '2500' };
    expect(montantPropose(saisieCourante(saisie, cleSaisieVersement('m2', 'r3')), 6000, COTISATION)).toBe('6000');
  });
  it('un champ vidé reste vide (pas remplacé par la proposition)', () => {
    const saisie: Saisie = { cle: cleSaisieVersement('m1', 'r3'), valeur: '' };
    expect(montantPropose(saisieCourante(saisie, cleSaisieVersement('m1', 'r3')), 7000, COTISATION)).toBe('');
  });
});
