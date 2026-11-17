import { NextRequest, NextResponse } from "next/server";
import { UUID_REGEX } from "./tontine";

export { UUID_REGEX } from "./tontine";

const API_BASE_URL = process.env.API_URL ?? "http://localhost:8080";

/**
 * Forwards a request to the ASP.NET API with the JWT from the httpOnly cookie,
 * and relays status + JSON body back to the browser.
 */
export async function proxyToApi(
  request: NextRequest,
  path: string,
  init: { method?: string; body?: unknown } = {}
): Promise<NextResponse> {
  const token = request.cookies.get("accessToken")?.value;
  if (!token) {
    return NextResponse.json({ error: "Non autorisé" }, { status: 401 });
  }

  try {
    const apiRes = await fetch(`${API_BASE_URL}${path}`, {
      method: init.method ?? "GET",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: init.body === undefined ? undefined : JSON.stringify(init.body),
      cache: "no-store",
    });
    const text = await apiRes.text();
    const data = text ? JSON.parse(text) : {};
    return NextResponse.json(data, { status: apiRes.status });
  } catch {
    return NextResponse.json({ error: "Erreur de connexion au serveur" }, { status: 502 });
  }
}

/** Whole number ≥ 1 read from a query value (decimals floored); the default when absent, not finite or < 1. */
function entierPositif(valeur: string | null, defaut: number): number {
  const n = Math.floor(Number(valeur));
  return valeur !== null && Number.isFinite(n) && n >= 1 ? n : defaut;
}

/** Builds a query string with only page/pageSize/tontineId, validated. */
export function pagingQuery(request: NextRequest): string {
  const sp = request.nextUrl.searchParams;
  const page = entierPositif(sp.get("page"), 1);
  const pageSize = Math.min(100, entierPositif(sp.get("pageSize"), 20));
  const tontineId = sp.get("tontineId");
  const t = tontineId && UUID_REGEX.test(tontineId) ? `&tontineId=${tontineId}` : "";
  return `page=${page}&pageSize=${pageSize}${t}`;
}
