import { NextRequest } from "next/server";
import { proxyToApi, UUID_REGEX } from "@/lib/proxy";

export async function GET(request: NextRequest) {
  const tontineId = request.nextUrl.searchParams.get("tontineId");
  const q = tontineId && UUID_REGEX.test(tontineId) ? `?tontineId=${tontineId}` : "";
  return proxyToApi(request, `/api/v1/dashboard/gestionnaire/tontine${q}`);
}
