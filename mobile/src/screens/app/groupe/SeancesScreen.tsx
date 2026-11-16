/** Séance reports of the group, newest first, searchable; any member can write one. */
import React, { useMemo, useState } from 'react';
import { View, Text, FlatList, TextInput, TouchableOpacity, RefreshControl } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../../navigation/types';
import { s, Bouton, Vide, dateCourte } from '../../../components/kit';
import { ErrorBanner } from '../../../components/ErrorBanner';
import { colors, spacing } from '../../../config/theme';
import { useGouvernance } from './GouvernanceScreen';

type Props = NativeStackScreenProps<AppStackParamList, 'Seances'>;

const simple = (t: string) => t.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

export function SeancesScreen({ route, navigation }: Props): React.JSX.Element {
  const { type, id } = route.params;
  const { data: g, error, refetch, isRefetching } = useGouvernance(type, id);
  const [q, setQ] = useState('');
  const [ouverte, setOuverte] = useState<string | null>(null);

  const liste = useMemo(() => {
    const t = simple(q.trim());
    return (g?.seances ?? []).filter((x) => !t || simple([x.titre, x.lieu ?? '', x.ordreDuJour ?? '', x.compteRendu, ...x.decisions, ...x.presents].join(' ')).includes(t));
  }, [g, q]);

  if (error) return <View style={s.contenu}><ErrorBanner message={error.message} /></View>;
  if (!g) return <View style={s.contenu}><Text style={s.muted}>Chargement…</Text></View>;

  return (
    <FlatList
      style={s.ecran}
      contentContainerStyle={s.contenu}
      data={liste}
      keyExtractor={(x) => x.id}
      refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} colors={[colors.primary]} />}
      ListHeaderComponent={
        <View style={{ gap: spacing.sm, marginBottom: spacing.sm }}>
          <Bouton titre="Rédiger un rapport de séance" onPress={() => navigation.navigate('SeanceForm', { type, id })} />
          <TextInput style={s.champ} placeholder="Rechercher (décision, sujet, participant…)" value={q} onChangeText={setQ} />
          <Text style={s.muted}>{g.seances.length} rapport(s) · chaque rapport est inscrit dans l'historique et ne peut pas être modifié.</Text>
        </View>
      }
      ListEmptyComponent={<Vide texte={q ? 'Aucun rapport ne correspond.' : 'Aucun rapport de séance pour le moment.'} />}
      renderItem={({ item: x }) => {
        const deplie = ouverte === x.id;
        return (
          <TouchableOpacity style={s.carte} onPress={() => setOuverte(deplie ? null : x.id)} accessibilityRole="button"
            accessibilityState={{ expanded: deplie }}>
            <Text style={s.muted}>{dateCourte(x.date)}{x.lieu ? ` · ${x.lieu}` : ''}</Text>
            <Text style={[s.texte, s.fort]}>{x.titre}</Text>
            {x.decisions.length > 0 ? (
              <View style={{ gap: 2 }}>
                <Text style={s.label}>Décisions</Text>
                {x.decisions.map((d, i) => <Text key={i} style={s.texte}>• {d}</Text>)}
              </View>
            ) : null}
            {deplie ? (
              <View style={{ gap: spacing.xs }}>
                {x.ordreDuJour ? <><Text style={s.label}>Ordre du jour</Text><Text style={s.texte}>{x.ordreDuJour}</Text></> : null}
                <Text style={s.label}>Compte rendu</Text>
                <Text style={s.texte}>{x.compteRendu}</Text>
                <Text style={s.label}>Présents ({x.presents.length})</Text>
                <Text style={s.texte}>{x.presents.join(', ') || '—'}</Text>
              </View>
            ) : null}
            <Text style={s.muted}>Rédigé par {x.auteurNom} · {deplie ? 'toucher pour réduire' : 'toucher pour lire le compte rendu'}</Text>
          </TouchableOpacity>
        );
      }}
    />
  );
}
