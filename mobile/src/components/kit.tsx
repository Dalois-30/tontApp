/**
 * Small shared building blocks for the group screens (project, governance, history):
 * cards, section titles, status pills (always icon-free text + colour, never colour alone),
 * buttons and chips. Keeps the screens consistent with the app's green theme.
 */
import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity, ActivityIndicator, type ViewStyle, type StyleProp } from 'react-native';
import { colors, spacing, fontSizes, borderRadius } from '../config/theme';

export const tons = {
  bon: { fg: '#1B5E20', bg: '#E8F5E9' },
  attente: { fg: '#8A4B00', bg: '#FFF3E0' },
  mauvais: { fg: '#B71C1C', bg: '#FFEBEE' },
  info: { fg: '#1F4E79', bg: '#E3F2FD' },
  neutre: { fg: '#616161', bg: '#F1F1F1' },
} as const;
export type Ton = keyof typeof tons;

export function Carte({ children, style }: { children: React.ReactNode; style?: StyleProp<ViewStyle> }) {
  return <View style={[s.carte, style]}>{children}</View>;
}

export function Section({ titre, action, onAction }: { titre: string; action?: string; onAction?: () => void }) {
  return (
    <View style={s.section}>
      <Text style={s.sectionTitre} accessibilityRole="header">{titre}</Text>
      {action && onAction ? (
        <TouchableOpacity onPress={onAction} accessibilityRole="button" hitSlop={8}>
          <Text style={s.lien}>{action}</Text>
        </TouchableOpacity>
      ) : null}
    </View>
  );
}

export function Pastille({ ton, texte }: { ton: Ton; texte: string }) {
  return (
    <View style={[s.pastille, { backgroundColor: tons[ton].bg }]}>
      <Text style={[s.pastilleTexte, { color: tons[ton].fg }]} numberOfLines={1}>{texte}</Text>
    </View>
  );
}

export function Ligne({ label, valeur, fort, ton }: { label: string; valeur: string; fort?: boolean; ton?: Ton }) {
  return (
    <View style={s.ligne}>
      <Text style={s.label}>{label}</Text>
      <Text style={[s.valeur, fort && s.fort, ton && { color: tons[ton].fg, fontWeight: '700' }]}>{valeur}</Text>
    </View>
  );
}

export function Bouton({ titre, onPress, variante = 'primaire', petit, desactive, chargement, style }: {
  titre: string; onPress: () => void; variante?: 'primaire' | 'secondaire' | 'danger'; petit?: boolean;
  desactive?: boolean; chargement?: boolean; style?: StyleProp<ViewStyle>;
}) {
  const v = variante === 'primaire' ? s.btnPrimaire : variante === 'danger' ? s.btnDanger : s.btnSecondaire;
  const t = variante === 'secondaire' ? s.btnTexteSecondaire : s.btnTexte;
  return (
    <TouchableOpacity
      onPress={onPress}
      disabled={desactive || chargement}
      style={[s.btn, petit && s.btnPetit, v, (desactive || chargement) && s.btnOff, style]}
      accessibilityRole="button"
      accessibilityState={{ disabled: !!(desactive || chargement) }}
    >
      {chargement ? <ActivityIndicator color={variante === 'secondaire' ? colors.primary : '#fff'} /> : <Text style={[t, petit && s.btnTextePetit]}>{titre}</Text>}
    </TouchableOpacity>
  );
}

export function Puce({ texte, actif, onPress }: { texte: string; actif?: boolean; onPress: () => void }) {
  return (
    <TouchableOpacity onPress={onPress} style={[s.puce, actif && s.puceActive]} accessibilityRole="button" accessibilityState={{ selected: !!actif }}>
      <Text style={[s.puceTexte, actif && s.puceTexteActive]}>{texte}</Text>
    </TouchableOpacity>
  );
}

/** Navigation tile of a group hub: a title, a one-line explanation, an optional counter. */
export function Tuile({ titre, sousTitre, compteur, onPress }: { titre: string; sousTitre: string; compteur?: number; onPress: () => void }) {
  return (
    <TouchableOpacity onPress={onPress} style={s.tuile} accessibilityRole="button" accessibilityLabel={`${titre}. ${sousTitre}`}>
      <View style={{ flex: 1 }}>
        <Text style={s.tuileTitre}>{titre}</Text>
        <Text style={s.muted} numberOfLines={2}>{sousTitre}</Text>
      </View>
      {compteur ? <View style={s.compteur}><Text style={s.compteurTexte}>{compteur}</Text></View> : null}
      <Text style={s.chevron}>›</Text>
    </TouchableOpacity>
  );
}

export const Vide = ({ texte }: { texte: string }) => <Text style={[s.muted, { paddingVertical: spacing.sm }]}>{texte}</Text>;

export const dateCourte = (iso: string) =>
  new Date(iso).toLocaleDateString('fr-FR', { day: '2-digit', month: 'short', year: 'numeric' });
export const dateHeure = (iso: string) =>
  new Date(iso).toLocaleString('fr-FR', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });

export const s = StyleSheet.create({
  ecran: { flex: 1, backgroundColor: colors.background },
  contenu: { padding: spacing.md, paddingBottom: spacing.xxl },
  titre: { fontSize: fontSizes.xl, fontWeight: '700', color: colors.textPrimary },
  carte: { backgroundColor: colors.surface, borderRadius: borderRadius.md, borderWidth: 1, borderColor: colors.border, padding: spacing.md, gap: spacing.sm, marginBottom: spacing.sm },
  section: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginTop: spacing.lg, marginBottom: spacing.sm },
  sectionTitre: { fontSize: fontSizes.lg, fontWeight: '600', color: colors.textPrimary },
  lien: { color: colors.primary, fontWeight: '600', fontSize: fontSizes.sm },
  pastille: { borderRadius: 12, paddingHorizontal: 8, paddingVertical: 2, alignSelf: 'flex-start' },
  pastilleTexte: { fontSize: fontSizes.xs, fontWeight: '700' },
  ligne: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: spacing.sm },
  label: { fontSize: fontSizes.sm, color: colors.textSecondary, flexShrink: 1 },
  valeur: { fontSize: fontSizes.md, color: colors.textPrimary },
  fort: { fontWeight: '700' },
  muted: { fontSize: fontSizes.sm, color: colors.textSecondary },
  texte: { fontSize: fontSizes.md, color: colors.textPrimary },
  btn: { borderRadius: borderRadius.md, paddingVertical: 12, paddingHorizontal: spacing.md, alignItems: 'center', justifyContent: 'center' },
  btnPetit: { paddingVertical: 7, paddingHorizontal: 12 },
  btnPrimaire: { backgroundColor: colors.primary },
  btnDanger: { backgroundColor: '#B71C1C' },
  btnSecondaire: { backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.primary },
  btnOff: { opacity: 0.5 },
  btnTexte: { color: '#fff', fontWeight: '700', fontSize: fontSizes.md },
  btnTexteSecondaire: { color: colors.primary, fontWeight: '700', fontSize: fontSizes.md },
  btnTextePetit: { fontSize: fontSizes.sm },
  puce: { borderWidth: 1, borderColor: colors.border, borderRadius: 16, paddingHorizontal: 12, paddingVertical: 6, backgroundColor: colors.surface },
  puceActive: { backgroundColor: colors.primary, borderColor: colors.primary },
  puceTexte: { fontSize: fontSizes.sm, color: colors.textPrimary },
  puceTexteActive: { color: '#fff', fontWeight: '600' },
  puces: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  tuile: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, backgroundColor: colors.surface, borderRadius: borderRadius.md, borderWidth: 1, borderColor: colors.border, padding: spacing.md, marginBottom: spacing.sm },
  tuileTitre: { fontSize: fontSizes.md, fontWeight: '600', color: colors.textPrimary },
  compteur: { minWidth: 24, height: 24, borderRadius: 12, backgroundColor: '#B26A00', alignItems: 'center', justifyContent: 'center', paddingHorizontal: 6 },
  compteurTexte: { color: '#fff', fontWeight: '700', fontSize: fontSizes.xs },
  chevron: { fontSize: 24, color: colors.textSecondary },
  champ: { borderWidth: 1, borderColor: colors.border, borderRadius: borderRadius.md, backgroundColor: colors.surface, paddingHorizontal: spacing.md, paddingVertical: 10, fontSize: fontSizes.md, color: colors.textPrimary },
  champLabel: { fontSize: fontSizes.sm, color: colors.textSecondary, marginTop: spacing.md, marginBottom: spacing.xs },
  separateur: { height: StyleSheet.hairlineWidth, backgroundColor: colors.border },
});
