/**
 * Bureau of the group (responsable, trésorier) and the members' votes. The responsable names the
 * trésorier; any member proposes a new responsable, which opens a vote of all active members.
 */
import React, { useCallback, useState } from 'react';
import { View, Text, ScrollView, Alert, RefreshControl } from 'react-native';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useFocusEffect } from '@react-navigation/native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../../navigation/types';
import { s, Carte, Section, Bouton, Pastille, Ligne, Vide, dateCourte, dateHeure, tons } from '../../../components/kit';
import { ErrorBanner } from '../../../components/ErrorBanner';
import { colors, spacing } from '../../../config/theme';
import { gouvernanceService, type Gouvernance, type Vote } from '../../../services/gouvernanceService';
import { messageErreur } from '../../../services/projetService';
import { ChoixPersonne } from '../projet/commun';

type Props = NativeStackScreenProps<AppStackParamList, 'Gouvernance'>;

const MODE: Record<Vote['mode'], string> = {
  MajoriteDesVotants: 'majorité des votants',
  MajoriteDesMembres: 'majorité de tous les membres',
  DeuxTiersDesMembres: 'deux tiers de tous les membres',
};

export function useGouvernance(type: 'projets' | 'tontines', id: string) {
  const client = useQueryClient();
  const q = useQuery({ queryKey: ['gouvernance', type, id], queryFn: () => gouvernanceService.consulter(type, id) });
  const { refetch } = q;
  useFocusEffect(useCallback(() => { void refetch(); }, [refetch]));
  const remplacer = useCallback((g: Gouvernance) => client.setQueryData(['gouvernance', type, id], g), [client, type, id]);
  return { ...q, remplacer };
}

function Barre({ valeur, total, couleur }: { valeur: number; total: number; couleur: string }) {
  return (
    <View style={{ height: 8, borderRadius: 4, backgroundColor: colors.border, overflow: 'hidden' }}>
      <View style={{ width: `${total ? Math.min(100, (valeur / total) * 100) : 0}%`, height: '100%', backgroundColor: couleur }} />
    </View>
  );
}

export function GouvernanceScreen({ route, navigation }: Props): React.JSX.Element {
  const { type, id } = route.params;
  const { data: g, error, refetch, isRefetching, remplacer } = useGouvernance(type, id);
  const [choix, setChoix] = useState<'tresorier' | 'candidat' | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  if (error) return <View style={s.contenu}><ErrorBanner message={error.message} /></View>;
  if (!g) return <View style={s.contenu}><Text style={s.muted}>Chargement…</Text></View>;

  const responsable = g.mesRoles.includes('responsable') || g.mesRoles.includes('admin');
  const ouverts = g.votes.filter((v) => v.statut === 'Ouvert');
  const clos = g.votes.filter((v) => v.statut !== 'Ouvert');
  const actifs = g.membres.filter((m) => m.actif);

  const agir = async (cle: string, f: () => Promise<Gouvernance>, ok?: string) => {
    setBusy(cle);
    try { remplacer(await f()); if (ok) Alert.alert('C\'est fait', ok); }
    catch (e) { Alert.alert('Action impossible', messageErreur(e)); }
    finally { setBusy(null); }
  };

  const choisir = (m: { id: string; nom: string }) => {
    const mode = choix;
    setChoix(null);
    if (mode === 'tresorier') {
      Alert.alert('Nommer le trésorier', `${m.nom} validera les paiements, les changements de palier et les dépenses.`, [
        { text: 'Annuler', style: 'cancel' },
        { text: 'Nommer', onPress: () => void agir('tresorier', () => gouvernanceService.nommerTresorier(type, id, m.id), `${m.nom} est le nouveau trésorier.`) },
      ]);
    } else {
      Alert.alert('Proposer un nouveau responsable',
        `Un vote s'ouvre pour ${g.regles.dureeVoteJours} jours : ${actifs.length} membres actifs votent. Adopté à la ${MODE[g.regles.modeVote]}` +
        (g.regles.modeVote === 'MajoriteDesVotants' ? `, si au moins ${g.regles.quorumPourcent} % des membres votent.` : '.'), [
        { text: 'Annuler', style: 'cancel' },
        { text: 'Ouvrir le vote', onPress: () => void agir('vote', () => gouvernanceService.proposerResponsable(type, id, m.id), 'Le vote est ouvert. Les membres peuvent voter dès maintenant.') },
      ]);
    }
  };

  const voter = (v: Vote, pour: boolean) =>
    void agir(v.id, () => gouvernanceService.voter(type, id, v.id, pour), pour ? 'Vous avez voté pour.' : 'Vous avez voté contre.');

  return (
    <ScrollView style={s.ecran} contentContainerStyle={s.contenu}
      refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} colors={[colors.primary]} />}>
      <Text style={s.titre}>{g.groupeNom}</Text>

      <Section titre="Bureau" />
      <Carte>
        <Ligne label="Responsable" valeur={g.bureau.responsableNom} fort />
        <Text style={s.muted}>Gère le groupe, les membres, les règles ; propose les dépenses. Change uniquement par un vote.</Text>
        <View style={s.separateur} />
        <Ligne label="Trésorier" valeur={g.bureau.tresorierNom} fort />
        <Text style={s.muted}>Valide les paiements déclarés, les changements de palier et les dépenses.</Text>
        {responsable ? (
          <Bouton petit variante="secondaire" titre="Nommer un autre trésorier" chargement={busy === 'tresorier'} onPress={() => setChoix('tresorier')} />
        ) : null}
      </Carte>

      <Section titre={`Votes en cours (${ouverts.length})`} />
      {ouverts.length === 0 ? <Vide texte="Aucun vote en cours." /> : null}
      {ouverts.map((v) => (
        <Carte key={v.id}>
          <Text style={[s.texte, s.fort]}>{v.candidatNom} comme responsable</Text>
          <Text style={s.muted}>Proposé par {v.proposeParNom} · clôture le {dateHeure(v.fermeLe)}</Text>
          <Text style={s.muted}>
            {v.pour + v.contre} votants sur {v.electeurs} · adopté à la {MODE[v.mode]}
            {v.mode === 'MajoriteDesVotants' ? ` (quorum ${v.quorumPourcent} %)` : ''}
          </Text>
          <View style={{ gap: 4 }}>
            <View style={s.ligne}><Text style={s.label}>Pour</Text><Text style={[s.texte, { color: tons.bon.fg, fontWeight: '700' }]}>{v.pour}</Text></View>
            <Barre valeur={v.pour} total={v.electeurs} couleur={tons.bon.fg} />
            <View style={s.ligne}><Text style={s.label}>Contre</Text><Text style={[s.texte, { color: tons.mauvais.fg, fontWeight: '700' }]}>{v.contre}</Text></View>
            <Barre valeur={v.contre} total={v.electeurs} couleur={tons.mauvais.fg} />
          </View>
          {v.monVote !== null ? <Pastille ton="info" texte={v.monVote ? 'Vous avez voté pour' : 'Vous avez voté contre'} /> : null}
          {v.peutVoter ? (
            <View style={[s.puces, { justifyContent: 'flex-end' }]}>
              <Bouton petit variante="danger" titre={v.monVote === false ? 'Contre ✓' : 'Contre'} desactive={busy === v.id} onPress={() => voter(v, false)} />
              <Bouton petit titre={v.monVote === true ? 'Pour ✓' : 'Pour'} chargement={busy === v.id} onPress={() => voter(v, true)} />
            </View>
          ) : <Text style={s.muted}>Seuls les membres actifs à l'ouverture du vote votent.</Text>}
          {v.peutVoter && v.monVote !== null ? <Text style={s.muted}>Vous pouvez changer votre vote tant qu'il est ouvert.</Text> : null}
        </Carte>
      ))}
      <Bouton variante="secondaire" titre="Proposer un nouveau responsable" chargement={busy === 'vote'} onPress={() => setChoix('candidat')} />

      <Section titre={`Votes terminés (${clos.length})`} />
      {clos.length === 0 ? <Vide texte="Aucun vote terminé." /> : null}
      {clos.map((v) => (
        <Carte key={v.id}>
          <View style={s.ligne}>
            <Text style={[s.texte, s.fort, { flex: 1 }]}>{v.candidatNom} comme responsable</Text>
            <Pastille ton={v.statut === 'Adopte' ? 'bon' : 'mauvais'} texte={v.statut === 'Adopte' ? 'Adopté' : 'Rejeté'} />
          </View>
          <Text style={s.muted}>{v.pour} pour · {v.contre} contre · {v.electeurs} électeurs · clos le {v.closLe ? dateCourte(v.closLe) : '—'}</Text>
        </Carte>
      ))}

      <Section titre="Règles de vote" />
      <Carte>
        <Text style={s.texte}>
          Un vote dure {g.regles.dureeVoteJours} jours. Il est adopté à la {MODE[g.regles.modeVote]}
          {g.regles.modeVote === 'MajoriteDesVotants' ? `, si au moins ${g.regles.quorumPourcent} % des membres actifs ont voté` : ''}.
          Il se clôt plus tôt dès que le résultat ne peut plus changer.
        </Text>
      </Carte>

      <View style={{ gap: spacing.sm, marginTop: spacing.md }}>
        <Bouton variante="secondaire" titre="Rapports de séance" onPress={() => navigation.navigate('Seances', { type, id })} />
        <Bouton variante="secondaire" titre="Historique des décisions"
          onPress={() => navigation.navigate('Chronologie', { type, id, titre: 'Décisions et gouvernance' })} />
      </View>

      <ChoixPersonne
        visible={choix !== null}
        titre={choix === 'tresorier' ? 'Nouveau trésorier' : 'Candidat au poste de responsable'}
        personnes={(choix === 'tresorier' ? g.membres : actifs)
          .filter((m) => choix === 'tresorier' ? m.utilisateurId !== g.bureau.tresorierId : m.utilisateurId !== g.bureau.responsableId)
          .map((m) => ({ id: m.utilisateurId, nom: m.nom, detail: m.roles.length ? m.roles.join(', ') : undefined }))}
        onChoisir={choisir}
        onFermer={() => setChoix(null)}
      />
    </ScrollView>
  );
}
