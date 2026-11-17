---
title: 'Mobile : montée d''Expo SDK 54 vers 58 (sécurité des outils de compilation)'
type: 'chore'
created: '2026-11-17'
status: 'done'
baseline_commit: '31b53f326a0f1e0cad8b5d6161046319df3142e4'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-tests-front-et-ci.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Après les correctifs compatibles, 13 alertes de sécurité restent côté mobile (1 haute : `image-size` via Metro ; 12 modérées : `glob`, `minimatch` via la CLI et la config Expo). Elles sont dans les outils de compilation d'Expo SDK 54 et ne se corrigent qu'avec un SDK plus récent.

**Approach:** Décision de l'utilisateur (2026-11-17) : monter maintenant en Expo SDK 58 (et la version de React Native qui l'accompagne), corriger toutes les ruptures, et prouver que l'application fonctionne comme avant : typage, lint, tests, paquet JS, APK reconstruit et vérifié sur le Redmi.

## Boundaries & Constraints

**Always:** `npm audit` mobile sans alerte haute ni critique, et sans les 13 alertes listées ; même comportement fonctionnel (écrans, navigation, appels API, plugin `withLocalNoProxy`, bundle JS embarqué) ; `make test-front` vert ; APK debug reconstruit (`expo prebuild` si nécessaire, `gradlew app:assembleDebug`), installé et l'application démarrée sur le Redmi `JRPJDYVK69GYY9KZ` avec connexion d'un compte de démo et ouverture du projet ; versions alignées sur celles recommandées par `expo install --fix`.

**Never:** ne modifier aucun réglage du téléphone (données, langue, taille de texte, mode développeur…) ; ne pas utiliser le Redmi si une autre session l'utilise (vérifier `adb` et demander s'il le faut) ; ne pas désinstaller d'autres applications ; pas de changement de fonctionnalité.

</frozen-after-approval>

## Code Map

- `mobile/package.json`, `mobile/package-lock.json` -- `expo` ~54, React Native 0.81, dépendances Expo (`expo-notifications`, `expo-constants`, `expo-asset`…), overrides postcss.
- `mobile/app.json`, `mobile/plugins/withLocalNoProxy.js` -- configuration et plugin de config Expo (proxy de l'opérateur) à garder fonctionnels.
- `mobile/android/` -- projet natif généré (prebuild) ; paquet `com.tontinesapp.mobile`.
- `mobile/eslint.config.js`, `mobile/vitest.config.*` -- lint et tests (ESLint 9, vitest 5).
- `scripts/test-front.sh`, `Makefile` (`test-front`, `mobile`) -- vérifications locales.
- Bundle : `EXPO_PUBLIC_API_URL=http://localhost:5243 npx expo export:embed --platform android --dev false --entry-file index.ts --bundle-output android/app/src/main/assets/index.android.bundle --assets-dest android/app/src/main/res`, puis `cd android && ANDROID_HOME=$HOME/Android/Sdk ./gradlew app:assembleDebug -PreactNativeArchitectures=arm64-v8a`, puis `adb -s JRPJDYVK69GYY9KZ install -r app/build/outputs/apk/debug/app-debug.apk` et `adb reverse tcp:5243 tcp:5243`.
- Comptes de démo : gestionnaire `+237670000002`, membres `+2376900000NN`, mot de passe `Demo2026!` ; API locale sur 5243 (`make api`).

## Tasks & Acceptance

**Execution:**
- [x] Monter `expo` en SDK 58 et aligner toutes les dépendances (`npx expo install --fix`), React Native compris ; adapter le code et la configuration aux ruptures.
- [x] Régénérer le projet natif si nécessaire ; reconstruire bundle et APK.
- [x] `make test-front` vert ; `npm audit` mobile sans les 13 alertes.
- [x] Vérification sur le Redmi : installation, démarrage, connexion, accueil, ouverture du projet collectif (captures d'écran).

**Acceptance Criteria:**
- Given l'APK reconstruit en SDK 58, when un membre de démo se connecte sur le Redmi, then l'accueil et le projet s'affichent comme avant, sans erreur.

## Verification

**Commands:**
- `make test-front` -- expected: vert
- `cd mobile && npm audit` -- expected: aucune alerte haute ou critique, aucune des 13 alertes listées

**Manual checks (if no CLI):**
- Captures du Redmi : écran de connexion, accueil, projet collectif.

## Implementation Notes

- Checkpoint 1 approuvé sans pause (consigne permanente) ; décision de l'utilisateur 2026-11-17 : monter en SDK 58.
- **minSdk** : `android.minSdkVersion: 26` dans `app.json` n'a jamais été appliqué (l'APK SDK 54 avait minSdk 24) et le SDK 58 le refuse. Décision (2026-11-17) : garder ce qui a toujours été livré, Android 7+ ; la valeur est rendue explicite par `expo-build-properties` (`android.minSdkVersion: 24`).
- **Plugins de config** : toute dépendance qui fournit un `app.plugin.js` figure dans `expo.plugins` (aujourd'hui `expo-secure-store`, `expo-notifications`, `expo-sharing`, `expo-status-bar`, `expo-splash-screen`, `expo-build-properties`), comme l'ajoutent `expo install` et l'exige `expo-doctor` ; plus le plugin local `./plugins/withLocalNoProxy`, qui échoue explicitement si le modèle `MainApplication.kt` d'un nouveau SDK ne correspond plus (test : `plugins/withLocalNoProxy.test.ts`).
- **Surcharge `@expo/fingerprint` → `minimatch ^11.0.9`** (`mobile/package.json`) : corrige GHSA-g3hv-75wg-m249 (minimatch `>=7.0.0 <11.0.1`, déni de service), tiré par `expo@58` via `@expo/fingerprint@0.21.x` qui épingle `minimatch ^10.2.6`. À retirer dès qu'Expo publie un `@expo/fingerprint` dépendant d'un minimatch corrigé (vérifier avec `npm audit` après l'avoir enlevée).
- **Projet natif** : `mobile/android/` est généré (ignoré par git). `expo prebuild --clean` est obligatoire après un changement de SDK Expo ou de plugin de config : `make mobile-apk` (contrôle mémoire, prebuild --clean, bundle embarqué, `gradlew app:assembleDebug`) ; la CI le fait dans le job `android-apk`.
- Consignes permanentes : vérifier la mémoire (`free -m`) avant toute installation, compilation Gradle ou test, un outil lourd à la fois (Gradle est gourmand : ≥ 4 Go libres) ; **aucun travail reporté** ; jamais toucher aux réglages du Redmi.

## Spec Change Log

## Review Triage Log

Passe 1 — 3 couches, 14 constats. Aucun report (consigne de l'utilisateur).

| # | Constat | Verdict | Route |
|---|---------|---------|-------|
| 1 | `minSdkVersion: 26` jamais appliqué (minSdk effectif 24), supprimé sans trace | medium | patch : `expo-build-properties` minSdk 24 explicite (décision : garder Android 7+) |
| 2 | Déclaration de versement : montant de A conservé pour B ; comparaison d'identité (`dusVus`) ; règles non testées | high | patch : fonctions pures + tests |
| 3 | Déclaration de paiement / rejoindre par code : logique non testée | medium | patch |
| 4 | Présentation : `useCallback` non garanti stable pour FlatList | medium | patch : `useState(() => fn)` |
| 5 | Compte à rebours : rendu impur, intervalle sans fin, pas de relance | medium | patch |
| 6 | `withLocalNoProxy` : dépendance non déclarée, remplacement silencieux | high | patch : `expo/config-plugins`, erreur explicite, test |
| 7 | Import double `expo-notifications` | low | patch |
| 8 | Liste des plugins (`expo-secure-store`) | false | déjà présent ; règle notée |
| 9 | Override `minimatch` sans trace | low | patch : noté (GHSA-g3hv-75wg-m249) |
| 10 | Prebuild propre requis non dit ; pas de commande | medium | patch : `make mobile-apk` |
| 11 | Configuration Expo, bundle et APK non vérifiés en CI | medium | patch : `expo config` + `export:embed` dans `front`, job `android-apk` |

- Vérification orchestrateur : `make test-front` vert (web 89, mobile 61), `make mobile-apk` (prebuild --clean + Gradle) réussi, APK réinstallée et vérifiée sur le Redmi (montant proposé, saisie conservée, champ vidé conservé vide).

