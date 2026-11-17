import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

// Unit tests of the pure logic and the API proxies (fetch mocked: no network): **/*.test.ts.
export default defineConfig({
  resolve: { alias: { "@": fileURLToPath(new URL("./", import.meta.url)) } },
  test: {
    include: ["lib/**/*.test.ts", "app/**/*.test.ts"],
    environment: "node",
  },
});
