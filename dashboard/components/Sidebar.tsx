"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useState } from "react";
import { Icon } from "./ui";

interface NavItem { label: string; href: string; icon: string; exact?: boolean }

const gestionnaireNav: NavItem[] = [
  { label: "Tontine", href: "/gestionnaire", icon: "home", exact: true },
  { label: "Paiements", href: "/gestionnaire/paiements", icon: "cash" },
  { label: "Projets collectifs", href: "/gestionnaire/projets", icon: "target" },
  { label: "Journal d'audit", href: "/gestionnaire/audit", icon: "shield" },
];

const adminNav: NavItem[] = [
  { label: "Vue d'ensemble", href: "/admin", icon: "grid", exact: true },
  { label: "Tontines", href: "/admin/tontines", icon: "list" },
  { label: "Alertes", href: "/admin/alertes", icon: "bell" },
];

export function Logo({ compact = false }: { compact?: boolean }) {
  return (
    <span className="inline-flex items-center gap-2.5">
      <svg viewBox="0 0 32 32" className="h-8 w-8" aria-hidden="true">
        <rect width="32" height="32" rx="8" className="fill-brand" />
        {/* three coins passed around a circle: the rotation of a tontine */}
        <circle cx="16" cy="9.5" r="3.3" className="fill-on-brand" />
        <circle cx="10.2" cy="19.6" r="3.3" className="fill-on-brand" opacity="0.8" />
        <circle cx="21.8" cy="19.6" r="3.3" className="fill-on-brand" opacity="0.6" />
      </svg>
      {!compact && <span className="text-[16px] font-semibold tracking-tight text-ink">TontApp</span>}
    </span>
  );
}

export default function Sidebar({ role, nom }: { role: string; nom: string }) {
  const pathname = usePathname();
  const router = useRouter();
  const [ouvert, setOuvert] = useState(false);
  const items = role === "Admin" ? adminNav : gestionnaireNav;
  const actif = (i: NavItem) => (i.exact ? pathname === i.href : pathname.startsWith(i.href));

  async function deconnexion() {
    await fetch("/api/auth/logout", { method: "POST" });
    router.push("/login");
    router.refresh();
  }

  const nav = (
    <nav className="flex flex-col gap-0.5" aria-label="Navigation principale">
      {items.map((i) => (
        <Link key={i.href} href={i.href} onClick={() => setOuvert(false)}
          aria-current={actif(i) ? "page" : undefined}
          className={`flex items-center gap-3 rounded-lg px-3 h-9 text-[13.5px] font-medium transition-colors ${
            actif(i) ? "bg-brand-soft text-brand" : "text-ink-2 hover:bg-surface-2 hover:text-ink"}`}>
          <Icon name={i.icon} className="h-[18px] w-[18px]" />
          {i.label}
        </Link>
      ))}
    </nav>
  );

  const compte = (
    <div className="flex items-center gap-3 border-t border-line pt-4">
      <span className="h-9 w-9 shrink-0 rounded-full bg-surface-2 text-ink-2 grid place-items-center text-[13px] font-semibold">
        {(nom || "?").split(" ").map((x) => x[0]).slice(0, 2).join("").toUpperCase()}
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-[13px] font-medium text-ink truncate">{nom || "Utilisateur"}</p>
        <p className="text-[12px] text-muted">{role === "Admin" ? "Administrateur" : "Gestionnaire"}</p>
      </div>
      <button onClick={deconnexion} className="btn-ghost btn-sm px-2" aria-label="Se déconnecter" title="Se déconnecter">
        <Icon name="logout" className="h-[18px] w-[18px]" />
      </button>
    </div>
  );

  return (
    <>
      {/* Phone: top bar */}
      <div className="lg:hidden sticky top-0 z-20 flex items-center justify-between border-b border-line bg-surface px-4 h-14">
        <Logo />
        <button className="btn-secondary btn-sm" onClick={() => setOuvert((o) => !o)} aria-expanded={ouvert}>
          {ouvert ? "Fermer" : "Menu"}
        </button>
      </div>
      {ouvert && (
        <div className="lg:hidden border-b border-line bg-surface px-4 py-3 space-y-4">{nav}{compte}</div>
      )}

      {/* Desktop: fixed sidebar */}
      <aside className="hidden lg:flex w-64 shrink-0 flex-col gap-6 border-r border-line bg-surface px-4 py-5 h-screen sticky top-0">
        <div className="px-2"><Logo /></div>
        <div className="space-y-2">
          <p className="eyebrow px-3">{role === "Admin" ? "Plateforme" : "Gestion"}</p>
          {nav}
        </div>
        <div className="mt-auto">{compte}</div>
      </aside>
    </>
  );
}
