/**
 * A member declares the contribution they paid outside the app (cash, mobile money...): round, amount,
 * method, reference. It waits for the trésorier's validation. The responsable or the trésorier can
 * declare for a member without a smartphone.
 */
import React, { useEffect, useMemo, useState } from 'react';
import { View, Text, TextInput, ScrollView, Alert, TouchableOpacity } from 'react-native';
import { useQueryClient } from '@tanstack/react-query';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../../navigation/types';
import { s, Carte, Bouton, Puce, Ligne } from '../../../components/kit';
import { ErrorBanner } from '../../../components/ErrorBanner';
import { formatMontant, libelleStatutTour } from '../../../utils/format';
import { spacing } from '../../../config/theme';
import { useAuthStore } from '../../../store/authStore';
import { tontineService } from '../../../services/tontineService';
import { ChoixPersonne } from '../projet/commun';
import { useTontine, messageErreur } from './commun';
import { rolesTontine, monMembre, toursDus } from './logique';

type Props = NativeStackScreenProps<AppStackParamList, 'DeclarerVersement'>;


const MOYENS = ['Espèces', 'Orange Money', 'MTN MoMo', 'Virement'];

export function DeclarerVersementScreen({ route, navigation }: Props): React.JSX.Element {
  const { tontineId } = route.params;
  const userId = useAuthStore((st) => st.user?.id);
  const client = useQueryClient();
  const { tontine: t, gouvernance, error } = useTontine(tontineId);
  const [pour, setPour] = useState<string | null>(null);
  const [choix, setChoix] = useState(false);
  const [tourId, setTourId] = useState<string | null>(null);
  const [montant, setMontant] = useState('');
  const [moyen, setMoyen] = useState('Espèces');
  const [reference, setReference] = useState('');
  const [busy, setBusy] = useState(false);

  const { responsable, tresorier } = rolesTontine(gouvernance);
  const bureau = responsable || tresorier;
  const moi = monMembre(t, userId);
  const cible = t?.membres.find((m) => m.id === (pour ?? moi?.id));
  const dus = useMemo(() => toursDus(t, cible), [t, cible]);

  // Default: the first round owed (the open one, else the oldest unpaid), one contribution.
  // Keep the chosen round while it is still due; otherwise (new member, refetch) take the first one due.
  useEffect(() => {
    setTourId((actuel) => (actuel && dus.some((x) => x.id === actuel) ? actuel : dus[0]?.id ?? null));
  }, [dus]);
  // Proposed amount: what remains on the chosen round (the full contribution, or the rest of a partial payment).
  // Re-proposed when the round changes; a refetch only updates it while the user has not typed an amount.
  const [montantSaisi, setMontantSaisi] = useState(false);
  const resteChoisi = dus.find((x) => x.id === tourId)?.reste;
  useEffect(() => { setMontantSaisi(false); }, [tourId]);
  useEffect(() => {
    if (!montantSaisi) setMontant(resteChoisi !== undefined ? String(resteChoisi) : t ? String(t.montantCotisation) : '');
  }, [tourId, resteChoisi, montantSaisi, t?.montantCotisation]); // eslint-disable-line react-hooks/exhaustive-deps

  if (error) return <View style={s.contenu}><ErrorBanner message={error.message} /></View>;
  if (!t || !gouvernance) return <View style={s.contenu}><Text style={s.muted}>Chargement…</Text></View>;

  const valeur = Number(montant.replace(/\s/g, ''));
  const tour = t.tours.find((x) => x.id === tourId);
  const envoyer = async () => {
    if (!cible) { Alert.alert('Choisissez le membre', 'Indiquez qui a payé.'); return; }
    if (!tour) { Alert.alert('Aucun tour à payer', `${cible.nom} ne doit aucune cotisation pour le moment.`); return; }
    if (!(valeur > 0)) { Alert.alert('Montant invalide', 'Saisissez le montant versé.'); return; }
    setBusy(true);
    try {
      await tontineService.declarerVersement({
        tontineId, tourId: tour.id, montant: valeur, moyen, reference: reference.trim() || undefined,
        membreId: cible.id === moi?.id ? undefined : cible.id,
      });
      await Promise.all([
        client.invalidateQueries({ queryKey: ['tontine', tontineId] }),
        client.invalidateQueries({ queryKey: ['versements-tontine', tontineId] }),
      ]);
      Alert.alert('Paiement déclaré',
        `${formatMontant(valeur)} pour ${cible.nom} (tour ${tour.numero}). Il apparaîtra comme payé dès que le trésorier (${gouvernance.bureau.tresorierNom}) l'aura validé.`,
        [{ text: 'OK', onPress: () => navigation.goBack() }]);
    } catch (e) {
      Alert.alert('Déclaration impossible', messageErreur(e));
    } finally {
      setBusy(false);
    }
  };

  // Only members who still owe a round (not the beneficiary, not those fully paid or pending).
  const payeurs = t.membres.filter((m) => toursDus(t, m).length > 0);

  return (
    <ScrollView style={s.ecran} contentContainerStyle={s.contenu} keyboardShouldPersistTaps="handled">
      <Text style={s.muted}>
        Remettez la cotisation au trésorier ou payez par mobile money, puis déclarez-la ici. Le trésorier vérifie et valide.
      </Text>

      <Text style={s.champLabel}>Membre qui a payé</Text>
      {bureau ? (
        <TouchableOpacity style={s.champ} onPress={() => setChoix(true)} accessibilityRole="button">
          <Text style={cible ? s.texte : s.muted}>{cible ? cible.nom : 'Choisir le membre'}</Text>
        </TouchableOpacity>
      ) : (
        <View style={s.champ}><Text style={s.texte}>{cible?.nom ?? 'Vous n\'êtes pas membre de cette tontine'}</Text></View>
      )}

      {cible ? (
        <Carte style={{ marginTop: spacing.sm }}>
          <Ligne label="Cotisation" valeur={formatMontant(t.montantCotisation)} />
          {libelleStatutTour(cible.statutTour, cible.resteTour, cible.enAttenteTour, { reporte: cible.reporteTour, excedent: cible.enAttenteExcedentTour }) ? (
            <Ligne label="Tour en cours" valeur={libelleStatutTour(cible.statutTour, cible.resteTour, cible.enAttenteTour, { reporte: cible.reporteTour, excedent: cible.enAttenteExcedentTour }) ?? ''}
              ton={(cible.resteTour ?? 0) > 0 ? 'mauvais' : cible.statutTour === 'paye' ? undefined : 'attente'} />
          ) : null}
          {(cible.avance ?? 0) > 0 ? (
            <Ligne label="Avance (prochains tours)" valeur={formatMontant(cible.avance ?? 0)} />
          ) : null}
          <Ligne label="Reste dû (tours passés)" valeur={formatMontant(cible.resteDu ?? 0)} ton={(cible.resteDu ?? 0) > 0 ? 'mauvais' : undefined} />
        </Carte>
      ) : null}

      <Text style={s.champLabel}>Tour payé</Text>
      {dus.length === 0 ? (
        <Text style={s.muted}>{cible ? 'Aucune cotisation due : tout est payé ou en attente de validation.' : '—'}</Text>
      ) : (
        <View style={s.puces}>
          {dus.map((x) => (
            <Puce key={x.id}
              texte={`Tour ${x.numero}${x.estOuvert ? ' (en cours)' : ' (en retard)'}${x.reste < t.montantCotisation ? ` · reste ${formatMontant(x.reste)}` : ''}`}
              actif={tourId === x.id} onPress={() => setTourId(x.id)} />
          ))}
        </View>
      )}

      <Text style={s.champLabel}>Montant versé (FCFA)</Text>
      <TextInput style={s.champ} keyboardType="number-pad" value={montant} onChangeText={(x) => { setMontant(x); setMontantSaisi(true); }} accessibilityLabel="Montant versé en FCFA" />

      <Text style={s.champLabel}>Moyen de paiement</Text>
      <View style={s.puces}>
        {MOYENS.map((m) => <Puce key={m} texte={m} actif={moyen === m} onPress={() => setMoyen(m)} />)}
      </View>

      <Text style={s.champLabel}>Référence (n° de transaction, de reçu…)</Text>
      <TextInput style={s.champ} value={reference} onChangeText={setReference} placeholder="Facultatif" />

      <Bouton titre="Déclarer le paiement" onPress={() => void envoyer()} chargement={busy} desactive={!cible || !tour}
        style={{ marginTop: spacing.lg }} />

      <ChoixPersonne
        visible={choix}
        titre="Qui a payé ?"
        personnes={payeurs.map((m) => ({
          id: m.id, nom: m.nom,
          detail: (m.resteDu ?? 0) > 0 ? `doit ${formatMontant(m.resteDu ?? 0)} sur les tours passés` : undefined,
        }))}
        onChoisir={(x) => { setPour(x.id); setChoix(false); }}
        onFermer={() => setChoix(false)}
      />
    </ScrollView>
  );
}
