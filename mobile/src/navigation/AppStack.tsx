/**
 * Main application navigation stack.
 * Shown when the user is authenticated.
 * Contains Home, TontineDetail, Paiement, Gestionnaire, and Profil screens.
 */

import React from 'react';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { HomeScreen } from '../screens/app/HomeScreen';
import { TontineDetailScreen } from '../screens/app/TontineDetailScreen';
import { PaiementScreen } from '../screens/app/PaiementScreen';
import { GestionnaireScreen } from '../screens/app/GestionnaireScreen';
import { CreateTontineScreen } from '../screens/app/CreateTontineScreen';
import { ProfilScreen } from '../screens/app/ProfilScreen';
import { RejoindreParCodeScreen } from '../screens/app/RejoindreParCodeScreen';
import { InvitationScreen } from '../screens/app/InvitationScreen';
import { HistoriqueVersementsScreen } from '../screens/app/HistoriqueVersementsScreen';
import { NotificationsScreen } from '../screens/app/NotificationsScreen';
import { ProjetDetailScreen } from '../screens/app/ProjetDetailScreen';
import { RejoindreProjetScreen } from '../screens/app/RejoindreProjetScreen';
import { ProjetMembresScreen } from '../screens/app/projet/ProjetMembresScreen';
import { ProjetMembreScreen } from '../screens/app/projet/ProjetMembreScreen';
import { ProjetPaiementsScreen } from '../screens/app/projet/ProjetPaiementsScreen';
import { DeclarerPaiementScreen } from '../screens/app/projet/DeclarerPaiementScreen';
import { ProjetValidationsScreen } from '../screens/app/projet/ProjetValidationsScreen';
import { ProjetDepensesScreen } from '../screens/app/projet/ProjetDepensesScreen';
import { GouvernanceScreen } from '../screens/app/groupe/GouvernanceScreen';
import { SeancesScreen } from '../screens/app/groupe/SeancesScreen';
import { SeanceFormScreen } from '../screens/app/groupe/SeanceFormScreen';
import { ChronologieScreen } from '../screens/app/groupe/ChronologieScreen';
import { DeclarerVersementScreen } from '../screens/app/tontine/DeclarerVersementScreen';
import { TontineVersementsScreen } from '../screens/app/tontine/TontineVersementsScreen';
import type { AppStackParamList } from './types';
import { colors } from '../config/theme';

const Stack = createNativeStackNavigator<AppStackParamList>();

export function AppStack(): React.JSX.Element {
  return (
    <Stack.Navigator
      initialRouteName="Home"
      screenOptions={{
        headerStyle: { backgroundColor: colors.primary },
        headerTintColor: colors.textOnPrimary,
        headerTitleStyle: { fontWeight: '600' },
        contentStyle: { backgroundColor: colors.background },
        animation: 'slide_from_right',
      }}
    >
      <Stack.Screen
        name="Home"
        component={HomeScreen}
        options={{ title: 'Mes Tontines' }}
      />
      <Stack.Screen
        name="ProjetDetail"
        component={ProjetDetailScreen}
        options={{ title: 'Projet collectif' }}
      />
      <Stack.Screen
        name="RejoindreProjet"
        component={RejoindreProjetScreen}
        options={{ title: 'Rejoindre un projet' }}
      />
      <Stack.Screen name="ProjetMembres" component={ProjetMembresScreen} options={{ title: 'Membres et cotisations' }} />
      <Stack.Screen name="ProjetMembre" component={ProjetMembreScreen} options={{ title: 'Fiche du membre' }} />
      <Stack.Screen name="ProjetPaiements" component={ProjetPaiementsScreen} options={{ title: 'Tous les paiements' }} />
      <Stack.Screen name="DeclarerPaiement" component={DeclarerPaiementScreen} options={{ title: 'Déclarer un paiement' }} />
      <Stack.Screen name="ProjetValidations" component={ProjetValidationsScreen} options={{ title: 'À valider' }} />
      <Stack.Screen name="ProjetDepenses" component={ProjetDepensesScreen} options={{ title: 'Dépenses' }} />
      <Stack.Screen name="Gouvernance" component={GouvernanceScreen} options={{ title: 'Bureau et votes' }} />
      <Stack.Screen name="Seances" component={SeancesScreen} options={{ title: 'Rapports de séance' }} />
      <Stack.Screen name="SeanceForm" component={SeanceFormScreen} options={{ title: 'Nouveau rapport' }} />
      <Stack.Screen name="Chronologie" component={ChronologieScreen} options={{ title: 'Historique' }} />
      <Stack.Screen
        name="TontineDetail"
        component={TontineDetailScreen}
        options={{ title: 'Détail Tontine' }}
      />
      <Stack.Screen name="DeclarerVersement" component={DeclarerVersementScreen} options={{ title: 'Déclarer ma cotisation' }} />
      <Stack.Screen name="TontineVersements" component={TontineVersementsScreen} options={{ title: 'Tous les versements' }} />
      <Stack.Screen
        name="Paiement"
        component={PaiementScreen}
        options={{ title: 'Paiement' }}
      />
      <Stack.Screen
        name="Gestionnaire"
        component={GestionnaireScreen}
        options={{ title: 'Gestion du tour' }}
      />
      <Stack.Screen
        name="CreateTontine"
        component={CreateTontineScreen}
        options={{ title: 'Nouvelle Tontine' }}
      />
      <Stack.Screen
        name="Profil"
        component={ProfilScreen}
        options={{ title: 'Mon Profil' }}
      />
      <Stack.Screen
        name="RejoindreParCode"
        component={RejoindreParCodeScreen}
        options={{ title: 'Rejoindre une tontine' }}
      />
      <Stack.Screen
        name="Invitation"
        component={InvitationScreen}
        options={{ title: 'Inviter des membres' }}
      />
      <Stack.Screen
        name="HistoriqueVersements"
        component={HistoriqueVersementsScreen}
        options={{ title: 'Mes Versements' }}
      />
      <Stack.Screen
        name="Notifications"
        component={NotificationsScreen}
        options={{ title: 'Notifications' }}
      />
    </Stack.Navigator>
  );
}
