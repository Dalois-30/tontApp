/**
 * The group's history, newest first: payments, tiers, expenses, members, roles, votes, séance
 * decisions. Filter by category or text to find a decision taken long ago.
 */
import React, { useMemo, useState } from 'react';
import { View, Text, SectionList, TextInput, RefreshControl } from 'react-native';
import { useQuery } from '@tanstack/react-query';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../../navigation/types';
import { s, Puce, Vide, Pastille, type Ton } from '../../../components/kit';
import { ErrorBanner } from '../../../components/ErrorBanner';
import { colors, spacing } from '../../../config/theme';
import { gouvernanceService, type CategorieEvenement, type Evenement } from '../../../services/gouvernanceService';

type Props = NativeStackScreenProps<AppStackParamList, 'Chronologie'>;

const CATEGORIES: { cle: CategorieEvenement | ''; label: string; ton: Ton }[] = [
  { cle: '', label: 'Tout', ton: 'neutre' },
  { cle: 'seances', label: 'Séances', ton: 'info' },
  { cle: 'votes', label: 'Votes', ton: 'info' },
  { cle: 'gouvernance', label: 'Bureau', ton: 'info' },
  { cle: 'paiements', label: 'Paiements', ton: 'bon' },
  { cle: 'paliers', label: 'Paliers', ton: 'attente' },
  { cle: 'depenses', label: 'Dépenses', ton: 'mauvais' },
  { cle: 'membres', label: 'Membres', ton: 'neutre' },
  { cle: 'projet', label: 'Projet', ton: 'neutre' },
];
const TON = Object.fromEntries(CATEGORIES.map((c) => [c.cle, c])) as Record<string, (typeof CATEGORIES)[number]>;

export function ChronologieScreen({ route, navigation }: Props): React.JSX.Element {
  const { type, id, participantId, titre } = route.params;
  const [categorie, setCategorie] = useState<CategorieEvenement | ''>(titre === 'Décisions et gouvernance' ? 'gouvernance' : '');
  const [q, setQ] = useState('');
  const [recherche, setRecherche] = useState('');

  React.useLayoutEffect(() => { if (titre) navigation.setOptions({ title: titre }); }, [navigation, titre]);

  const { data, error, refetch, isRefetching, isLoading } = useQuery({
    queryKey: ['chronologie', type, id, participantId, categorie, recherche],
    queryFn: () => gouvernanceService.chronologie(type, id, {
      categorie: categorie || undefined, participantId, q: recherche || undefined,
    }),
  });

  // Grouped by month, newest first.
  const sections = useMemo(() => {
    const groupes = new Map<string, Evenement[]>();
    for (const e of data ?? []) {
      const cle = new Date(e.horodatage).toLocaleDateString('fr-FR', { month: 'long', year: 'numeric' });
      groupes.set(cle, [...(groupes.get(cle) ?? []), e]);
    }
    return [...groupes].map(([title, items]) => ({ title: title.charAt(0).toUpperCase() + title.slice(1), data: items }));
  }, [data]);

  return (
    <SectionList
      style={s.ecran}
      contentContainerStyle={s.contenu}
      sections={sections}
      keyExtractor={(e) => `${e.source}-${e.sequence}`}
      stickySectionHeadersEnabled={false}
      refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} colors={[colors.primary]} />}
      ListHeaderComponent={
        <View style={{ gap: spacing.sm, marginBottom: spacing.sm }}>
          <TextInput style={s.champ} placeholder="Rechercher (ex. médicaments, Grace, palier…)" value={q} onChangeText={setQ}
            returnKeyType="search" onSubmitEditing={() => setRecherche(q.trim())} onBlur={() => setRecherche(q.trim())} />
          <View style={s.puces}>
            {CATEGORIES.map((c) => <Puce key={c.cle || 'tout'} texte={c.label} actif={categorie === c.cle} onPress={() => setCategorie(c.cle)} />)}
          </View>
          {error ? <ErrorBanner message={(error as Error).message} /> : null}
          {data ? <Text style={s.muted}>{data.length} événement(s) · journal infalsifiable, chaque ligne est chaînée à la précédente.</Text> : null}
        </View>
      }
      ListEmptyComponent={isLoading ? <Vide texte="Chargement…" /> : <Vide texte="Rien ne correspond à cette recherche." />}
      renderSectionHeader={({ section }) => <Text style={[s.sectionTitre, { marginTop: spacing.md, marginBottom: spacing.xs }]}>{section.title}</Text>}
      renderItem={({ item: e }) => (
        <View style={[s.carte, { gap: 4 }]}>
          <View style={s.ligne}>
            <Text style={s.muted}>{new Date(e.horodatage).toLocaleString('fr-FR', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })}</Text>
            <Pastille ton={TON[e.categorie]?.ton ?? 'neutre'} texte={TON[e.categorie]?.label ?? e.categorie} />
          </View>
          <Text style={s.texte}>{e.resume}</Text>
          {e.acteurNom ? <Text style={s.muted}>par {e.acteurNom}</Text> : null}
        </View>
      )}
    />
  );
}
