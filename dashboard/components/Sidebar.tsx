"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { createContext, useContext, useEffect, useState } from "react";
import { Icon } from "./ui";
import { libelleRolesBureau, libelleStatutGroupe } from "@/lib/format";
import { avecTontine, PARAM_TONTINE, tontineDemandee, type TontineBureau } from "@/lib/tontine";

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

/** Platform role of the session (JWT, display only: the API enforces every right). */
const RolePlateforme = createContext("");

export function RolePlateformeProvider({ role, children }: { role: string; children: React.ReactNode }) {
  return <RolePlateforme.Provider value={role}>{children}</RolePlateforme.Provider>;
}

/** Creating a tontine or a project still needs the platform "Gestionnaire" role (or Admin). */
export function usePeutCreerGroupe(): boolean {
  const role = useContext(RolePlateforme);
  return role === "Gestionnaire" || role === "Admin";
}

/** The tontine chosen in the bureau's space (valid ?tontine= id), to keep it on the links of a page. */
export function useTontineChoisie(): string | null {
  const d = tontineDemandee(useSearchParams().get(PARAM_TONTINE));
  return d === "invalide" ? null : d;
}

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

/** A tontine of the selector: the list's items, plus a chosen one outside the list (shared link, admin). */
type Option = TontineBureau & { indisponible?: string };

/**
 * Tontine selector of the bureau's space, under its heading. The choice goes in the URL (?tontine=),
 * which every link of the space carries.
 */
function SelecteurTontine({ options, valeur, onChoix }: {
  options: Option[]; valeur: string; onChoix: (id: string) => void;
}) {
  const courante = options.find((t) => t.id === valeur);
  const roles = courante ? libelleRolesBureau(courante) : "";
  const libelle = (t: Option) => t.indisponible
    ?? [t.nom, libelleStatutGroupe(t.status), libelleRolesBureau(t)].filter(Boolean).join(" · ");
  return (
    <div className="px-3 space-y-1.5">
      <label htmlFor="selecteur-tontine" className="sr-only">Tontine</label>
      <select id="selecteur-tontine" data-testid="selecteur-tontine" className="input h-9 text-[13px]"
        value={valeur} onChange={(e) => onChoix(e.target.value)}>
        {options.map((t) => <option key={t.id} value={t.id}>{libelle(t)}</option>)}
      </select>
      {roles && <p data-testid="selecteur-roles" className="text-[12px] text-muted">Vous : {roles}</p>}
    </div>
  );
}

/**
 * Tontines of the caller's bureau, reloaded at each navigation so that an election, a new trésorier or a new
 * tontine shows up; null while loading, "erreur" when it cannot be loaded. Plus the chosen tontine when it is
 * not in the list, with its name and the caller's roles on it (or why it cannot be opened).
 */
function useTontinesDuBureau(actif: boolean, choisie: string | null, cle: string) {
  const [liste, setListe] = useState<TontineBureau[] | "erreur" | null>(null);
  const [horsListe, setHorsListe] = useState<Option | null>(null);

  useEffect(() => {
    if (!actif) return;
    let vivant = true;
    fetch("/api/bureau/tontines")
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(String(r.status)))))
      .then((d: TontineBureau[]) => { if (vivant) setListe(d); })
      .catch(() => { if (vivant) setListe("erreur"); });
    return () => { vivant = false; };
  }, [actif, cle]);

  const dansLaListe = Array.isArray(liste) && liste.some((t) => t.id === choisie);
  useEffect(() => {
    if (!actif || !choisie || liste === null || dansLaListe) { setHorsListe(null); return; }
    let vivant = true;
    fetch(`/api/tontine?tontineId=${encodeURIComponent(choisie)}`)
      .then(async (r) => {
        const d = r.ok ? await r.json() as TontineBureau : null;
        const indisponible = r.ok ? undefined
          : r.status === 403 ? "Tontine hors de votre bureau" : r.status === 404 ? "Tontine introuvable" : "Tontine indisponible";
        if (vivant) setHorsListe({ id: choisie, nom: d?.nom ?? "", status: d?.status ?? "", mesRoles: d?.mesRoles ?? [], indisponible });
      })
      .catch(() => { if (vivant) setHorsListe({ id: choisie, nom: "", status: "", mesRoles: [], indisponible: "Tontine indisponible" }); });
    return () => { vivant = false; };
  }, [actif, choisie, liste, dansLaListe]);

  return { liste, horsListe };
}

export default function Sidebar({ role, nom }: { role: string; nom: string }) {
  const pathname = usePathname();
  const router = useRouter();
  const searchParams = useSearchParams();
  const [ouvert, setOuvert] = useState(false);
  const bureau = pathname.startsWith("/gestionnaire");
  // An administrator may also open the bureau's space (tontines where they sit, or any by link): each space
  // links to the other.
  const items = !bureau
    ? (role === "Admin" ? [...adminNav, { label: "Espace du bureau", href: "/gestionnaire", icon: "home", exact: true }] : gestionnaireNav)
    : role === "Admin" ? [...gestionnaireNav, { label: "Administration", href: "/admin", icon: "grid", exact: true }] : gestionnaireNav;
  // The chosen tontine (valid id only) follows every link of the bureau's space.
  const demandee = tontineDemandee(searchParams.get(PARAM_TONTINE));
  const choisie = bureau && demandee !== "invalide" ? demandee : null;
  const { liste, horsListe } = useTontinesDuBureau(bureau, choisie, `${pathname}?${searchParams.toString()}`);

  // Shown when there is a choice to make (several tontines) or a tontine chosen other than the default one.
  const tontines = Array.isArray(liste) ? liste : [];
  const options: Option[] = horsListe ? [horsListe, ...tontines] : tontines;
  const valeur = choisie ?? tontines[0]?.id ?? "";
  const selecteur = bureau && Array.isArray(liste) && (tontines.length > 1 || (choisie !== null && choisie !== tontines[0]?.id));
  const listeIndisponible = bureau && liste === "erreur";

  function choisir(id: string) {
    setOuvert(false);
    // A project page belongs to no tontine: go back to the list, keeping the choice for the next pages.
    const base = pathname.startsWith("/gestionnaire/projets/") ? "/gestionnaire/projets" : pathname;
    router.push(avecTontine(base, id));
  }
  const actif = (i: NavItem) => (i.exact ? pathname === i.href : pathname.startsWith(i.href));

  async function deconnexion() {
    await fetch("/api/auth/logout", { method: "POST" });
    router.push("/login");
    router.refresh();
  }

  const nav = (
    <nav className="flex flex-col gap-0.5" aria-label="Navigation principale">
      {items.map((i) => (
        <Link key={i.href} href={i.href.startsWith("/gestionnaire") ? avecTontine(i.href, choisie) : i.href} onClick={() => setOuvert(false)}
          aria-current={actif(i) ? "page" : undefined}
          className={`flex items-center gap-3 rounded-lg px-3 h-9 text-[13.5px] font-medium transition-colors ${
            actif(i) ? "bg-brand-soft text-brand" : "text-ink-2 hover:bg-surface-2 hover:text-ink"}`}>
          <Icon name={i.icon} className="h-[18px] w-[18px]" />
          {i.label}
        </Link>
      ))}
    </nav>
  );

  // Heading of the space, then the tontine selector right under it, then the links: the same on phone and desktop.
  const entete = (
    <div className="space-y-2">
      <p className="eyebrow px-3">{role === "Admin" && !bureau ? "Plateforme" : "Espace du bureau"}</p>
      {selecteur && <SelecteurTontine options={options} valeur={valeur} onChoix={choisir} />}
      {listeIndisponible && (
        <p data-testid="selecteur-indisponible" className="px-3 text-[12px] text-muted">
          La liste de vos tontines n&apos;a pas pu être chargée.
        </p>
      )}
      {nav}
    </div>
  );

  const compte = (
    <div className="flex items-center gap-3 border-t border-line pt-4">
      <span className="h-9 w-9 shrink-0 rounded-full bg-surface-2 text-ink-2 grid place-items-center text-[13px] font-semibold">
        {(nom || "?").split(" ").map((x) => x[0]).slice(0, 2).join("").toUpperCase()}
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-[13px] font-medium text-ink truncate">{nom || "Utilisateur"}</p>
        <p className="text-[12px] text-muted">{role === "Admin" ? "Administrateur" : "Espace du bureau"}</p>
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
        <div className="lg:hidden border-b border-line bg-surface px-4 py-3 space-y-4">{entete}{compte}</div>
      )}

      {/* Desktop: fixed sidebar */}
      <aside className="hidden lg:flex w-64 shrink-0 flex-col gap-6 border-r border-line bg-surface px-4 py-5 h-screen sticky top-0">
        <div className="px-2"><Logo /></div>
        {entete}
        <div className="mt-auto">{compte}</div>
      </aside>
    </>
  );
}
