/**
 * One participant's file, visible to every member: commitment, every instalment, every payment.
 * The trésorier decides a pending tier change here; the responsable changes the member's standing.
 */
import React, { useState } from 'react';
import { View, Text, ScrollView, RefreshControl, Alert } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../../navigation/types';
import { s, Carte, Section, Ligne, Pastille, Bouton, Puce, Vide, dateCourte } from '../../../components/kit';
import { ErrorBanner } from '../../../components/ErrorBanner';
import { formatMontant } from '../../../utils/format';
import { colors, spacing } from '../../../config/theme';
import { projetService, messageErreur, type Participant, type ProjetDetails } from '../../../services/projetService';
import { useProjet } from './commun';
import { roles, ECHEANCE, PAIEMENT, STATUT_PARTICIPANT, moyen } from './statuts';

type Props = NativeStackScreenProps<AppStackParamList, 'ProjetMembre'>;

export function ProjetMembreScreen({ route, navigation }: Props): React.JSX.Element {
  const { projetId, participantId } = route.params;
  const { data: p, error, refetch, isRefetching, remplacer } = useProjet(projetId);
  const [toutes, setToutes] = useState(false);
  const [busy, setBusy] = useState(false);

  if (error) return <View style={s.contenu}><ErrorBanner message={error.message} /></View>;
  const x = p?.participants.find((y) => y.id === participantId);
  if (!p || !x) return <View style={s.contenu}><Text style={s.muted}>Chargement…</Text></View>;

  const { responsable, tresorier } = roles(p);
  const paiements = p.paiements.filter((y) => y.participantId === x.id);
  const echeances = x.echeances ?? [];
  const echues = echeances.filter((e) => e.statut !== 'a_venir');
  const visibles = toutes ? echeances : echues.slice(-12).reverse();

  const agir = async (f: () => Promise<ProjetDetails>, ok: string) => {
    setBusy(true);
    try { remplacer(await f()); Alert.alert('C\'est fait', ok); }
    catch (e) { Alert.alert('Action impossible', messageErreur(e)); }
    finally { setBusy(false); }
  };

  const changerStatut = (statut: Participant['statut']) => {
    if (statut === x.statut) return;
    Alert.alert('Changer le statut', `${x.nom} passe à « ${STATUT_PARTICIPANT[statut].label} ». Ce changement est inscrit dans l'historique.`, [
      { text: 'Annuler', style: 'cancel' },
      { text: 'Confirmer', onPress: () => void agir(() => projetService.changerStatut(projetId, x.id, statut), 'Statut modifié.') },
    ]);
  };

  return (
    <ScrollView
      style={s.ecran}
      contentContainerStyle={s.contenu}
      refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} colors={[colors.primary]} />}
    >
      <Text style={s.titre}>{x.nom}</Text>
      <View style={[s.puces, { marginTop: spacing.xs }]}>
        <Pastille ton={STATUT_PARTICIPANT[x.statut].ton} texte={STATUT_PARTICIPANT[x.statut].label} />
        {x.utilisateurId === p.bureau.responsableId ? <Pastille ton="info" texte="Responsable" /> : null}
        {x.utilisateurId === p.bureau.tresorierId ? <Pastille ton="info" texte="Trésorier" /> : null}
        {!x.utilisateurId ? <Pastille ton="neutre" texte="Sans compte" /> : null}
      </View>
      <Text style={[s.muted, { marginTop: spacing.xs }]}>Membre depuis le {dateCourte(x.dateAdhesion)}</Text>

      <Section titre="Engagement" />
      <Carte>
        <Ligne label="Palier" valeur={`${formatMontant(x.palierActuel)} / échéance`} fort />
        {x.palierDemande !== null ? <Ligne label="Demande en attente" valeur={formatMontant(x.palierDemande)} ton="info" /> : null}
        <Ligne label="Déjà payé" valeur={formatMontant(x.totalPaye)} />
        <Ligne label="Reste dû aujourd'hui" valeur={formatMontant(x.resteDuExigible)} ton={x.resteDuExigible > 0 ? 'mauvais' : undefined} />
        <Ligne label="Avance" valeur={formatMontant(x.avance)} />
        <Ligne label="Engagement total" valeur={formatMontant(x.engagementTotal)} />
        <Text style={s.muted}>
          {echues.filter((e) => e.statut === 'soldee').length} échéances soldées sur {echues.length} échues
        </Text>
      </Carte>

      {tresorier && x.palierDemande !== null ? (
        <Carte>
          <Text style={s.texte}>Demande de passage de {formatMontant(x.palierActuel)} à {formatMontant(x.palierDemande)}.</Text>
          <View style={[s.puces, { justifyContent: 'flex-end' }]}>
            <Bouton petit variante="secondaire" titre="Refuser" desactive={busy}
              onPress={() => void agir(() => projetService.deciderPalier(projetId, x.id, false), 'Demande refusée.')} />
            <Bouton petit titre="Approuver" chargement={busy}
              onPress={() => void agir(() => projetService.deciderPalier(projetId, x.id, true), 'Changement de palier approuvé.')} />
          </View>
        </Carte>
      ) : null}

      <Section titre={toutes ? `Toutes les échéances (${echeances.length})` : 'Dernières échéances (payé / dû)'}
        action={toutes ? 'Les 12 dernières' : 'Tout l\'échéancier'} onAction={() => setToutes((v) => !v)} />
      <Carte>
        {visibles.length === 0 ? <Vide texte="Aucune échéance échue pour le moment." /> : null}
        {visibles.map((e) => (
          <View key={e.numero} style={s.ligne}>
            <Text style={[s.texte, { width: 86 }]}>{new Date(e.date).toLocaleDateString('fr-FR', { month: 'short', year: 'numeric' })}</Text>
            <Text style={[s.muted, { flex: 1 }]} numberOfLines={1}>
              {formatMontant(e.paye).replace(/\s*FCFA$/, '')} / {formatMontant(e.palier).replace(/\s*FCFA$/, '')}
            </Text>
            <Pastille ton={ECHEANCE[e.statut].ton} texte={ECHEANCE[e.statut].label} />
          </View>
        ))}
      </Carte>

      <Section titre={`Paiements (${paiements.length})`} />
      <Carte>
        {paiements.length === 0 ? <Vide texte="Aucun paiement enregistré." /> : null}
        {paiements.map((y) => (
          <View key={y.id} style={{ gap: 2, paddingVertical: 4 }}>
            <View style={s.ligne}>
              <Text style={[s.texte, s.fort]}>{formatMontant(y.montant)}</Text>
              <Pastille ton={PAIEMENT[y.statut].ton} texte={PAIEMENT[y.statut].label} />
            </View>
            <Text style={s.muted}>{dateCourte(y.saisiLe)} · {moyen(y.preuve)}</Text>
            {y.statut === 'rejete' && y.motif ? <Text style={[s.muted, { color: '#B71C1C' }]}>Motif : {y.motif}</Text> : null}
          </View>
        ))}
      </Carte>

      <Bouton variante="secondaire" titre="Historique de ce membre"
        onPress={() => navigation.navigate('Chronologie', { type: 'projets', id: projetId, participantId: x.id, titre: x.nom })} />

      {responsable ? (
        <>
          <Section titre="Statut du membre" />
          <View style={s.puces}>
            {(Object.keys(STATUT_PARTICIPANT) as Participant['statut'][]).map((st) => (
              <Puce key={st} texte={STATUT_PARTICIPANT[st].label} actif={x.statut === st} onPress={() => changerStatut(st)} />
            ))}
          </View>
          <Text style={[s.muted, { marginTop: spacing.xs }]}>Seuls les membres actifs votent. L'historique et ce qui est dû sont conservés.</Text>
        </>
      ) : null}
    </ScrollView>
  );
}
