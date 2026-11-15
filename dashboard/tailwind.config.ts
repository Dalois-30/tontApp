import type { Config } from "tailwindcss";

const token = (name: string) => `rgb(var(--${name}) / <alpha-value>)`;

const config: Config = {
  content: ["./components/**/*.{ts,tsx}", "./app/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: Object.fromEntries(
        ["bg", "surface", "surface-2", "line", "ink", "ink-2", "muted", "brand", "brand-hover", "on-brand",
         "brand-soft", "accent", "accent-soft", "good", "good-soft", "warn", "warn-soft", "warn-mark",
         "serious", "serious-soft", "bad", "bad-soft", "info", "info-soft", "chart-1", "chart-2", "grid"]
          .map((n) => [n, token(n)])
      ),
      fontFamily: { sans: ["var(--font-sans)", "system-ui", "sans-serif"] },
    },
  },
  plugins: [],
};
export default config;
