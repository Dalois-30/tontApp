/**
 * Collective project: progress to the goal, my commitment (tier, amount due, credit),
 * my instalments, tier change (raise applied, decrease pending approval), expenses.
 */
import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, RefreshControl, Alert, TouchableOpacity } from 'react-native';
import { useQuery } from '@tanstack/react-query';
import { useFocusEffect } from '@react-navigation/native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../navigation/types';
import { projetService, messageErreur, type Echeance } from '../../services/projetService';
import { ErrorBanner } from '../../components/ErrorBanner';
import { formatMontant } from '../../utils/format';
import { colors, spacing, fontSizes, borderRadius } from '../../config/theme';

type Props = NativeStackScreenProps<AppStackParamList, 'ProjetDetail'>;

const ECHEANCE: Record<Echeance['statut'], { label: string; color: string }> = {
  soldee: { label: 'Soldée', color: '#1B5E20' },
  partielle: { label: 'Partielle', color: '#B26A00' },
  en_retard: { label: 'En retard', color: '#B71C1C' },
  a_venir: { label: 'À venir', color: '#757575' },
};

const dateCourte = (iso: string) =>
  new Date(iso).toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: '2-digit' });

export function ProjetDetailScreen({ route }: Props): React.JSX.Element {
  const { projetId } = route.params;
  const [busy, setBusy] = useState(false);
  const { data: p, error, refetch, isRefetching } = useQuery({
    queryKey: ['projet', projetId],
    queryFn: () => projetService.details(projetId),
  });

  useFocusEffect(useCallback(() => { void refetch(); }, [refetch]));

  const moi = p?.participants.find((x) => x.id === p.monParticipantId);

  const changerPalier = useCallback((palier: number) => {
    if (!p || !moi || palier === moi.palierActuel) return;
    const baisse = palier < moi.palierActuel;
    Alert.alert(
      baisse ? 'Baisser mon palier' : 'Augmenter mon palier',
      baisse
        ? `Passer de ${formatMontant(moi.palierActuel)} à ${formatMontant(palier)} par échéance ? ` +
          'Une baisse d\'engagement doit être validée par le gestionnaire.'
        : `Passer de ${formatMontant(moi.palierActuel)} à ${formatMontant(palier)} par échéance, dès la prochaine échéance ?`,
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
                statut === 'applique' ? 'Palier augmenté' : 'Demande envoyée',
                statut === 'applique'
                  ? 'Votre nouveau palier s\'applique à partir de la prochaine échéance.'
                  : 'Votre demande de baisse attend la validation du gestionnaire. Votre palier actuel reste dû en attendant.',
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

  if (error) return <View style={styles.container}><View style={styles.content}><ErrorBanner message={error.message} /></View></View>;
  if (!p) return <View style={styles.container}><Text style={styles.muted}>Chargement…</Text></View>;

  return (
    <ScrollView
      style={styles.container}
      contentContainerStyle={styles.content}
      refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} colors={[colors.primary]} />}
    >
      <Text style={styles.title}>{p.nom}</Text>
      {p.description ? <Text style={styles.muted}>{p.description}</Text> : null}

      <View style={styles.card}>
        <Text style={styles.label}>Collecté sur l'objectif</Text>
        <Text style={styles.big}>{formatMontant(p.totalCollecte)}</Text>
        <Text style={styles.muted}>sur {formatMontant(p.objectif)} – {p.progression} %</Text>
        <View style={styles.bar}><View style={[styles.barFill, { width: `${Math.min(100, p.progression)}%` }]} /></View>
        <View style={styles.row}>
          <Text style={styles.muted}>Dépensé {formatMontant(p.totalDepense)}</Text>
          <Text style={styles.strong}>Solde {formatMontant(p.solde)}</Text>
        </View>
      </View>

      {moi ? (
        <>
          <Text style={styles.section}>Mon engagement</Text>
          <View style={styles.card}>
            <View style={styles.row}><Text style={styles.label}>Mon palier</Text><Text style={styles.strong}>{formatMontant(moi.palierActuel)} / échéance</Text></View>
            {moi.baisseDemandee !== null ? (
              <Text style={styles.pending}>Baisse à {formatMontant(moi.baisseDemandee)} en attente de validation</Text>
            ) : null}
            <View style={styles.row}><Text style={styles.label}>Déjà payé</Text><Text style={styles.value}>{formatMontant(moi.totalPaye)}</Text></View>
            <View style={styles.row}>
              <Text style={styles.label}>Reste dû aujourd'hui</Text>
              <Text style={[styles.value, moi.resteDuExigible > 0 && styles.due]}>{formatMontant(moi.resteDuExigible)}</Text>
            </View>
            <View style={styles.row}><Text style={styles.label}>Avance (échéances futures)</Text><Text style={styles.value}>{formatMontant(moi.avance)}</Text></View>
            <Text style={styles.hint}>
              Payer moins que le palier laisse un reste dû ; un trop-perçu est reporté sur les échéances suivantes.
              Remettez vos paiements au gestionnaire : ils apparaissent ici une fois validés.
            </Text>
          </View>

          {p.statut === 'Ouvert' || p.statut === 'Brouillon' ? (
            <>
              <Text style={styles.section}>Changer de palier</Text>
              <View style={styles.chips}>
                {p.paliers.map((pal) => (
                  <TouchableOpacity
                    key={pal}
                    disabled={busy}
                    onPress={() => changerPalier(pal)}
                    style={[styles.chip, pal === moi.palierActuel && styles.chipActive]}
                    accessibilityRole="button"
                    accessibilityLabel={`Palier ${formatMontant(pal)}`}
                  >
                    <Text style={[styles.chipText, pal === moi.palierActuel && styles.chipTextActive]}>{formatMontant(pal)}</Text>
                  </TouchableOpacity>
                ))}
              </View>
              <Text style={styles.hint}>Hausse : immédiate pour les prochaines échéances. Baisse : validée par le gestionnaire.</Text>
            </>
          ) : null}

          <Text style={styles.section}>Mes échéances</Text>
          <View style={styles.card}>
            <Text style={styles.hint}>Date – payé / dû en FCFA</Text>
            {(moi.echeances ?? []).map((e) => (
              <View key={e.numero} style={styles.echeance}>
                <View style={[styles.dot, { backgroundColor: ECHEANCE[e.statut].color }]} />
                <Text style={styles.echeanceDate}>{dateCourte(e.date)}</Text>
                <Text style={styles.echeanceMontant} numberOfLines={1}>{formatMontant(e.paye).replace(/\s*FCFA$/, '')} / {formatMontant(e.palier).replace(/\s*FCFA$/, '')}</Text>
                <Text style={[styles.echeanceStatut, { color: ECHEANCE[e.statut].color }]}>{ECHEANCE[e.statut].label}</Text>
              </View>
            ))}
          </View>
        </>
      ) : (
        <>
          <Text style={styles.section}>Participants</Text>
          <View style={styles.card}>
            {p.participants.map((x) => (
              <View key={x.id} style={styles.row}>
                <Text style={styles.value}>{x.nom} – {formatMontant(x.palierActuel)}</Text>
                <Text style={[styles.value, x.resteDuExigible > 0 && styles.due]}>{formatMontant(x.resteDuExigible)}</Text>
              </View>
            ))}
            <Text style={styles.hint}>Encaissements, validations et dépenses se gèrent depuis le tableau de bord web.</Text>
          </View>
        </>
      )}

      <Text style={styles.section}>Dépenses</Text>
      <View style={styles.card}>
        {p.depenses.length === 0 ? <Text style={styles.muted}>Aucune dépense pour le moment.</Text> : null}
        {p.depenses.map((d) => (
          <View key={d.id} style={styles.row}>
            <Text style={styles.value}>{d.libelle}</Text>
            <Text style={styles.value}>{formatMontant(d.montant)} – {d.statut === 'valide' ? 'validée' : d.statut === 'rejete' ? 'rejetée' : 'en attente'}</Text>
          </View>
        ))}
      </View>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg, paddingBottom: spacing.xl * 2 },
  title: { fontSize: fontSizes.xl, fontWeight: '700', color: colors.textPrimary, marginBottom: spacing.xs },
  section: { fontSize: fontSizes.lg, fontWeight: '600', color: colors.textPrimary, marginTop: spacing.lg, marginBottom: spacing.sm },
  card: { backgroundColor: colors.surface, borderRadius: borderRadius.md, borderWidth: 1, borderColor: colors.border, padding: spacing.md, marginTop: spacing.md, gap: spacing.sm },
  label: { fontSize: fontSizes.md, color: colors.textSecondary },
  big: { fontSize: 28, fontWeight: '700', color: colors.textPrimary },
  muted: { fontSize: fontSizes.sm, color: colors.textSecondary },
  strong: { fontSize: fontSizes.md, fontWeight: '700', color: colors.textPrimary },
  value: { fontSize: fontSizes.md, color: colors.textPrimary },
  due: { color: '#B71C1C', fontWeight: '700' },
  pending: { fontSize: fontSizes.sm, color: '#1F4E79', fontWeight: '600' },
  hint: { fontSize: fontSizes.sm, color: colors.textSecondary, marginTop: spacing.xs },
  row: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: spacing.sm },
  bar: { height: 10, borderRadius: 5, backgroundColor: colors.border, overflow: 'hidden', marginVertical: spacing.sm },
  barFill: { height: '100%', backgroundColor: colors.primary },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  chip: { borderWidth: 1, borderColor: colors.primary, borderRadius: 20, paddingHorizontal: spacing.md, paddingVertical: spacing.sm },
  chipActive: { backgroundColor: colors.primary },
  chipText: { color: colors.primary, fontWeight: '600', fontSize: fontSizes.md },
  chipTextActive: { color: '#FFFFFF' },
  echeance: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingVertical: 2 },
  dot: { width: 10, height: 10, borderRadius: 5 },
  echeanceDate: { width: 70, fontSize: fontSizes.sm, color: colors.textPrimary },
  echeanceMontant: { flex: 1, fontSize: fontSizes.sm, color: colors.textPrimary },
  echeanceStatut: { width: 72, textAlign: 'right', fontSize: fontSizes.sm, fontWeight: '600' },
});
