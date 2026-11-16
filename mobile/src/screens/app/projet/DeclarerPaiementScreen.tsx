/**
 * A member declares the payment they made (amount, method, receipt reference); it waits for the
 * trésorier's validation. The responsable or the trésorier can declare for a member without a phone.
 */
import React, { useEffect, useState } from 'react';
import { View, Text, TextInput, ScrollView, Alert, TouchableOpacity } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../../navigation/types';
import { s, Carte, Bouton, Puce, Ligne } from '../../../components/kit';
import { ErrorBanner } from '../../../components/ErrorBanner';
import { formatMontant } from '../../../utils/format';
import { spacing } from '../../../config/theme';
import { projetService, messageErreur } from '../../../services/projetService';
import { useProjet, roles, ChoixPersonne } from './commun';

type Props = NativeStackScreenProps<AppStackParamList, 'DeclarerPaiement'>;

const MOYENS = ['Espèces', 'Orange Money', 'MTN MoMo', 'Virement'];

export function DeclarerPaiementScreen({ route, navigation }: Props): React.JSX.Element {
  const { projetId } = route.params;
  const { data: p, error, remplacer } = useProjet(projetId);
  const [pour, setPour] = useState<string | null>(null);
  const [choix, setChoix] = useState(false);
  const [montant, setMontant] = useState('');
  const [moyenChoisi, setMoyen] = useState('Espèces');
  const [preuve, setPreuve] = useState('');
  const [busy, setBusy] = useState(false);

  const { responsable, tresorier } = roles(p);
  const bureau = responsable || tresorier;
  const cibleId = pour ?? p?.monParticipantId ?? null;
  const cible = p?.participants.find((x) => x.id === cibleId);

  // Suggest what is due today, or one instalment at the current tier.
  useEffect(() => {
    if (cible) setMontant(String(cible.resteDuExigible > 0 ? cible.resteDuExigible : cible.palierActuel));
  }, [cible?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  if (error) return <View style={s.contenu}><ErrorBanner message={error.message} /></View>;
  if (!p) return <View style={s.contenu}><Text style={s.muted}>Chargement…</Text></View>;

  const valeur = Number(montant.replace(/\s/g, ''));
  const envoyer = async () => {
    if (!cible) { Alert.alert('Choisissez le membre', 'Indiquez qui a payé.'); return; }
    if (!(valeur > 0)) { Alert.alert('Montant invalide', 'Saisissez le montant versé.'); return; }
    setBusy(true);
    try {
      remplacer(await projetService.declarerPaiement(projetId, {
        montant: valeur, moyen: moyenChoisi, preuve: preuve.trim() || undefined,
        participantId: cible.id === p.monParticipantId ? undefined : cible.id,
      }));
      Alert.alert('Paiement déclaré', `${formatMontant(valeur)} pour ${cible.nom}. Il apparaîtra comme payé dès que le trésorier (${p.bureau.tresorierNom}) l'aura validé.`,
        [{ text: 'OK', onPress: () => navigation.goBack() }]);
    } catch (e) {
      Alert.alert('Déclaration impossible', messageErreur(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <ScrollView style={s.ecran} contentContainerStyle={s.contenu} keyboardShouldPersistTaps="handled">
      <Text style={s.muted}>
        Remettez l'argent au trésorier ou payez par mobile money, puis déclarez-le ici. Le trésorier vérifie et valide.
      </Text>

      <Text style={s.champLabel}>Membre qui a payé</Text>
      {bureau ? (
        <TouchableOpacity style={s.champ} onPress={() => setChoix(true)} accessibilityRole="button">
          <Text style={cible ? s.texte : s.muted}>{cible ? cible.nom : 'Choisir le membre'}</Text>
        </TouchableOpacity>
      ) : (
        <View style={s.champ}><Text style={s.texte}>{cible?.nom ?? 'Vous ne participez pas à ce projet'}</Text></View>
      )}

      {cible ? (
        <Carte style={{ marginTop: spacing.sm }}>
          <Ligne label="Palier" valeur={formatMontant(cible.palierActuel)} />
          <Ligne label="Reste dû aujourd'hui" valeur={formatMontant(cible.resteDuExigible)} ton={cible.resteDuExigible > 0 ? 'mauvais' : undefined} />
        </Carte>
      ) : null}

      <Text style={s.champLabel}>Montant versé (FCFA)</Text>
      <TextInput style={s.champ} keyboardType="number-pad" value={montant} onChangeText={setMontant} accessibilityLabel="Montant versé en FCFA" />

      <Text style={s.champLabel}>Moyen de paiement</Text>
      <View style={s.puces}>
        {MOYENS.map((m) => <Puce key={m} texte={m} actif={moyenChoisi === m} onPress={() => setMoyen(m)} />)}
      </View>

      <Text style={s.champLabel}>Référence (n° de transaction, de reçu…)</Text>
      <TextInput style={s.champ} value={preuve} onChangeText={setPreuve} placeholder="Facultatif" />

      <Bouton titre="Déclarer le paiement" onPress={() => void envoyer()} chargement={busy} desactive={!cible} style={{ marginTop: spacing.lg }} />

      <ChoixPersonne
        visible={choix}
        titre="Qui a payé ?"
        personnes={p.participants.map((x) => ({ id: x.id, nom: x.nom, detail: `Palier ${formatMontant(x.palierActuel)} · doit ${formatMontant(x.resteDuExigible)}` }))}
        onChoisir={(x) => { setPour(x.id); setChoix(false); }}
        onFermer={() => setChoix(false)}
      />
    </ScrollView>
  );
}
