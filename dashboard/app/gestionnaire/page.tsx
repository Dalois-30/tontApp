import { cookies } from "next/headers";
import { apiFetch } from "@/lib/api";
import { formatMontant, maskMontant } from "@/lib/format";
import type { TontineDto, VersementDto } from "@/lib/types";
import StatCard from "@/components/StatCard";
import StatusBadge from "@/components/StatusBadge";
import RelancerButton from "@/components/RelancerButton";
import AdhesionsEnAttente from "@/components/AdhesionsEnAttente";

async function getTontine(token: string): Promise<TontineDto | null> {
  try {
    // In a real app, the gestionnaire's tontine ID would come from their profile.
    // For now, we fetch from a dedicated endpoint.
    return await apiFetch<TontineDto>("/api/v1/dashboard/gestionnaire/tontine", {
      token,
    });
  } catch {
    return null;
  }
}

async function getRecentPayments(
  token: string,
  tontineId: string
): Promise<VersementDto[]> {
  try {
    const result = await apiFetch<{ items: VersementDto[] }>(
      `/api/v1/tontines/${tontineId}/versements?page=1&pageSize=10`,
      { token }
    );
    return result.items ?? [];
  } catch {
    return [];
  }
}

export default async function DashboardGestionnairePage() {
  const cookieStore = await cookies();
  const token = cookieStore.get("accessToken")?.value ?? "";

  const tontine = await getTontine(token);

  if (!tontine) {
    return (
      <div className="max-w-4xl">
        <h1 className="text-2xl font-bold text-gray-900 mb-4">
          Tableau de bord Gestionnaire
        </h1>
        <div className="bg-yellow-50 border border-yellow-200 text-yellow-800 px-4 py-3 rounded-md">
          Aucune tontine trouvée. Créez votre première tontine via
          l&apos;application mobile.
        </div>
      </div>
    );
  }

  const payments = await getRecentPayments(token, tontine.id);

  const membresEnRetard = tontine.membres.filter(
    (m) => m.statut === "en_retard"
  );
  const membresPaye = tontine.membres.filter(
    (m) => m.statut === "paye" || m.statut === "confirme"
  );
  const tourActuel = tontine.tours.find((t) => t.status === "Ouvert");
  const membresActifs = tontine.membres.filter((m) => m.statut !== "adhesion_en_attente");
  // Contributors of the current round: the beneficiary and suspended members do not pay.
  const payeurs = membresActifs.filter(
    (m) => m.statut !== "beneficiaire" && m.statut !== "suspendu"
  );
  const demandes = tontine.membres.filter((m) => m.statut === "adhesion_en_attente");
  const arrieres = membresActifs.filter((m) => (m.resteDu ?? 0) > 0);

  return (
    <div className="max-w-6xl space-y-8">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">
          {tontine.nom}
        </h1>
        <p className="text-gray-500 mt-1">{tontine.description}</p>
        <StatusBadge status={tontine.status} />
      </div>

      {/* ── Stats Cards ────────────────────────────────────────────── */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          title="Membres"
          value={`${membresActifs.length} / ${tontine.maxMembres}`}
          subtitle="inscrits"
        />
        <StatCard
          title="Cotisation"
          value={formatMontant(tontine.montantCotisation)}
          subtitle={tontine.periodicite}
        />
        <StatCard
          title="Paiements confirmés"
          value={membresPaye.length}
          subtitle={`sur ${payeurs.length}`}
          trend={payeurs.length > 0 && membresPaye.length === payeurs.length ? "up" : "neutral"}
        />
        <StatCard
          title="En retard"
          value={membresEnRetard.length}
          trend={membresEnRetard.length > 0 ? "down" : "up"}
          trendValue={
            membresEnRetard.length > 0
              ? `${membresEnRetard.length} membre(s)`
              : "Tous à jour"
          }
        />
      </div>

      {demandes.length > 0 && (
        <AdhesionsEnAttente tontineId={tontine.id} demandes={demandes} />
      )}

      {arrieres.length > 0 && (
        <div className="bg-white rounded-lg shadow p-6 border border-amber-200">
          <h2 className="text-lg font-semibold text-amber-900 mb-1">Reste dû sur les tours précédents</h2>
          <p className="text-sm text-gray-500 mb-4">
            Cotisations non réglées des tours passés, y compris le rattrapage des membres arrivés en cours de cycle.
          </p>
          <ul className="divide-y divide-gray-200">
            {arrieres.map((m) => (
              <li key={m.id} className="py-3 flex items-center justify-between gap-4">
                <div>
                  <p className="font-medium text-gray-900">{m.nom}</p>
                  <p className="text-sm text-gray-500">Tours {m.toursImpayes?.join(", ")}</p>
                </div>
                <span className="font-semibold text-amber-900 tabular-nums">{formatMontant(m.resteDu ?? 0)}</span>
              </li>
            ))}
          </ul>
        </div>
      )}

      {/* ── Tour actuel ────────────────────────────────────────────── */}
      {tourActuel && (
        <div className="bg-white rounded-lg shadow p-6 border border-gray-200">
          <h2 className="text-lg font-semibold text-gray-900 mb-4">
            Tour en cours — #{tourActuel.numero}
          </h2>
          {tourActuel.beneficiaire && (
            <p className="text-sm text-gray-600 mb-4">
              Bénéficiaire :{" "}
              <span className="font-medium">
                {tourActuel.beneficiaire.nom}
              </span>
            </p>
          )}
        </div>
      )}

      {/* ── Membres en retard + Relancer ───────────────────────────── */}
      {membresEnRetard.length > 0 && (
        <div className="bg-white rounded-lg shadow p-6 border border-red-200">
          <h2 className="text-lg font-semibold text-red-800 mb-4">
            Membres en retard de paiement
          </h2>
          <ul className="divide-y divide-gray-200">
            {membresEnRetard.map((membre) => (
              <li
                key={membre.id}
                className="py-3 flex items-center justify-between"
              >
                <div>
                  <p className="font-medium text-gray-900">{membre.nom}</p>
                  {membre.telephone && (
                    <p className="text-sm text-gray-500">
                      {membre.telephone}
                    </p>
                  )}
                </div>
                <RelancerButton
                  membreId={membre.id}
                  membreNom={membre.nom}
                  tontineId={tontine.id}
                />
              </li>
            ))}
          </ul>
        </div>
      )}

      {/* ── Derniers paiements ─────────────────────────────────────── */}
      <div className="bg-white rounded-lg shadow border border-gray-200">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-semibold text-gray-900">
            Derniers paiements
          </h2>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Payeur
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Montant
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Statut
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Date
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {payments.map((paiement) => (
                <tr key={paiement.id}>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                    {paiement.payeurNom}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                    {maskMontant(paiement.montant, true)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    <StatusBadge status={paiement.status} />
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {new Date(paiement.dateCreation).toLocaleDateString(
                      "fr-FR"
                    )}
                  </td>
                </tr>
              ))}
              {payments.length === 0 && (
                <tr>
                  <td
                    colSpan={4}
                    className="px-6 py-4 text-center text-sm text-gray-500"
                  >
                    Aucun paiement enregistré.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* ── Lien Audit Trail ───────────────────────────────────────── */}
      <div className="bg-white rounded-lg shadow p-6 border border-gray-200">
        <h2 className="text-lg font-semibold text-gray-900 mb-2">
          Audit Trail
        </h2>
        <p className="text-sm text-gray-500 mb-4">
          Consultez l&apos;historique complet des actions sur cette tontine.
        </p>
        <a
          href="/gestionnaire/audit"
          className="inline-flex items-center px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-md hover:bg-blue-700 transition-colors"
        >
          Voir l&apos;audit trail
        </a>
      </div>
    </div>
  );
}
