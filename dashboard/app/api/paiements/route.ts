import { NextRequest } from "next/server";
import { proxyToApi, pagingQuery } from "@/lib/proxy";

export async function GET(request: NextRequest) {
  return proxyToApi(request, `/api/v1/dashboard/gestionnaire/versements?${pagingQuery(request)}`);
}
