/** Every payment of the project, visible to all members, with who declared and who validated it. */
import React, { useMemo, useState } from 'react';
import { View, Text, FlatList, TextInput, RefreshControl } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../../navigation/types';
import { s, Pastille, Puce, Vide, dateCourte } from '../../../components/kit';
import { ErrorBanner } from '../../../components/ErrorBanner';
import { formatMontant } from '../../../utils/format';
import { colors, spacing } from '../../../config/theme';
import type { PaiementProjet } from '../../../services/projetService';
import { useProjet, PAIEMENT, moyen } from './commun';

type Props = NativeStackScreenProps<AppStackParamList, 'ProjetPaiements'>;
type Filtre = 'tous' | PaiementProjet['statut'];

export function ProjetPaiementsScreen({ route }: Props): React.JSX.Element {
  const { projetId } = route.params;
  const { data: p, error, refetch, isRefetching } = useProjet(projetId);
  const [q, setQ] = useState('');
  const [filtre, setFiltre] = useState<Filtre>('tous');
  const [limite, setLimite] = useState(40);

  const liste = useMemo(() => (p?.paiements ?? [])
    .filter((x) => filtre === 'tous' || x.statut === filtre)
    .filter((x) => x.participantNom.toLowerCase().includes(q.trim().toLowerCase())), [p, q, filtre]);

  if (error) return <View style={s.contenu}><ErrorBanner message={error.message} /></View>;
  if (!p) return <View style={s.contenu}><Text style={s.muted}>Chargement…</Text></View>;

  const n = (st: PaiementProjet['statut']) => p.paiements.filter((x) => x.statut === st).length;
  const total = liste.filter((x) => x.statut === 'valide').reduce((t, x) => t + x.montant, 0);

  return (
    <FlatList
      style={s.ecran}
      contentContainerStyle={s.contenu}
      data={liste.slice(0, limite)}
      keyExtractor={(x) => x.id}
      onEndReached={() => setLimite((l) => l + 40)}
      refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} colors={[colors.primary]} />}
      ListHeaderComponent={
        <View style={{ gap: spacing.sm, marginBottom: spacing.sm }}>
          <TextInput style={s.champ} placeholder="Filtrer par membre" value={q} onChangeText={setQ} />
          <View style={s.puces}>
            <Puce texte="Tous" actif={filtre === 'tous'} onPress={() => setFiltre('tous')} />
            <Puce texte={`À valider (${n('en_attente')})`} actif={filtre === 'en_attente'} onPress={() => setFiltre('en_attente')} />
            <Puce texte={`Validés (${n('valide')})`} actif={filtre === 'valide'} onPress={() => setFiltre('valide')} />
            <Puce texte={`Rejetés (${n('rejete')})`} actif={filtre === 'rejete'} onPress={() => setFiltre('rejete')} />
          </View>
          <Text style={s.muted}>{liste.length} paiement(s) · {formatMontant(total)} validés dans cette vue</Text>
        </View>
      }
      ListEmptyComponent={<Vide texte="Aucun paiement dans cette vue." />}
      renderItem={({ item: x }) => (
        <View style={[s.carte, { gap: 2 }]}>
          <View style={s.ligne}>
            <Text style={[s.texte, s.fort, { flex: 1 }]} numberOfLines={1}>{x.participantNom}</Text>
            <Text style={[s.texte, s.fort]}>{formatMontant(x.montant)}</Text>
          </View>
          <View style={s.ligne}>
            <Text style={[s.muted, { flex: 1 }]} numberOfLines={1}>{dateCourte(x.saisiLe)} · {moyen(x.preuve)}</Text>
            <Pastille ton={PAIEMENT[x.statut].ton} texte={PAIEMENT[x.statut].label} />
          </View>
          {x.decideParNom && x.statut !== 'en_attente'
            ? <Text style={s.muted}>{x.statut === 'valide' ? 'Validé' : 'Rejeté'} par {x.decideParNom}{x.motif && x.statut === 'rejete' ? ` : ${x.motif}` : ''}</Text>
            : x.saisiParNom ? <Text style={s.muted}>Déclaré par {x.saisiParNom}</Text> : null}
        </View>
      )}
    />
  );
}
