/**
 * Join a collective project: enter the code, see the project and its tiers, pick a tier.
 */
import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TextInput, TouchableOpacity, Alert, KeyboardAvoidingView, Platform } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../navigation/types';
import { projetService, messageErreur, type ProjetApercu } from '../../services/projetService';
import { useAuthStore } from '../../store/authStore';
import { PrimaryButton } from '../../components/PrimaryButton';
import { ErrorBanner } from '../../components/ErrorBanner';
import { formatMontant } from '../../utils/format';
import { colors, spacing, fontSizes, borderRadius } from '../../config/theme';

type Props = NativeStackScreenProps<AppStackParamList, 'RejoindreProjet'>;

const PERIODE: Record<string, string> = { Monthly: 'mensuelles', Weekly: 'hebdomadaires', Biweekly: 'toutes les 2 semaines' };

export function RejoindreProjetScreen({ navigation }: Props): React.JSX.Element {
  const nomCompte = useAuthStore((s) => s.user?.nom ?? '');
  const [code, setCode] = useState('');
  const [apercu, setApercu] = useState<ProjetApercu | null>(null);
  const [palier, setPalier] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const voir = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      const a = await projetService.apercu(code.trim());
      setApercu(a);
      setPalier(null);
    } catch (e) {
      setApercu(null);
      setError(messageErreur(e) === 'Invalid or expired invitation code.' ? 'Code invalide ou expiré.' : messageErreur(e));
    } finally {
      setBusy(false);
    }
  }, [code]);

  const rejoindre = useCallback(async () => {
    if (!apercu || palier === null) return;
    setBusy(true);
    setError(null);
    try {
      const p = await projetService.rejoindre(code.trim(), nomCompte || 'Participant', palier);
      Alert.alert('Bienvenue dans le projet', `Vous vous engagez à ${formatMontant(palier)} par échéance.`, [
        { text: 'Voir le projet', onPress: () => navigation.replace('ProjetDetail', { projetId: p.id }) },
      ]);
    } catch (e) {
      setError(messageErreur(e));
    } finally {
      setBusy(false);
    }
  }, [apercu, palier, code, nomCompte, navigation]);

  return (
    <KeyboardAvoidingView style={styles.container} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        <Text style={styles.title}>Rejoindre un projet collectif</Text>
        <Text style={styles.muted}>Saisissez le code reçu du gestionnaire, puis choisissez votre palier d'engagement.</Text>

        {error ? <ErrorBanner message={error} /> : null}

        <Text style={styles.label}>Code d'invitation</Text>
        <TextInput
          style={styles.codeInput}
          value={code}
          onChangeText={(t) => { setCode(t.toUpperCase()); setApercu(null); setError(null); }}
          placeholder="Ex : ABC123"
          placeholderTextColor={colors.textSecondary}
          autoCapitalize="characters"
          autoCorrect={false}
          maxLength={12}
        />
        {!apercu ? (
          <PrimaryButton title="Voir le projet" onPress={voir} loading={busy} disabled={code.trim().length < 4} />
        ) : (
          <>
            <View style={styles.card}>
              <Text style={styles.projet}>{apercu.nom}</Text>
              {apercu.description ? <Text style={styles.muted}>{apercu.description}</Text> : null}
              <Text style={styles.value}>Objectif : {formatMontant(apercu.objectif)}</Text>
              <Text style={styles.value}>
                {apercu.nombreEcheances} échéances {PERIODE[apercu.periodicite] ?? ''} à partir du{' '}
                {new Date(apercu.dateDebut).toLocaleDateString('fr-FR')}
              </Text>
              <Text style={styles.muted}>{apercu.participants} participant(s) déjà engagé(s)</Text>
            </View>

            <Text style={styles.label}>Mon palier par échéance</Text>
            <View style={styles.chips}>
              {apercu.paliers.map((p) => (
                <TouchableOpacity key={p} onPress={() => setPalier(p)} style={[styles.chip, palier === p && styles.chipActive]}
                  accessibilityRole="button" accessibilityLabel={`Palier ${formatMontant(p)}`}>
                  <Text style={[styles.chipText, palier === p && styles.chipTextActive]}>{formatMontant(p)}</Text>
                </TouchableOpacity>
              ))}
            </View>
            {palier !== null ? (
              <Text style={styles.hint}>
                Le palier est un engagement de {formatMontant(palier)} par échéance
                {apercu.regles.rattrapageAdhesionTardive
                  ? ', y compris pour les échéances déjà passées (rattrapage).'
                  : ', à partir des échéances qui suivent votre arrivée.'}
                {apercu.regles.baisseAutorisee ? '' : ' Il ne pourra pas être baissé.'}
              </Text>
            ) : null}
            <PrimaryButton title="Rejoindre" onPress={rejoindre} loading={busy} disabled={palier === null} />
          </>
        )}
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.md },
  title: { fontSize: fontSizes.xl, fontWeight: '700', color: colors.textPrimary },
  muted: { fontSize: fontSizes.sm, color: colors.textSecondary },
  label: { fontSize: fontSizes.md, fontWeight: '600', color: colors.textPrimary, marginTop: spacing.sm },
  codeInput: { borderWidth: 1, borderColor: colors.border, borderRadius: borderRadius.md, backgroundColor: colors.surface,
    paddingHorizontal: spacing.md, paddingVertical: spacing.md, fontSize: fontSizes.xl, letterSpacing: 4, textAlign: 'center', color: colors.textPrimary },
  card: { backgroundColor: colors.surface, borderRadius: borderRadius.md, borderWidth: 1, borderColor: colors.border, padding: spacing.md, gap: spacing.xs },
  projet: { fontSize: fontSizes.lg, fontWeight: '700', color: colors.textPrimary },
  value: { fontSize: fontSizes.md, color: colors.textPrimary },
  hint: { fontSize: fontSizes.sm, color: colors.textSecondary },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  chip: { borderWidth: 1, borderColor: colors.primary, borderRadius: 20, paddingHorizontal: spacing.md, paddingVertical: spacing.sm },
  chipActive: { backgroundColor: colors.primary },
  chipText: { color: colors.primary, fontWeight: '600', fontSize: fontSizes.md },
  chipTextActive: { color: '#FFFFFF' },
});
