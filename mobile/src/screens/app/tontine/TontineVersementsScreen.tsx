/**
 * Every payment of the tontine, visible to all members. The trésorier validates the declared ones
 * after checking the money was received, or rejects them with a reason every member can read.
 */
import React, { useMemo, useState } from 'react';
import { View, Text, FlatList, TextInput, RefreshControl, Alert } from 'react-native';
import { useQueryClient } from '@tanstack/react-query';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../../navigation/types';
import { s, Pastille, Puce, Vide, Bouton, dateCourte } from '../../../components/kit';
import { ErrorBanner } from '../../../components/ErrorBanner';
import { formatMontant } from '../../../utils/format';
import { colors, spacing } from '../../../config/theme';
import { tontineService, type VersementTontine } from '../../../services/tontineService';
import { useTontine, useVersements, messageErreur } from './commun';
import { rolesTontine, STATUT_VERSEMENT } from './logique';

type Props = NativeStackScreenProps<AppStackParamList, 'TontineVersements'>;
type Filtre = 'tous' | VersementTontine['status'];

export function TontineVersementsScreen({ route, navigation }: Props): React.JSX.Element {
  const { tontineId } = route.params;
  const client = useQueryClient();
  const { gouvernance, refetch: refetchTontine } = useTontine(tontineId);
  const { data, error, refetch, isRefetching } = useVersements(tontineId);
  const { tresorier } = rolesTontine(gouvernance);
  const [q, setQ] = useState('');
  const [filtre, setFiltre] = useState<Filtre>(route.params.aValider ? 'en_attente' : 'tous');
  const [busy, setBusy] = useState<string | null>(null);
  const [rejet, setRejet] = useState<string | null>(null);
  const [motif, setMotif] = useState('');

  const liste = useMemo(() => (data ?? [])
    .filter((x) => filtre === 'tous' || x.status === filtre)
    .filter((x) => x.payeurNom.toLowerCase().includes(q.trim().toLowerCase())), [data, q, filtre]);

  if (error) return <View style={s.contenu}><ErrorBanner message={error.message} /></View>;
  if (!data) return <View style={s.contenu}><Text style={s.muted}>Chargement…</Text></View>;

  const n = (st: VersementTontine['status']) => data.filter((x) => x.status === st).length;
  const total = liste.filter((x) => x.status === 'confirme').reduce((t, x) => t + x.montant, 0);

  const agir = async (id: string, f: () => Promise<void>) => {
    setBusy(id);
    try {
      await f();
      setRejet(null);
      setMotif('');
      await Promise.all([refetch(), refetchTontine(), client.invalidateQueries({ queryKey: ['chronologie', 'tontines', tontineId] })]);
    } catch (e) {
      Alert.alert('Action impossible', messageErreur(e));
    } finally {
      setBusy(null);
    }
  };

  return (
    <FlatList
      style={s.ecran}
      contentContainerStyle={s.contenu}
      data={liste}
      keyExtractor={(x) => x.id}
      keyboardShouldPersistTaps="handled"
      refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} colors={[colors.primary]} />}
      ListHeaderComponent={
        <View style={{ gap: spacing.sm, marginBottom: spacing.sm }}>
          {!tresorier && gouvernance ? (
            <Text style={s.muted}>Seul le trésorier ({gouvernance.bureau.tresorierNom}) valide les paiements. Tout le monde les voit.</Text>
          ) : null}
          <TextInput style={s.champ} placeholder="Filtrer par membre" value={q} onChangeText={setQ} />
          <View style={s.puces}>
            <Puce texte="Tous" actif={filtre === 'tous'} onPress={() => setFiltre('tous')} />
            <Puce texte={`À valider (${n('en_attente')})`} actif={filtre === 'en_attente'} onPress={() => setFiltre('en_attente')} />
            <Puce texte={`Validés (${n('confirme')})`} actif={filtre === 'confirme'} onPress={() => setFiltre('confirme')} />
            <Puce texte={`Rejetés (${n('rejete')})`} actif={filtre === 'rejete'} onPress={() => setFiltre('rejete')} />
          </View>
          <Text style={s.muted}>{liste.length} paiement(s) · {formatMontant(total)} validés dans cette vue</Text>
        </View>
      }
      ListEmptyComponent={<Vide texte="Aucun paiement dans cette vue." />}
      ListFooterComponent={
        <Bouton variante="secondaire" titre="Qui a déclaré, validé ou rejeté ?" style={{ marginTop: spacing.md }}
          onPress={() => navigation.navigate('Chronologie', { type: 'tontines', id: tontineId, titre: 'Historique des paiements' })} />
      }
      renderItem={({ item: x }) => (
        <View style={[s.carte, { gap: 2 }]}>
          <View style={s.ligne}>
            <Text style={[s.texte, s.fort, { flex: 1 }]} numberOfLines={1}>{x.payeurNom}</Text>
            <Text style={[s.texte, s.fort]}>{formatMontant(x.montant)}</Text>
          </View>
          <View style={s.ligne}>
            <Text style={[s.muted, { flex: 1 }]} numberOfLines={1}>
              {x.tourNumero ? `Tour ${x.tourNumero} · ` : ''}{dateCourte(x.dateCreation)}{x.reference ? ` · ${x.reference}` : ''}
            </Text>
            <Pastille ton={STATUT_VERSEMENT[x.status].ton} texte={STATUT_VERSEMENT[x.status].label} />
          </View>
          <Text style={s.muted}>Déclaré : {x.preuve ?? 'sans précision'}</Text>
          {x.status === 'rejete' && x.motif ? <Text style={s.muted}>Motif du rejet : {x.motif}</Text> : null}
          {tresorier && x.status === 'en_attente' && rejet !== x.id ? (
            <View style={[s.puces, { justifyContent: 'flex-end', marginTop: spacing.xs }]}>
              <Bouton petit variante="secondaire" titre="Rejeter" onPress={() => { setRejet(x.id); setMotif(''); }} />
              <Bouton petit titre="Valider" chargement={busy === x.id}
                onPress={() => void agir(x.id, () => tontineService.validerVersement(x.id))} />
            </View>
          ) : null}
          {rejet === x.id ? (
            <View style={{ gap: spacing.sm, marginTop: spacing.xs }}>
              <TextInput style={s.champ} value={motif} onChangeText={setMotif} placeholder="Motif du rejet (obligatoire)" autoFocus />
              <View style={[s.puces, { justifyContent: 'flex-end' }]}>
                <Bouton petit variante="secondaire" titre="Annuler" onPress={() => { setRejet(null); setMotif(''); }} />
                <Bouton petit variante="danger" titre="Confirmer le rejet" desactive={!motif.trim()} chargement={busy === x.id}
                  onPress={() => void agir(x.id, () => tontineService.rejeterVersement(x.id, motif.trim()))} />
              </View>
            </View>
          ) : null}
        </View>
      )}
    />
  );
}
