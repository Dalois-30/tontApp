/**
 * The trésorier's inbox: declared payments, tier changes and expenses waiting for a decision.
 * A rejection always carries a reason, visible to every member.
 */
import React, { useState } from 'react';
import { View, Text, ScrollView, TextInput, Alert, RefreshControl } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../../navigation/types';
import { s, Carte, Section, Bouton, Vide, dateCourte } from '../../../components/kit';
import { ErrorBanner } from '../../../components/ErrorBanner';
import { formatMontant } from '../../../utils/format';
import { colors, spacing } from '../../../config/theme';
import { projetService, messageErreur, type ProjetDetails } from '../../../services/projetService';
import { useProjet, roles, moyen } from './commun';

type Props = NativeStackScreenProps<AppStackParamList, 'ProjetValidations'>;

export function ProjetValidationsScreen({ route }: Props): React.JSX.Element {
  const { projetId } = route.params;
  const { data: p, error, refetch, isRefetching, remplacer } = useProjet(projetId);
  const [busy, setBusy] = useState<string | null>(null);
  const [rejet, setRejet] = useState<{ id: string; type: 'paiement' | 'depense' } | null>(null);
  const [motif, setMotif] = useState('');

  if (error) return <View style={s.contenu}><ErrorBanner message={error.message} /></View>;
  if (!p) return <View style={s.contenu}><Text style={s.muted}>Chargement…</Text></View>;

  const { tresorier } = roles(p);
  const paiements = p.paiements.filter((x) => x.statut === 'en_attente');
  const paliers = p.participants.filter((x) => x.palierDemande !== null);
  const depenses = p.depenses.filter((x) => x.statut === 'en_attente');

  const agir = async (id: string, f: () => Promise<ProjetDetails>) => {
    setBusy(id);
    try { remplacer(await f()); setRejet(null); setMotif(''); }
    catch (e) { Alert.alert('Action impossible', messageErreur(e)); }
    finally { setBusy(null); }
  };

  const zoneRejet = (id: string, type: 'paiement' | 'depense') => rejet?.id === id ? (
    <View style={{ gap: spacing.sm }}>
      <TextInput style={s.champ} value={motif} onChangeText={setMotif} placeholder="Motif du rejet (obligatoire)" autoFocus />
      <View style={[s.puces, { justifyContent: 'flex-end' }]}>
        <Bouton petit variante="secondaire" titre="Annuler" onPress={() => { setRejet(null); setMotif(''); }} />
        <Bouton petit variante="danger" titre="Confirmer le rejet" desactive={!motif.trim()} chargement={busy === id}
          onPress={() => void agir(id, () => type === 'paiement'
            ? projetService.deciderPaiement(projetId, id, false, motif.trim())
            : projetService.deciderDepense(projetId, id, false, motif.trim()))} />
      </View>
    </View>
  ) : null;

  return (
    <ScrollView
      style={s.ecran}
      contentContainerStyle={s.contenu}
      keyboardShouldPersistTaps="handled"
      refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} colors={[colors.primary]} />}
    >
      {!tresorier ? (
        <Text style={[s.muted, { marginBottom: spacing.sm }]}>
          Seul le trésorier ({p.bureau.tresorierNom}) peut valider. Vous voyez ce qui l'attend.
        </Text>
      ) : null}

      <Section titre={`Paiements déclarés (${paiements.length})`} />
      {paiements.length === 0 ? <Vide texte="Aucun paiement à valider." /> : null}
      {paiements.map((x) => (
        <Carte key={x.id}>
          <View style={s.ligne}>
            <Text style={[s.texte, s.fort, { flex: 1 }]}>{x.participantNom}</Text>
            <Text style={[s.texte, s.fort]}>{formatMontant(x.montant)}</Text>
          </View>
          <Text style={s.muted}>{dateCourte(x.saisiLe)} · {moyen(x.preuve)}{x.saisiParNom ? ` · déclaré par ${x.saisiParNom}` : ''}</Text>
          {tresorier && rejet?.id !== x.id ? (
            <View style={[s.puces, { justifyContent: 'flex-end' }]}>
              <Bouton petit variante="secondaire" titre="Rejeter" onPress={() => { setRejet({ id: x.id, type: 'paiement' }); setMotif(''); }} />
              <Bouton petit titre="Valider" chargement={busy === x.id}
                onPress={() => void agir(x.id, () => projetService.deciderPaiement(projetId, x.id, true))} />
            </View>
          ) : null}
          {zoneRejet(x.id, 'paiement')}
        </Carte>
      ))}

      <Section titre={`Changements de palier (${paliers.length})`} />
      {paliers.length === 0 ? <Vide texte="Aucune demande en attente." /> : null}
      {paliers.map((x) => (
        <Carte key={x.id}>
          <Text style={[s.texte, s.fort]}>{x.nom}</Text>
          <Text style={s.muted}>De {formatMontant(x.palierActuel)} à {formatMontant(x.palierDemande ?? 0)} par échéance</Text>
          {tresorier ? (
            <View style={[s.puces, { justifyContent: 'flex-end' }]}>
              <Bouton petit variante="secondaire" titre="Refuser" desactive={busy === x.id}
                onPress={() => void agir(x.id, () => projetService.deciderPalier(projetId, x.id, false))} />
              <Bouton petit titre="Approuver" chargement={busy === x.id}
                onPress={() => void agir(x.id, () => projetService.deciderPalier(projetId, x.id, true))} />
            </View>
          ) : null}
        </Carte>
      ))}

      <Section titre={`Dépenses proposées (${depenses.length})`} />
      {depenses.length === 0 ? <Vide texte="Aucune dépense en attente." /> : null}
      {depenses.map((x) => (
        <Carte key={x.id}>
          <View style={s.ligne}>
            <Text style={[s.texte, s.fort, { flex: 1 }]}>{x.libelle}</Text>
            <Text style={[s.texte, s.fort]}>{formatMontant(x.montant)}</Text>
          </View>
          <Text style={s.muted}>{dateCourte(x.proposeeLe)} · {x.justificatif || 'sans justificatif'} · solde {formatMontant(p.solde)}</Text>
          {tresorier && rejet?.id !== x.id ? (
            <View style={[s.puces, { justifyContent: 'flex-end' }]}>
              <Bouton petit variante="secondaire" titre="Rejeter" onPress={() => { setRejet({ id: x.id, type: 'depense' }); setMotif(''); }} />
              <Bouton petit titre="Valider" chargement={busy === x.id}
                onPress={() => void agir(x.id, () => projetService.deciderDepense(projetId, x.id, true))} />
            </View>
          ) : null}
          {zoneRejet(x.id, 'depense')}
        </Carte>
      ))}
    </ScrollView>
  );
}
