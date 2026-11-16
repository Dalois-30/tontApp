/** Expenses of the project, visible to all; the responsable proposes, the trésorier validates. */
import React, { useState } from 'react';
import { View, Text, ScrollView, TextInput, Alert, RefreshControl } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../../navigation/types';
import { s, Carte, Section, Bouton, Pastille, Vide, Ligne, dateCourte } from '../../../components/kit';
import { ErrorBanner } from '../../../components/ErrorBanner';
import { formatMontant } from '../../../utils/format';
import { colors, spacing } from '../../../config/theme';
import { projetService, messageErreur } from '../../../services/projetService';
import { useProjet, roles, PAIEMENT } from './commun';

type Props = NativeStackScreenProps<AppStackParamList, 'ProjetDepenses'>;

export function ProjetDepensesScreen({ route }: Props): React.JSX.Element {
  const { projetId } = route.params;
  const { data: p, error, refetch, isRefetching, remplacer } = useProjet(projetId);
  const [libelle, setLibelle] = useState('');
  const [montant, setMontant] = useState('');
  const [justificatif, setJustificatif] = useState('');
  const [busy, setBusy] = useState(false);

  if (error) return <View style={s.contenu}><ErrorBanner message={error.message} /></View>;
  if (!p) return <View style={s.contenu}><Text style={s.muted}>Chargement…</Text></View>;
  const { responsable } = roles(p);

  const proposer = async () => {
    const m = Number(montant.replace(/\s/g, ''));
    if (!libelle.trim() || !(m > 0)) { Alert.alert('Dépense incomplète', 'Indiquez un libellé et un montant.'); return; }
    setBusy(true);
    try {
      remplacer(await projetService.proposerDepense(projetId, { libelle: libelle.trim(), montant: m, justificatif: justificatif.trim() }));
      setLibelle(''); setMontant(''); setJustificatif('');
      Alert.alert('Dépense proposée', `Elle attend la validation du trésorier (${p.bureau.tresorierNom}).`);
    } catch (e) {
      Alert.alert('Proposition impossible', messageErreur(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <ScrollView style={s.ecran} contentContainerStyle={s.contenu} keyboardShouldPersistTaps="handled"
      refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} colors={[colors.primary]} />}>
      <Carte>
        <Ligne label="Collecté" valeur={formatMontant(p.totalCollecte)} />
        <Ligne label="Dépensé" valeur={formatMontant(p.totalDepense)} />
        <Ligne label="Solde disponible" valeur={formatMontant(p.solde)} fort />
      </Carte>

      <Section titre={`Dépenses (${p.depenses.length})`} />
      {p.depenses.length === 0 ? <Vide texte="Aucune dépense pour le moment." /> : null}
      {p.depenses.map((d) => (
        <Carte key={d.id}>
          <View style={s.ligne}>
            <Text style={[s.texte, s.fort, { flex: 1 }]}>{d.libelle}</Text>
            <Text style={[s.texte, s.fort]}>{formatMontant(d.montant)}</Text>
          </View>
          <View style={s.ligne}>
            <Text style={[s.muted, { flex: 1 }]}>{dateCourte(d.proposeeLe)} · {d.justificatif || 'sans justificatif'}</Text>
            <Pastille ton={PAIEMENT[d.statut].ton} texte={d.statut === 'valide' ? 'Validée' : d.statut === 'rejete' ? 'Rejetée' : 'À valider'} />
          </View>
          {d.statut === 'rejete' && d.motif ? <Text style={[s.muted, { color: '#B71C1C' }]}>Motif : {d.motif}</Text> : null}
        </Carte>
      ))}

      {responsable ? (
        <>
          <Section titre="Proposer une dépense" />
          <TextInput style={s.champ} placeholder="Libellé (ex. achat de médicaments)" value={libelle} onChangeText={setLibelle} />
          <TextInput style={[s.champ, { marginTop: spacing.sm }]} placeholder="Montant (FCFA)" keyboardType="number-pad" value={montant} onChangeText={setMontant} />
          <TextInput style={[s.champ, { marginTop: spacing.sm }]} placeholder="Justificatif (n° de facture, devis…)" value={justificatif} onChangeText={setJustificatif} />
          <Bouton titre="Proposer" onPress={() => void proposer()} chargement={busy} style={{ marginTop: spacing.md }} />
          <Text style={[s.muted, { marginTop: spacing.xs }]}>Une dépense ne peut pas dépasser le solde. Le trésorier la valide.</Text>
        </>
      ) : null}
    </ScrollView>
  );
}
