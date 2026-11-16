"use client";

import { Suspense, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Logo } from "@/components/Sidebar";

function LoginForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const callbackUrl = searchParams.get("callbackUrl");
  const [telephone, setTelephone] = useState("+237");
  const [motDePasse, setMotDePasse] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    setLoading(true);
    try {
      const res = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ telephone: telephone.replace(/\s/g, ""), motDePasse }),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        setError(res.status === 401 || res.status === 400 ? "Numéro ou mot de passe incorrect." : data.error ?? "Connexion impossible.");
        return;
      }
      router.push(callbackUrl ?? (data.role === "Admin" ? "/admin" : "/gestionnaire"));
      router.refresh();
    } catch {
      setError("Le serveur ne répond pas. Vérifiez que l'API est démarrée.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <form className="space-y-5" onSubmit={handleSubmit}>
      {error && <div className="notice-err" role="alert">{error}</div>}
      <div>
        <label htmlFor="telephone" className="label">Numéro de téléphone</label>
        <input id="telephone" name="telephone" type="tel" autoComplete="tel" required value={telephone}
          onChange={(e) => setTelephone(e.target.value)} className="input num" />
      </div>
      <div>
        <label htmlFor="motDePasse" className="label">Mot de passe</label>
        <input id="motDePasse" name="motDePasse" type="password" autoComplete="current-password" required
          value={motDePasse} onChange={(e) => setMotDePasse(e.target.value)} className="input" />
      </div>
      <button type="submit" disabled={loading} className="btn-primary w-full h-10">
        {loading ? "Connexion…" : "Se connecter"}
      </button>
    </form>
  );
}

export default function LoginPage() {
  return (
    <div className="min-h-screen grid lg:grid-cols-[1fr_1.1fr]">
      <div className="flex flex-col justify-center px-6 md:px-16 py-12">
        <div className="w-full max-w-sm mx-auto space-y-8">
          <Logo />
          <div className="space-y-1.5">
            <h1 className="text-[26px] font-semibold text-ink">Espace du bureau</h1>
            <p className="text-ink-2">Responsables et trésoriers : encaissez, validez et suivez les cotisations de vos groupes.</p>
          </div>
          <Suspense fallback={<p className="text-muted">Chargement…</p>}>
            <LoginForm />
          </Suspense>
          <p className="text-[12.5px] text-muted">
            Les membres utilisent l&apos;application mobile. Ce tableau de bord est réservé au bureau des tontines (responsable, trésorier) et aux administrateurs.
          </p>
        </div>
      </div>
      {/* The subject's own artefact: a cotisation register, not a stock illustration. */}
      <aside className="hidden lg:flex flex-col justify-center bg-brand px-16 py-12 text-on-brand">
        <div className="max-w-md space-y-8">
          <p className="text-[13px] uppercase tracking-[0.12em] opacity-80">Le carnet de la tontine, sans le carnet</p>
          <div className="rounded-xl bg-on-brand/10 p-5 space-y-3 text-[13.5px]">
            {[
              ["Tour 1 · Ibrahim", "4 / 4 payés", "100%"],
              ["Tour 2 · Mariam", "3 / 4 payés", "75%"],
              ["Tour 3 · Seydou", "1 / 4 payé", "25%"],
            ].map(([t, p, w]) => (
              <div key={t} className="space-y-1.5">
                <div className="flex justify-between"><span>{t}</span><span className="opacity-80">{p}</span></div>
                <div className="h-1.5 rounded-full bg-on-brand/20"><div className="h-full rounded-full bg-on-brand" style={{ width: w }} /></div>
              </div>
            ))}
          </div>
          <ul className="space-y-3 text-[14px] opacity-90">
            <li>L&apos;argent reste hors de l&apos;application : vous enregistrez, vous validez.</li>
            <li>Chaque action est inscrite dans un journal chaîné, impossible à modifier sans que cela se voie.</li>
          </ul>
        </div>
      </aside>
    </div>
  );
}
