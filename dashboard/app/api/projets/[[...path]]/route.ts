import { NextRequest, NextResponse } from "next/server";
import { proxyToApi } from "@/lib/proxy";

// Forwards /api/projets/... to /api/v1/projets/... (same method and JSON body).
// Each path segment must be a UUID or a known action word: nothing else reaches the API.
const SEGMENT = /^([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|[a-z-]{3,20})$/i;

async function forward(request: NextRequest, params: Promise<{ path?: string[] }>, method: string) {
  const { path = [] } = await params;
  if (path.length > 5 || !path.every((s) => SEGMENT.test(s))) {
    return NextResponse.json({ error: "Requête invalide" }, { status: 400 });
  }
  const qs = request.nextUrl.searchParams.get("expirationJours");
  const query = qs && /^\d{1,2}$/.test(qs) ? `?expirationJours=${qs}` : "";
  const body = method === "GET" ? undefined : await request.json().catch(() => ({}));
  return proxyToApi(request, `/api/v1/projets${path.length ? "/" + path.join("/") : ""}${query}`, { method, body });
}

type Ctx = { params: Promise<{ path?: string[] }> };
export const GET = (r: NextRequest, c: Ctx) => forward(r, c.params, "GET");
export const POST = (r: NextRequest, c: Ctx) => forward(r, c.params, "POST");
export const PUT = (r: NextRequest, c: Ctx) => forward(r, c.params, "PUT");
