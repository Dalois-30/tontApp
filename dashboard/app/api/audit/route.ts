import { NextRequest } from "next/server";
import { proxyToApi, pagingQuery } from "@/lib/proxy";

/** Audit trail of the chosen tontine (tontineId), or of the caller's default tontine. */
export async function GET(request: NextRequest) {
  return proxyToApi(request, `/api/v1/dashboard/gestionnaire/audit?${pagingQuery(request)}`);
}
