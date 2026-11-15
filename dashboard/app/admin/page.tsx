import Link from "next/link";
import { cookies } from "next/headers";
import { apiFetch } from "@/lib/api";
import { formatMontant } from "@/lib/format";
import type { AdminMetrics } from "@/lib/types";
import { Card, EmptyState, Icon, PageHeader, Stat } from "@/components/ui";
import { AlerteLigne } from "@/components/AlerteLigne";

const GRAFANA = process.env.NEXT_PUBLIC_GRAFANA_URL ?? "http://localhost:3001";
const PROMETHEUS = process.env.NEXT_PUBLIC_PROMETHEUS_URL ?? "http://localhost:9090";

export default async function VueEnsembleAdmin() {
  const token = (await cookies()).get("accessToken")?.value ?? "";
  const metrics = await apiFetch<AdminMetrics>("/api/v1/dashboard/admin/metrics", { token }).catch(() => null);

  if (!metrics) {
    return (
      <>
        <PageHeader eyebrow="Plateforme" title="Vue d'ensemble" />
        <div className="notice-err">Impossible de charger les métriques : l&apos;API ne répond pas ou votre session a expiré.</div>
      </>
    );
  }

  const tauxEchec = metrics.smsEnvoyesSemaine + metrics.smsEchecsSemaine > 0
    ? (metrics.smsEchecsSemaine / (metrics.smsEnvoyesSemaine + metrics.smsEchecsSemaine)) * 100 : 0;
  const critiques = metrics.alertes.filter((a) => a.severity === "critical").length;

  return (
    <>
      <PageHeader eyebrow="Plateforme" title="Vue d'ensemble"
        subtitle="L'activité de toutes les tontines sur les sept derniers jours, et ce qui demande votre attention." />

      <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">
        <Stat label="Tontines actives" value={metrics.tontinesActives} />
        <Stat label="Membres inscrits" value={metrics.totalMembres} context="dans toutes les tontines" />
        <Stat label="Cotisations validées (7 jours)" value={formatMontant(metrics.volumeFcfaSemaine)} />
        <Stat label="SMS envoyés (7 jours)" value={metrics.smsEnvoyesSemaine}
          context={metrics.smsEchecsSemaine > 0 ? `${metrics.smsEchecsSemaine} échec(s) · ${tauxEchec.toFixed(1)} %` : "Aucun échec"}
          tone={metrics.smsEchecsSemaine > 0 ? "bad" : "good"} />
      </div>

      <Card title="Alertes" subtitle={critiques > 0 ? `${critiques} alerte(s) critique(s) à traiter en priorité.` : "Rien de critique en ce moment."}
        actions={<Link href="/admin/alertes" className="btn-ghost btn-sm">Tout voir <Icon name="arrow" className="h-3.5 w-3.5" /></Link>}
        bodyClassName="pt-2">
        {metrics.alertes.length === 0
          ? <EmptyState icon="check" title="Aucune alerte">Chaînes d&apos;audit intègres, aucun tour en retard, aucun SMS en échec.</EmptyState>
          : <ul className="divide-y divide-line">{metrics.alertes.map((a) => <AlerteLigne key={a.id} a={a} />)}</ul>}
      </Card>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <Card title="Grafana" subtitle="Tableaux de bord temps réel : latence de l'API, SMS, base de données.">
          <a href={GRAFANA} target="_blank" rel="noreferrer" className="btn-secondary">Ouvrir Grafana <Icon name="arrow" className="h-3.5 w-3.5" /></a>
        </Card>
        <Card title="Prometheus" subtitle="Métriques brutes et requêtes PromQL, dont le taux d'erreur de l'API.">
          <a href={PROMETHEUS} target="_blank" rel="noreferrer" className="btn-secondary">Ouvrir Prometheus <Icon name="arrow" className="h-3.5 w-3.5" /></a>
        </Card>
      </div>
    </>
  );
}
