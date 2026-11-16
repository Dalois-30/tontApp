/** Any member writes a séance report: date, attendance, agenda, minutes and the decisions taken. */
import React, { useState } from 'react';
import { View, Text, TextInput, ScrollView, Alert, TouchableOpacity } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { AppStackParamList } from '../../../navigation/types';
import { s, Bouton, Puce } from '../../../components/kit';
import { ErrorBanner } from '../../../components/ErrorBanner';
import { spacing } from '../../../config/theme';
import { gouvernanceService } from '../../../services/gouvernanceService';
import { messageErreur } from '../../../services/projetService';
import { useGouvernance } from './GouvernanceScreen';

type Props = NativeStackScreenProps<AppStackParamList, 'SeanceForm'>;

const aujourdhui = () => new Date().toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' });

/** "14/11/2026" -> "2026-11-14"; null when invalid. */
function isoDepuis(jjmmaaaa: string): string | null {
  const m = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(jjmmaaaa.trim());
  if (!m) return null;
  const [j, mo, a] = [Number(m[1]), Number(m[2]), Number(m[3])];
  const d = new Date(Date.UTC(a, mo - 1, j));
  return d.getUTCDate() === j && d.getUTCMonth() === mo - 1 ? d.toISOString().slice(0, 10) : null;
}

export function SeanceFormScreen({ route, navigation }: Props): React.JSX.Element {
  const { type, id } = route.params;
  const { data: g, error, remplacer } = useGouvernance(type, id);
  const [date, setDate] = useState(aujourdhui());
  const [titre, setTitre] = useState('');
  const [lieu, setLieu] = useState('');
  const [presents, setPresents] = useState<string[]>([]);
  const [ordre, setOrdre] = useState('');
  const [compteRendu, setCompteRendu] = useState('');
  const [decisions, setDecisions] = useState<string[]>(['']);
  const [busy, setBusy] = useState(false);

  if (error) return <View style={s.contenu}><ErrorBanner message={error.message} /></View>;
  if (!g) return <View style={s.contenu}><Text style={s.muted}>Chargement…</Text></View>;

  const basculer = (nom: string) => setPresents((p) => p.includes(nom) ? p.filter((x) => x !== nom) : [...p, nom]);
  const tous = g.membres.map((m) => m.nom);

  const publier = async () => {
    const iso = isoDepuis(date);
    if (!iso) { Alert.alert('Date invalide', 'Écrivez la date au format JJ/MM/AAAA.'); return; }
    if (!titre.trim() || !compteRendu.trim()) { Alert.alert('Rapport incomplet', 'Le titre et le compte rendu sont obligatoires.'); return; }
    setBusy(true);
    try {
      remplacer(await gouvernanceService.redigerSeance(type, id, {
        date: iso, titre: titre.trim(), lieu: lieu.trim() || undefined, presents, ordreDuJour: ordre.trim() || undefined,
        compteRendu: compteRendu.trim(), decisions: decisions.map((d) => d.trim()).filter(Boolean),
      }));
      Alert.alert('Rapport publié', 'Tous les membres peuvent le lire. Il est inscrit dans l\'historique.', [{ text: 'OK', onPress: () => navigation.goBack() }]);
    } catch (e) {
      Alert.alert('Publication impossible', messageErreur(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <ScrollView style={s.ecran} contentContainerStyle={s.contenu} keyboardShouldPersistTaps="handled">
      <Text style={s.champLabel}>Date de la séance</Text>
      <TextInput style={s.champ} value={date} onChangeText={setDate} placeholder="JJ/MM/AAAA" keyboardType="numbers-and-punctuation" />
      <Text style={s.champLabel}>Titre</Text>
      <TextInput style={s.champ} value={titre} onChangeText={setTitre} placeholder="Ex. Assemblée générale de novembre" />
      <Text style={s.champLabel}>Lieu</Text>
      <TextInput style={s.champ} value={lieu} onChangeText={setLieu} placeholder="Facultatif" />

      <View style={[s.ligne, { marginTop: spacing.md, marginBottom: spacing.xs }]}>
        <Text style={s.label}>Présents ({presents.length})</Text>
        <TouchableOpacity onPress={() => setPresents(presents.length === tous.length ? [] : tous)} accessibilityRole="button">
          <Text style={s.lien}>{presents.length === tous.length ? 'Aucun' : 'Tout le monde'}</Text>
        </TouchableOpacity>
      </View>
      <View style={s.puces}>
        {tous.map((n) => <Puce key={n} texte={n} actif={presents.includes(n)} onPress={() => basculer(n)} />)}
      </View>

      <Text style={s.champLabel}>Ordre du jour</Text>
      <TextInput style={[s.champ, { minHeight: 70, textAlignVertical: 'top' }]} multiline value={ordre} onChangeText={setOrdre} placeholder="Facultatif" />
      <Text style={s.champLabel}>Compte rendu</Text>
      <TextInput style={[s.champ, { minHeight: 140, textAlignVertical: 'top' }]} multiline value={compteRendu} onChangeText={setCompteRendu}
        placeholder="Ce qui a été dit, présenté, discuté…" />

      <Text style={s.champLabel}>Décisions prises</Text>
      {decisions.map((d, i) => (
        <View key={i} style={[s.ligne, { marginBottom: spacing.sm }]}>
          <TextInput style={[s.champ, { flex: 1 }]} value={d} placeholder={`Décision ${i + 1}`}
            onChangeText={(t) => setDecisions((l) => l.map((x, j) => (j === i ? t : x)))} />
          {decisions.length > 1 ? (
            <TouchableOpacity onPress={() => setDecisions((l) => l.filter((_, j) => j !== i))} accessibilityRole="button" accessibilityLabel={`Retirer la décision ${i + 1}`}>
              <Text style={[s.lien, { color: '#B71C1C' }]}>Retirer</Text>
            </TouchableOpacity>
          ) : null}
        </View>
      ))}
      <TouchableOpacity onPress={() => setDecisions((l) => [...l, ''])} accessibilityRole="button">
        <Text style={s.lien}>+ Ajouter une décision</Text>
      </TouchableOpacity>

      <Bouton titre="Publier le rapport" onPress={() => void publier()} chargement={busy} style={{ marginTop: spacing.lg }} />
      <Text style={[s.muted, { marginTop: spacing.xs }]}>Un rapport publié ne peut plus être modifié : relisez-le avant.</Text>
    </ScrollView>
  );
}
