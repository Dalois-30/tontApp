/** Every participant's standing, visible to all members: tier, paid, amount due, instalment strip. */
import React, { useMemo, useState } from 'react';
import { View, Text, FlatList, TextInput, TouchableOpacity, RefreshControl } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../../navigation/types';
import { s, Pastille, Puce, Vide } from '../../../components/kit';
import { ErrorBanner } from '../../../components/ErrorBanner';
import { formatMontant } from '../../../utils/format';
import { colors, spacing } from '../../../config/theme';
import { useProjet, BandeEcheances } from './commun';
import { STATUT_PARTICIPANT } from './statuts';

type Props = NativeStackScreenProps<AppStackParamList, 'ProjetMembres'>;
type Filtre = 'tous' | 'retard' | 'ajour';

export function ProjetMembresScreen({ route, navigation }: Props): React.JSX.Element {
  const { projetId } = route.params;
  const { data: p, error, refetch, isRefetching } = useProjet(projetId);
  const [q, setQ] = useState('');
  const [filtre, setFiltre] = useState<Filtre>('tous');

  const liste = useMemo(() => {
    if (!p) return [];
    return p.participants
      .filter((x) => x.nom.toLowerCase().includes(q.trim().toLowerCase()))
      .filter((x) => filtre === 'tous' || (filtre === 'retard' ? x.resteDuExigible > 0 : x.resteDuExigible === 0))
      .sort((a, b) => b.resteDuExigible - a.resteDuExigible || a.nom.localeCompare(b.nom));
  }, [p, q, filtre]);

  if (error) return <View style={s.contenu}><ErrorBanner message={error.message} /></View>;
  if (!p) return <View style={s.contenu}><Text style={s.muted}>Chargement…</Text></View>;

  const enRetard = p.participants.filter((x) => x.resteDuExigible > 0).length;
  const totalDu = p.participants.reduce((t, x) => t + x.resteDuExigible, 0);

  return (
    <FlatList
      style={s.ecran}
      contentContainerStyle={s.contenu}
      data={liste}
      keyExtractor={(x) => x.id}
      refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} colors={[colors.primary]} />}
      ListHeaderComponent={
        <View style={{ gap: spacing.sm, marginBottom: spacing.sm }}>
          <Text style={s.muted}>
            {p.participants.length} participants · {enRetard} avec un reste dû · {formatMontant(totalDu)} à recouvrer
          </Text>
          <TextInput style={s.champ} placeholder="Rechercher un membre" value={q} onChangeText={setQ} />
          <View style={s.puces}>
            <Puce texte="Tous" actif={filtre === 'tous'} onPress={() => setFiltre('tous')} />
            <Puce texte={`Reste dû (${enRetard})`} actif={filtre === 'retard'} onPress={() => setFiltre('retard')} />
            <Puce texte={`À jour (${p.participants.length - enRetard})`} actif={filtre === 'ajour'} onPress={() => setFiltre('ajour')} />
          </View>
        </View>
      }
      ListEmptyComponent={<Vide texte="Aucun membre ne correspond." />}
      renderItem={({ item: x }) => (
        <TouchableOpacity
          style={[s.carte, { gap: 6 }]}
          onPress={() => navigation.navigate('ProjetMembre', { projetId, participantId: x.id })}
          accessibilityRole="button"
          accessibilityLabel={`${x.nom}, palier ${formatMontant(x.palierActuel)}, reste dû ${formatMontant(x.resteDuExigible)}`}
        >
          <View style={s.ligne}>
            <Text style={[s.texte, s.fort, { flex: 1 }]} numberOfLines={1}>
              {x.nom}{x.id === p.monParticipantId ? ' (moi)' : ''}
            </Text>
            {x.statut !== 'Actif' ? <Pastille ton={STATUT_PARTICIPANT[x.statut].ton} texte={STATUT_PARTICIPANT[x.statut].label} /> : null}
            {x.resteDuExigible > 0
              ? <Pastille ton="mauvais" texte={`Doit ${formatMontant(x.resteDuExigible)}`} />
              : <Pastille ton="bon" texte="À jour" />}
          </View>
          <Text style={s.muted}>
            Palier {formatMontant(x.palierActuel)} · payé {formatMontant(x.totalPaye)}
            {x.avance > 0 ? ` · avance ${formatMontant(x.avance)}` : ''}
            {x.palierDemande !== null ? ` · demande ${formatMontant(x.palierDemande)}` : ''}
          </Text>
          {x.echeances ? <BandeEcheances echeances={x.echeances} /> : null}
        </TouchableOpacity>
      )}
    />
  );
}
