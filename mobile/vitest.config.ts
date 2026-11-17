import { defineConfig } from 'vitest/config';

// Unit tests of the pure rules (no React Native, no network, no device): src/**/*.test.ts.
export default defineConfig({
  test: {
    include: ['src/**/*.test.ts'],
    environment: 'node',
  },
});
