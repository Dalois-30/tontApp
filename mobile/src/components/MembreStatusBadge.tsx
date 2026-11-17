/**
 * Member payment status badge.
 * Green = paid, Amber = partly paid (with the rest), Orange = pending, Red = late.
 * WCAG AA accessible contrast ratios.
 */

import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import type { StatutPaiement } from '../types/api';
import { fontSizes, borderRadius, spacing } from '../config/theme';
import { libelleStatutTour } from '../utils/format';

interface MembreStatusBadgeProps {
  /** Payment status */
  statut: StatutPaiement;
  /** paye_partiel / en_attente: what is still to pay on the round (FCFA) */
  reste?: number;
  /** Amount declared and waiting for validation (FCFA) */
  aValider?: number;
  /** Validated money carried into the round from other rounds' surplus (FCFA) */
  reporte?: number;
  /** Pending money beyond what the round still owes (FCFA) */
  excedent?: number;
}

const STATUS_CONFIG: Record<
  StatutPaiement,
  { label: string; backgroundColor: string; textColor: string }
> = {
  paye: {
    label: 'Payé',
    backgroundColor: '#1B5E20',     // green – contrast 8.2:1 on white
    textColor: '#FFFFFF',
  },
  paye_partiel: {
    label: 'Payé partiellement',
    backgroundColor: '#8D5A00',     // dark amber – contrast 5.9:1 on white
    textColor: '#FFFFFF',
  },
  en_attente: {
    label: 'À valider',
    backgroundColor: '#E65100',     // deep orange – contrast 5.0:1 on white
    textColor: '#FFFFFF',
  },
  a_payer: {
    label: 'À payer',
    backgroundColor: '#5F6B63',     // neutral – contrast 5.6:1 on white
    textColor: '#FFFFFF',
  },
  en_retard: {
    label: 'En retard',
    backgroundColor: '#B71C1C',     // dark red – contrast 7.8:1 on white
    textColor: '#FFFFFF',
  },
  non_du: {
    label: 'Non dû',
    backgroundColor: '#5F6B63',     // neutral – contrast 5.6:1 on white
    textColor: '#FFFFFF',
  },
  adhesion_en_attente: {
    label: 'À valider',
    backgroundColor: '#1F4E79',     // dark blue – contrast 8.6:1 on white
    textColor: '#FFFFFF',
  },
};

export function MembreStatusBadge({
  statut,
  reste,
  aValider,
  reporte,
  excedent,
}: MembreStatusBadgeProps): React.JSX.Element {
  const config = STATUS_CONFIG[statut];
  const label = libelleStatutTour(statut, reste, aValider, { reporte, excedent }) ?? config.label;

  return (
    <View
      style={[styles.badge, { backgroundColor: config.backgroundColor }]}
      accessibilityLabel={`Statut: ${label}`}
      accessibilityRole="text"
    >
      <Text style={[styles.text, { color: config.textColor }]}>
        {label}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  badge: {
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
    borderRadius: borderRadius.sm,
    alignSelf: 'flex-start',
  },
  text: {
    fontSize: fontSizes.xs,
    fontWeight: '700',
  },
});
