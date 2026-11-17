// https://docs.expo.dev/guides/using-eslint/
const { defineConfig } = require('eslint/config');
const expoConfig = require('eslint-config-expo/flat');

module.exports = defineConfig([
  expoConfig,
  {
    // React Native renders JSX text as-is: an apostrophe in French copy is not an HTML entity issue.
    rules: { 'react/no-unescaped-entities': 'off' },
  },
  {
    ignores: ['dist/*', 'android/*', '.expo/*'],
  },
]);
