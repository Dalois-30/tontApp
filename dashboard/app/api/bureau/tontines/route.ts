import { NextRequest } from "next/server";
import { proxyToApi } from "@/lib/proxy";

/** Tontines where the caller sits on the bureau, with their roles (tontine selector). */
export async function GET(request: NextRequest) {
  return proxyToApi(request, "/api/v1/dashboard/gestionnaire/tontines");
}
