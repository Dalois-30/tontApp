/** Shared helpers of the collective-project screens: data hook, status labels, member picker. */
import React, { useCallback, useMemo, useState } from 'react';
import { Modal, View, Text, TextInput, FlatList, TouchableOpacity, StyleSheet } from 'react-native';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useFocusEffect } from '@react-navigation/native';
import { projetService, type ProjetDetails, type Echeance, type PaiementProjet, type Participant } from '../../../services/projetService';
import { s as k, tons, type Ton } from '../../../components/kit';
import { colors, spacing } from '../../../config/theme';

/** The project, refreshed when the screen regains focus; `remplacer` stores a mutation's result. */
export function useProjet(projetId: string) {
  const client = useQueryClient();
  const q = useQuery({ queryKey: ['projet', projetId], queryFn: () => projetService.details(projetId) });
  const { refetch } = q;
  useFocusEffect(useCallback(() => { void refetch(); }, [refetch]));
  const remplacer = useCallback((p: ProjetDetails) => client.setQueryData(['projet', projetId], p), [client, projetId]);
  return { ...q, remplacer };
}

export const roles = (p?: ProjetDetails) => ({
  responsable: !!p && (p.mesRoles.includes('responsable') || p.mesRoles.includes('admin')),
  tresorier: !!p && (p.mesRoles.includes('tresorier') || p.mesRoles.includes('admin')),
});

export const ECHEANCE: Record<Echeance['statut'], { label: string; ton: Ton }> = {
  soldee: { label: 'Soldée', ton: 'bon' },
  partielle: { label: 'Partielle', ton: 'attente' },
  en_retard: { label: 'En retard', ton: 'mauvais' },
  a_venir: { label: 'À venir', ton: 'neutre' },
};

export const PAIEMENT: Record<PaiementProjet['statut'], { label: string; ton: Ton }> = {
  valide: { label: 'Validé', ton: 'bon' },
  en_attente: { label: 'À valider', ton: 'attente' },
  rejete: { label: 'Rejeté', ton: 'mauvais' },
};

export const STATUT_PARTICIPANT: Record<Participant['statut'], { label: string; ton: Ton }> = {
  Actif: { label: 'Actif', ton: 'bon' },
  Suspendu: { label: 'Suspendu', ton: 'attente' },
  Demissionnaire: { label: 'Démissionnaire', ton: 'neutre' },
  Exclu: { label: 'Exclu', ton: 'mauvais' },
};

/** "Import du registre · OM" -> "OM" ; any other proof is shown as entered. */
export const moyen = (preuve: string) => preuve.replace(/^Import du registre · /, '');

/** Strip of the instalments already due: one cell per instalment, coloured by status (with a text summary next to it). */
export function BandeEcheances({ echeances }: { echeances: Echeance[] }) {
  const echues = echeances.filter((e) => e.statut !== 'a_venir');
  return (
    <View style={styles.bande} accessibilityLabel={`${echues.filter((e) => e.statut === 'soldee').length} échéances soldées sur ${echues.length}`}>
      {echues.map((e) => <View key={e.numero} style={[styles.case, { backgroundColor: tons[ECHEANCE[e.statut].ton].fg }]} />)}
    </View>
  );
}

/** Modal list of people to choose from (treasurer, candidate, participant who paid...). */
export function ChoixPersonne<T extends { id: string; nom: string; detail?: string }>({ visible, titre, personnes, onChoisir, onFermer }: {
  visible: boolean; titre: string; personnes: T[]; onChoisir: (p: T) => void; onFermer: () => void;
}) {
  const [q, setQ] = useState('');
  const liste = useMemo(() => personnes.filter((p) => p.nom.toLowerCase().includes(q.trim().toLowerCase())), [personnes, q]);
  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onFermer}>
      <View style={[k.ecran, { padding: spacing.md }]}>
        <View style={k.ligne}>
          <Text style={k.titre}>{titre}</Text>
          <TouchableOpacity onPress={onFermer} accessibilityRole="button"><Text style={k.lien}>Fermer</Text></TouchableOpacity>
        </View>
        <TextInput style={[k.champ, { marginVertical: spacing.md }]} placeholder="Rechercher un nom" value={q} onChangeText={setQ} />
        <FlatList
          data={liste}
          keyExtractor={(p) => p.id}
          ItemSeparatorComponent={() => <View style={k.separateur} />}
          renderItem={({ item }) => (
            <TouchableOpacity style={{ paddingVertical: 14 }} onPress={() => { setQ(''); onChoisir(item); }} accessibilityRole="button">
              <Text style={k.texte}>{item.nom}</Text>
              {item.detail ? <Text style={k.muted}>{item.detail}</Text> : null}
            </TouchableOpacity>
          )}
        />
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  bande: { flexDirection: 'row', flexWrap: 'wrap', gap: 2 },
  case: { width: 7, height: 12, borderRadius: 2, backgroundColor: colors.border },
});
