import Link from "next/link";
import { Logo } from "@/components/Sidebar";
import { Icon } from "@/components/ui";

const fonctions = [
  {
    icon: "cash",
    titre: "Des cotisations sans carnet",
    texte: "Le gestionnaire enregistre chaque cotisation remise en espèces et la valide après avoir vérifié le reçu. Les membres voient leur paiement passer à « validé » sur leur téléphone.",
  },
  {
    icon: "users",
    titre: "Des tours qui tournent seuls",
    texte: "Ordre des bénéficiaires, retardataires, relances par SMS, adhésion en cours de cycle avec rattrapage : les règles sont celles que votre groupe choisit.",
  },
  {
    icon: "target",
    titre: "Des projets collectifs par paliers",
    texte: "Chacun s'engage sur un palier par échéance. Paiements partiels, avance, changement de palier et dépenses suivent des règles configurables.",
  },
  {
    icon: "shield",
    titre: "Un journal impossible à truquer",
    texte: "Chaque saisie, validation ou rejet est chaînée par empreinte SHA-256. Une ligne modifiée après coup se voit immédiatement.",
  },
];

const offres = [
  { nom: "Gratuit", prix: "0 FCFA", periode: "par mois", points: ["1 tontine", "Jusqu'à 10 membres", "Projets collectifs", "Journal d'audit"], cta: "Commencer", mis: false },
  { nom: "Pro", prix: "2 000 FCFA", periode: "par mois", points: ["Jusqu'à 10 tontines", "Membres illimités", "Relances SMS", "Export PDF"], cta: "Choisir Pro", mis: true },
  { nom: "IMF", prix: "Sur devis", periode: "", points: ["Tontines et membres illimités", "Accompagnement au déploiement", "Pour les institutions de microfinance"], cta: "Nous contacter", mis: false },
];

export default function Accueil() {
  return (
    <div className="min-h-screen bg-bg">
      <header className="border-b border-line bg-surface">
        <nav className="mx-auto max-w-6xl px-4 md:px-8 h-16 flex items-center justify-between">
          <Logo />
          <Link href="/login" className="btn-secondary">Espace gestionnaire</Link>
        </nav>
      </header>

      <section className="mx-auto max-w-6xl px-4 md:px-8 py-16 md:py-20 grid gap-12 lg:grid-cols-[1.1fr_1fr] items-center">
        <div className="space-y-6">
          <p className="eyebrow text-accent">Tontines et projets collectifs</p>
          <h1 className="text-[40px] md:text-[52px] leading-[1.05] font-semibold tracking-tight text-ink">
            Le carnet de la tontine,<br />tenu juste et partagé.
          </h1>
          <p className="text-[17px] text-ink-2 max-w-[52ch]">
            TontApp ne touche pas à votre argent : il reste entre vos mains ou à la banque. L&apos;application tient le registre,
            les membres le consultent sur leur téléphone, le gestionnaire valide chaque mouvement.
          </p>
          <div className="flex flex-wrap gap-3">
            <Link href="/login" className="btn-primary h-11 px-5 text-[14px]">Ouvrir le tableau de bord <Icon name="arrow" /></Link>
            <a href="#fonctions" className="btn-ghost h-11 px-4 text-[14px]">Voir les fonctions</a>
          </div>
        </div>

        {/* A real artefact of the product: the current round, as members see it. */}
        <div className="card p-6 space-y-5 shadow-[0_24px_60px_-30px_rgba(20,26,22,0.35)]">
          <div className="flex items-center justify-between">
            <div>
              <p className="text-[12.5px] text-muted">Tontine du marché · tour 3</p>
              <p className="text-[18px] font-semibold text-ink">Bénéficiaire : Seydou</p>
            </div>
            <span className="inline-flex items-center gap-1 rounded-full bg-good-soft text-good h-6 px-2 text-[12px] font-medium"><Icon name="check" className="h-3.5 w-3.5" />Active</span>
          </div>
          <div className="space-y-2">
            <div className="flex justify-between text-[13px]"><span className="text-ink-2">Collecté</span><span className="font-medium num">30 000 / 40 000 FCFA</span></div>
            <div className="h-2 rounded-full bg-brand-soft"><div className="h-full w-3/4 rounded-full bg-brand" /></div>
          </div>
          <ul className="divide-y divide-line text-[13.5px]">
            {[["Awa", "Validé", "good"], ["Ibrahim", "Validé", "good"], ["Mariam", "À valider", "warn"], ["Fatou", "À payer", "neutral"]].map(([n, s, t]) => (
              <li key={n} className="flex items-center justify-between py-2.5">
                <span className="text-ink">{n}</span>
                <span className={`inline-flex items-center gap-1 rounded-full h-6 px-2 text-[12px] font-medium ${t === "good" ? "bg-good-soft text-good" : t === "warn" ? "bg-warn-soft text-warn" : "bg-surface-2 text-ink-2"}`}>
                  <Icon name={t === "good" ? "check" : t === "warn" ? "clock" : "minus"} className="h-3.5 w-3.5" />{s}
                </span>
              </li>
            ))}
          </ul>
        </div>
      </section>

      <section id="fonctions" className="border-y border-line bg-surface">
        <div className="mx-auto max-w-6xl px-4 md:px-8 py-16 grid gap-10 md:grid-cols-2">
          {fonctions.map((f) => (
            <div key={f.titre} className="flex gap-4">
              <span className="h-10 w-10 shrink-0 rounded-lg bg-brand-soft text-brand grid place-items-center"><Icon name={f.icon} className="h-5 w-5" /></span>
              <div className="space-y-1.5">
                <h2 className="text-[16px] font-semibold text-ink">{f.titre}</h2>
                <p className="text-ink-2 max-w-[56ch]">{f.texte}</p>
              </div>
            </div>
          ))}
        </div>
      </section>

      <section className="mx-auto max-w-6xl px-4 md:px-8 py-16 space-y-8">
        <div className="space-y-2">
          <h2 className="text-[26px] font-semibold text-ink">Offres</h2>
          <p className="text-ink-2">L&apos;abonnement concerne le gestionnaire. Les membres utilisent l&apos;application gratuitement.</p>
        </div>
        <div className="grid gap-4 md:grid-cols-3">
          {offres.map((o) => (
            <div key={o.nom} className={`card p-6 flex flex-col gap-5 ${o.mis ? "border-brand ring-1 ring-brand" : ""}`}>
              <div>
                <p className="text-[15px] font-semibold text-ink">{o.nom}</p>
                <p className="mt-2"><span className="text-[28px] font-semibold text-ink">{o.prix}</span> <span className="text-muted">{o.periode}</span></p>
              </div>
              <ul className="space-y-2 text-[13.5px] text-ink-2 flex-1">
                {o.points.map((pt) => <li key={pt} className="flex gap-2"><Icon name="check" className="h-4 w-4 text-brand mt-0.5" />{pt}</li>)}
              </ul>
              <Link href="/login" className={o.mis ? "btn-primary" : "btn-secondary"}>{o.cta}</Link>
            </div>
          ))}
        </div>
      </section>

      <footer className="border-t border-line">
        <div className="mx-auto max-w-6xl px-4 md:px-8 py-8 flex flex-wrap items-center justify-between gap-4 text-[12.5px] text-muted">
          <Logo />
          <p>L&apos;argent reste hors de l&apos;application. TontApp tient le registre.</p>
        </div>
      </footer>
    </div>
  );
}
