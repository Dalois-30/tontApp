import { readdirSync } from "node:fs";
import { join, relative } from "node:path";
import { NextRequest } from "next/server";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { pagingQuery } from "./proxy";
import * as adminAlertes from "@/app/api/admin/alertes/route";
import * as adminTontines from "@/app/api/admin/tontines/route";
import * as audit from "@/app/api/audit/route";
import * as bureauTontines from "@/app/api/bureau/tontines/route";
import * as paiements from "@/app/api/paiements/route";
import * as projets from "@/app/api/projets/[[...path]]/route";
import * as relancer from "@/app/api/relancer/route";
import * as tontine from "@/app/api/tontine/route";
import * as decisionMembre from "@/app/api/tontines/[id]/membres/[membreId]/[decision]/route";
import * as regles from "@/app/api/tontines/[id]/regles/route";
import * as versementAction from "@/app/api/versements/[id]/[action]/route";
import * as versementManuel from "@/app/api/versements/manuel/route";

const ID = "3f2a1b4c-5d6e-4f70-8a9b-0c1d2e3f4a5b";
const ID2 = "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d";

const requete = (query: string, jeton: string | null = "jeton", init: { method?: string; body?: unknown } = {}) =>
  new NextRequest(`http://localhost:3000/api/x${query}`, {
    method: init.method ?? "GET",
    headers: jeton ? { cookie: `accessToken=${jeton}` } : {},
    body: init.body === undefined ? undefined : JSON.stringify(init.body),
  });

const params = <T,>(p: T) => ({ params: Promise.resolve(p) });

describe("pagingQuery", () => {
  it("page et taille par défaut", () => {
    expect(pagingQuery(requete(""))).toBe("page=1&pageSize=20");
  });
  it("transmet un tontineId valide", () => {
    expect(pagingQuery(requete(`?page=2&pageSize=50&tontineId=${ID}`))).toBe(`page=2&pageSize=50&tontineId=${ID}`);
  });
  it("écarte un tontineId malformé", () => {
    expect(pagingQuery(requete("?tontineId=abc&evil=1"))).toBe("page=1&pageSize=20");
    expect(pagingQuery(requete(`?tontineId=${ID}%26pageSize%3D1000`))).toBe("page=1&pageSize=20");
  });
  it("borne la taille à 100", () => {
    expect(pagingQuery(requete("?page=3&pageSize=5000"))).toBe("page=3&pageSize=100");
  });
  it("décimales arrondies à l’entier inférieur", () => {
    expect(pagingQuery(requete("?page=2.5&pageSize=10.9"))).toBe("page=2&pageSize=10");
  });
  it("notation exponentielle lue comme un nombre", () => {
    expect(pagingQuery(requete("?page=1e3&pageSize=1e3"))).toBe("page=1000&pageSize=100");
  });
  it("valeur négative, nulle, < 1, vide, non numérique ou infinie : valeur par défaut", () => {
    for (const v of ["-5", "0", "0.5", "", "abc", "Infinity", "-Infinity", "NaN"]) {
      expect(pagingQuery(requete(`?page=${v}&pageSize=${v}`)), `valeur « ${v} »`).toBe("page=1&pageSize=20");
    }
  });
});

type Fetch = (url: string, init?: RequestInit) => Promise<Response>;
let fetchMock: ReturnType<typeof vi.fn<Fetch>>;
beforeEach(() => {
  fetchMock = vi.fn<Fetch>(async () => Response.json({ items: [] }));
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => vi.unstubAllGlobals());

/** Path + query and Authorization header of the single API call. */
const appel = () => {
  expect(fetchMock).toHaveBeenCalledTimes(1);
  const [url, init] = fetchMock.mock.calls[0];
  const u = new URL(url);
  return { chemin: u.pathname + u.search, auth: new Headers(init?.headers).get("Authorization") };
};

/** Every route that needs the session, with a valid call (so that only the token is missing). */
const ROUTES: { fichier: string; appeler: (jeton: string | null) => Promise<Response>; chemin: string }[] = [
  { fichier: "admin/alertes", appeler: (j) => adminAlertes.GET(requete("", j)), chemin: "/api/v1/dashboard/admin/alertes?page=1&pageSize=20" },
  { fichier: "admin/tontines", appeler: (j) => adminTontines.GET(requete("", j)), chemin: "/api/v1/dashboard/admin/tontines?page=1&pageSize=20" },
  { fichier: "audit", appeler: (j) => audit.GET(requete("", j)), chemin: "/api/v1/dashboard/gestionnaire/audit?page=1&pageSize=20" },
  { fichier: "bureau/tontines", appeler: (j) => bureauTontines.GET(requete("", j)), chemin: "/api/v1/dashboard/gestionnaire/tontines" },
  { fichier: "paiements", appeler: (j) => paiements.GET(requete("", j)), chemin: "/api/v1/dashboard/gestionnaire/versements?page=1&pageSize=20" },
  { fichier: "projets/[[...path]]", appeler: (j) => projets.GET(requete("", j), params({ path: [ID] })), chemin: `/api/v1/projets/${ID}` },
  {
    fichier: "relancer",
    appeler: (j) => relancer.POST(requete("", j, { method: "POST", body: { tontineId: ID, membreId: ID2 } })),
    chemin: `/api/v1/tontines/${ID}/members/${ID2}/relancer`,
  },
  { fichier: "tontine", appeler: (j) => tontine.GET(requete("", j)), chemin: "/api/v1/dashboard/gestionnaire/tontine" },
  {
    fichier: "tontines/[id]/membres/[membreId]/[decision]",
    appeler: (j) => decisionMembre.POST(requete("", j, { method: "POST" }), params({ id: ID, membreId: ID2, decision: "accepter" })),
    chemin: `/api/v1/tontines/${ID}/membres/${ID2}/accepter`,
  },
  {
    fichier: "tontines/[id]/regles",
    appeler: (j) => regles.PUT(requete("", j, { method: "PUT", body: { adhesionEnCours: "Bloquer" } }), params({ id: ID })),
    chemin: `/api/v1/tontines/${ID}/regles`,
  },
  {
    fichier: "versements/[id]/[action]",
    appeler: (j) => versementAction.POST(requete("", j, { method: "POST", body: {} }), params({ id: ID, action: "valider" })),
    chemin: `/api/v1/versements/${ID}/valider`,
  },
  {
    fichier: "versements/manuel",
    appeler: (j) => versementManuel.POST(requete("", j, { method: "POST", body: { tontineId: ID, tourId: ID2, membreId: ID, montant: 5000 } })),
    chemin: "/api/v1/versements/manuel",
  },
];

/** Routes that do not need a session: login creates it, logout clears it (no API call with a token). */
const SANS_SESSION = ["auth/login", "auth/logout"];

describe("routes proxy", () => {
  it("chaque route de app/api figure dans la table des tests (ou est sans session)", () => {
    const racine = join(__dirname, "../app/api");
    const fichiers = (readdirSync(racine, { recursive: true }) as string[])
      .filter((f) => f.endsWith("route.ts"))
      .map((f) => relative(racine, join(racine, f)).replace(/\/route\.ts$/, ""))
      .sort();
    expect(fichiers).toEqual([...ROUTES.map((r) => r.fichier), ...SANS_SESSION].sort());
  });

  describe.each(ROUTES)("$fichier", ({ appeler, chemin }) => {
    it("sans jeton : 401, l’API n’est pas appelée", async () => {
      const res = await appeler(null);
      expect(res.status).toBe(401);
      expect(fetchMock).not.toHaveBeenCalled();
    });

    it("avec jeton : appelle l’API au bon chemin en Bearer", async () => {
      const res = await appeler("jeton");
      expect(res.status).toBe(200);
      expect(appel()).toEqual({ chemin, auth: "Bearer jeton" });
    });

    it("statut de l’API relayé (403)", async () => {
      fetchMock.mockImplementationOnce(async () => Response.json({ error: "interdit" }, { status: 403 }));
      const res = await appeler("jeton");
      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(res.status).toBe(403);
    });
  });

  it("audit : tontine choisie transmise", async () => {
    await audit.GET(requete(`?tontineId=${ID}`));
    expect(appel().chemin).toBe(`/api/v1/dashboard/gestionnaire/audit?page=1&pageSize=20&tontineId=${ID}`);
  });

  it("paiements : id malformé écarté (tontine par défaut)", async () => {
    await paiements.GET(requete("?tontineId=pas-un-id&page=3"));
    expect(appel().chemin).toBe("/api/v1/dashboard/gestionnaire/versements?page=3&pageSize=20");
  });

  it("tontine : id valide transmis, id malformé écarté", async () => {
    await tontine.GET(requete(`?tontineId=${ID}`));
    expect(appel().chemin).toBe(`/api/v1/dashboard/gestionnaire/tontine?tontineId=${ID}`);
    fetchMock.mockClear();
    await tontine.GET(requete("?tontineId=x"));
    expect(appel().chemin).toBe("/api/v1/dashboard/gestionnaire/tontine");
  });

  it("corps de l’API relayé avec son statut", async () => {
    fetchMock.mockImplementationOnce(async () => Response.json({ error: "interdit" }, { status: 403 }));
    const res = await tontine.GET(requete(""));
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(await res.json()).toEqual({ error: "interdit" });
  });

  it("API injoignable : 502", async () => {
    fetchMock.mockImplementationOnce(async () => { throw new TypeError("fetch failed"); });
    const res = await paiements.GET(requete(""));
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(res.status).toBe(502);
  });
});
