/**
 * Collective project hub: progress to the goal, the bureau, my commitment and the entry points to
 * everything the members share (all members' standing, payments, expenses, votes, séances, history).
 * Each member declares their own payments; the trésorier validates them.
 */
import React, { useCallback, useState } from 'react';
import { View, Text, ScrollView, RefreshControl, Alert, StyleSheet } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../navigation/types';
import { projetService, messageErreur } from '../../services/projetService';
import { ErrorBanner } from '../../components/ErrorBanner';
import { formatMontant } from '../../utils/format';
import { colors, spacing } from '../../config/theme';
import { s, Carte, Section, Ligne, Bouton, Puce, Tuile, Pastille } from '../../components/kit';
import { useProjet, roles, BandeEcheances } from './projet/commun';

type Props = NativeStackScreenProps<AppStackParamList, 'ProjetDetail'>;

export function ProjetDetailScreen({ route, navigation }: Props): React.JSX.Element {
  const { projetId } = route.params;
  const [busy, setBusy] = useState(false);
  const { data: p, error, refetch, isRefetching } = useProjet(projetId);
  const moi = p?.participants.find((x) => x.id === p.monParticipantId);

  const changerPalier = useCallback((palier: number) => {
    if (!p || !moi || palier === moi.palierActuel) return;
    const r = p.regles;
    const baisse = palier < moi.palierActuel;
    const validation = baisse ? r.baisseSoumiseAValidation : r.hausseSoumiseAValidation;
    const retro = baisse ? r.baisseRetroactive : r.hausseRetroactive;
    const portee = retro
      ? baisse
        ? 'Elle s\'applique aussi aux échéances passées : l\'argent déjà versé sera réparti au nouveau palier.'
        : 'Elle s\'applique aussi aux échéances passées : la différence devient due et sera réglée en premier.'
      : 'Elle s\'applique à partir de la prochaine échéance ; ce qui est déjà versé ne change pas.';
    Alert.alert(
      baisse ? 'Baisser mon palier' : 'Augmenter mon palier',
      `Passer de ${formatMontant(moi.palierActuel)} à ${formatMontant(palier)} par échéance ? ${portee}` +
        (validation ? ` Le trésorier (${p.bureau.tresorierNom}) doit valider ce changement.` : ''),
      [
        { text: 'Annuler', style: 'cancel' },
        {
          text: 'Confirmer',
          onPress: async () => {
            setBusy(true);
            try {
              const statut = await projetService.changerMonPalier(projetId, palier);
              await refetch();
              Alert.alert(
                statut === 'applique' ? 'Palier modifié' : 'Demande envoyée',
                statut === 'applique'
                  ? 'Votre nouveau palier est pris en compte.'
                  : 'Votre demande attend la validation du trésorier. Votre palier actuel reste dû en attendant.',
              );
            } catch (e) {
              Alert.alert('Changement impossible', messageErreur(e));
            } finally {
              setBusy(false);
            }
          },
        },
      ],
    );
  }, [p, moi, projetId, refetch]);

  if (error) return <View style={s.contenu}><ErrorBanner message={error.message} /></View>;
  if (!p) return <View style={s.contenu}><Text style={s.muted}>Chargement…</Text></View>;

  const { responsable, tresorier } = roles(p);
  const aValider = p.paiements.filter((x) => x.statut === 'en_attente').length
    + p.participants.filter((x) => x.palierDemande !== null).length
    + p.depenses.filter((x) => x.statut === 'en_attente').length;
  const enRetard = p.participants.filter((x) => x.resteDuExigible > 0).length;
  const mesEnAttente = moi ? p.paiements.filter((x) => x.participantId === moi.id && x.statut === 'en_attente') : [];
  const ouvert = p.statut === 'Ouvert';

  return (
    <ScrollView
      style={s.ecran}
      contentContainerStyle={s.contenu}
      refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} colors={[colors.primary]} />}
    >
      <Text style={s.titre}>{p.nom}</Text>
      {p.description ? <Text style={s.muted}>{p.description}</Text> : null}

      <Carte style={{ marginTop: spacing.md }}>
        <Text style={s.label}>Collecté sur l'objectif</Text>
        <Text style={styles.gros}>{formatMontant(p.totalCollecte)}</Text>
        <Text style={s.muted}>sur {formatMontant(p.objectif)} – {p.progression} %</Text>
        <View style={styles.barre}><View style={[styles.barreRemplie, { width: `${Math.min(100, p.progression)}%` }]} /></View>
        <Ligne label={`Dépensé ${formatMontant(p.totalDepense)}`} valeur={`Solde ${formatMontant(p.solde)}`} fort />
      </Carte>

      <Carte>
        <Ligne label="Responsable" valeur={p.bureau.responsableNom} />
        <Ligne label="Trésorier" valeur={p.bureau.tresorierNom} />
        {responsable || tresorier ? (
          <View style={s.puces}>
            {p.mesRoles.includes('responsable') ? <Pastille ton="info" texte="Vous êtes responsable" /> : null}
            {p.mesRoles.includes('tresorier') ? <Pastille ton="info" texte="Vous êtes trésorier" /> : null}
            {p.mesRoles.includes('admin') ? <Pastille ton="info" texte="Administrateur" /> : null}
          </View>
        ) : null}
      </Carte>

      {moi ? (
        <>
          <Section titre="Mon engagement" />
          <Carte>
            <Ligne label="Mon palier" valeur={`${formatMontant(moi.palierActuel)} / échéance`} fort />
            {moi.palierDemande !== null ? (
              <Text style={styles.attente}>Passage à {formatMontant(moi.palierDemande)} en attente de validation</Text>
            ) : null}
            <Ligne label="Déjà payé" valeur={formatMontant(moi.totalPaye)} />
            <Ligne label="Reste dû aujourd'hui" valeur={formatMontant(moi.resteDuExigible)} ton={moi.resteDuExigible > 0 ? 'mauvais' : undefined} />
            <Ligne label="Avance (échéances futures)" valeur={formatMontant(moi.avance)} />
            {moi.echeances ? <BandeEcheances echeances={moi.echeances} /> : null}
            {mesEnAttente.length > 0 ? (
              <Text style={styles.attente}>
                {mesEnAttente.length} paiement(s) déclaré(s) pour {formatMontant(mesEnAttente.reduce((t, x) => t + x.montant, 0))}, en attente du trésorier
              </Text>
            ) : null}
            <Bouton petit variante="secondaire" titre="Mes échéances et mes paiements"
              onPress={() => navigation.navigate('ProjetMembre', { projetId, participantId: moi.id })} />
          </Carte>
          {ouvert ? <Bouton titre="Déclarer un paiement" onPress={() => navigation.navigate('DeclarerPaiement', { projetId })} /> : null}

          {p.statut === 'Ouvert' || p.statut === 'Brouillon' ? (
            <>
              <Section titre="Changer de palier" />
              <View style={s.puces}>
                {p.paliers.filter((pal) => p.regles.baisseAutorisee || pal >= moi.palierActuel).map((pal) => (
                  <Puce key={pal} texte={formatMontant(pal)} actif={pal === moi.palierActuel}
                    onPress={() => { if (!busy) changerPalier(pal); }} />
                ))}
              </View>
              <Text style={[s.muted, { marginTop: spacing.xs }]}>
                Hausse : {p.regles.hausseSoumiseAValidation ? 'validée par le trésorier' : 'immédiate'}
                {p.regles.hausseRetroactive ? ', anciennes échéances complétées' : ', à partir de la prochaine échéance'}.
                {p.regles.baisseAutorisee
                  ? ` Baisse : ${p.regles.baisseSoumiseAValidation ? 'validée par le trésorier' : 'immédiate'}` +
                    (p.regles.baisseRetroactive ? ', appliquée aux échéances passées.' : ', sans toucher à ce qui est déjà versé.')
                  : ' Baisse non autorisée dans ce projet.'}
              </Text>
            </>
          ) : null}
        </>
      ) : (tresorier || responsable) && ouvert ? (
        <Bouton titre="Enregistrer un paiement pour un membre" style={{ marginTop: spacing.md }}
          onPress={() => navigation.navigate('DeclarerPaiement', { projetId })} />
      ) : null}

      <Section titre="Le groupe" />
      {tresorier || aValider > 0 ? (
        <Tuile titre="À valider" compteur={aValider}
          sousTitre={tresorier ? 'Paiements déclarés, changements de palier, dépenses' : `En attente du trésorier (${p.bureau.tresorierNom})`}
          onPress={() => navigation.navigate('ProjetValidations', { projetId })} />
      ) : null}
      <Tuile titre="Membres et cotisations" sousTitre={`${p.participants.length} participants · ${enRetard} avec un reste dû`}
        onPress={() => navigation.navigate('ProjetMembres', { projetId })} />
      <Tuile titre="Tous les paiements" sousTitre={`${p.paiements.length} paiements, qui a déclaré, qui a validé`}
        onPress={() => navigation.navigate('ProjetPaiements', { projetId })} />
      <Tuile titre="Dépenses" sousTitre={`${p.depenses.length} dépense(s) · solde ${formatMontant(p.solde)}`}
        onPress={() => navigation.navigate('ProjetDepenses', { projetId })} />
      <Tuile titre="Bureau et votes" sousTitre="Responsable, trésorier, votes en cours"
        onPress={() => navigation.navigate('Gouvernance', { type: 'projets', id: projetId })} />
      <Tuile titre="Rapports de séance" sousTitre="Comptes rendus et décisions des réunions"
        onPress={() => navigation.navigate('Seances', { type: 'projets', id: projetId })} />
      <Tuile titre="Historique" sousTitre="Tout ce qui s'est passé, avec recherche"
        onPress={() => navigation.navigate('Chronologie', { type: 'projets', id: projetId })} />
      {p.journalValide === false ? <Text style={[s.muted, { color: '#B71C1C' }]}>Attention : le journal du projet semble altéré.</Text> : null}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  gros: { fontSize: 28, fontWeight: '700', color: colors.textPrimary },
  barre: { height: 10, borderRadius: 5, backgroundColor: colors.border, overflow: 'hidden', marginVertical: spacing.xs },
  barreRemplie: { height: '100%', backgroundColor: colors.primary },
  attente: { fontSize: 14, color: '#1F4E79', fontWeight: '600' },
});
