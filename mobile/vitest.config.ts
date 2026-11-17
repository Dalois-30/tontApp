import { defineConfig } from 'vitest/config';

// Unit tests of the pure rules (no React Native, no network, no device): src/**/*.test.ts and the config plugins (plugins/**/*.test.ts).
export default defineConfig({
  test: {
    include: ['src/**/*.test.ts', 'plugins/**/*.test.ts'],
    environment: 'node',
  },
});
