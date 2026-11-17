import { describe, expect, it } from 'vitest';
import { nomPrerempli, saisieCourante } from './saisie';

describe('saisieCourante', () => {
  it('rend la saisie tant que son contexte est le contexte courant (une simple actualisation la garde)', () => {
    expect(saisieCourante({ cle: 'm1|r3', valeur: '5000' }, 'm1|r3')).toBe('5000');
  });
  it('écarte la saisie quand le contexte change (autre membre ou autre tour)', () => {
    expect(saisieCourante({ cle: 'm1|r3', valeur: '5000' }, 'm2|r3')).toBeNull();
    expect(saisieCourante({ cle: 'm1|r3', valeur: '5000' }, 'm1|r2')).toBeNull();
  });
  it('garde un champ vidé vide', () => {
    expect(saisieCourante({ cle: 'm1', valeur: '' }, 'm1')).toBe('');
  });
  it('rien de saisi : null', () => {
    expect(saisieCourante(null, 'm1')).toBeNull();
  });
});

describe('nomPrerempli', () => {
  it('propose le nom du profil tant que rien n’est saisi, y compris quand le profil arrive après', () => {
    expect(nomPrerempli(null, undefined)).toBe('');
    expect(nomPrerempli(null, 'Awa Ndiaye')).toBe('Awa Ndiaye');
  });
  it('garde ce que la personne a saisi, même vide, quand le profil change', () => {
    expect(nomPrerempli('Awa N.', 'Awa Ndiaye')).toBe('Awa N.');
    expect(nomPrerempli('', 'Awa Ndiaye')).toBe('');
  });
});
