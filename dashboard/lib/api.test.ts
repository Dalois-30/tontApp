import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiFetch, getTokenFromCookies } from "./api";

afterEach(() => vi.unstubAllGlobals());

type Fetch = (url: string, init?: RequestInit) => Promise<Response>;

/** The error a call rejects with (fails the test when it resolves). */
async function erreurDe(appel: Promise<unknown>): Promise<ApiError> {
  try { await appel; } catch (e) { return e as ApiError; }
  throw new Error("l'appel aurait dû échouer");
}

describe("ApiError", () => {
  it("porte le statut HTTP et reste une Error", () => {
    const e = new ApiError(403, "API 403: interdit");
    expect(e).toBeInstanceOf(Error);
    expect(e).toBeInstanceOf(ApiError);
    expect(e.status).toBe(403);
    expect(e.name).toBe("ApiError");
    expect(e.message).toBe("API 403: interdit");
  });
});

describe("apiFetch", () => {
  it("réponse non 2xx : ApiError avec le statut", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("refusé", { status: 403 })));
    const err = await erreurDe(apiFetch("/x", { token: "t" }));
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(403);
    expect(err.message).toBe("API 403: refusé");
  });

  it("API injoignable : ApiError de statut 0", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => { throw new TypeError("fetch failed"); }));
    const err = await erreurDe(apiFetch("/x"));
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(0);
  });

  it("réponse 2xx : JSON, jeton transmis en Bearer", async () => {
    const fetchMock = vi.fn<Fetch>(async () => Response.json({ ok: 1 }));
    vi.stubGlobal("fetch", fetchMock);
    await expect(apiFetch("/x", { token: "jeton" })).resolves.toEqual({ ok: 1 });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toMatch(/\/x$/);
    expect((init?.headers as Record<string, string>).Authorization).toBe("Bearer jeton");
  });
});

describe("apiFetch : en-têtes, adresse, corps vide", () => {
  afterEach(() => { vi.unstubAllEnvs(); vi.resetModules(); });

  it("sans jeton : aucun en-tête Authorization (jamais « Bearer undefined »)", async () => {
    const fetchMock = vi.fn<Fetch>(async () => Response.json({}));
    vi.stubGlobal("fetch", fetchMock);
    await apiFetch("/x");
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const headers = fetchMock.mock.calls[0][1]?.headers as Record<string, string>;
    expect(headers).not.toHaveProperty("Authorization");
    expect(JSON.stringify(headers)).not.toContain("undefined");
  });

  it("adresse de base : NEXT_PUBLIC_API_URL, sinon http://localhost:8080", async () => {
    const fetchMock = vi.fn<Fetch>(async () => Response.json({}));
    vi.stubGlobal("fetch", fetchMock);
    vi.stubEnv("NEXT_PUBLIC_API_URL", "https://api.exemple.test");
    vi.resetModules();
    await (await import("./api")).apiFetch("/api/v1/x");
    vi.stubEnv("NEXT_PUBLIC_API_URL", undefined);
    vi.resetModules();
    await (await import("./api")).apiFetch("/api/v1/x");
    expect(fetchMock.mock.calls.map((c) => c[0])).toEqual(["https://api.exemple.test/api/v1/x", "http://localhost:8080/api/v1/x"]);
  });

  it("2xx sans corps (204) : pas d’erreur, résultat undefined", async () => {
    vi.stubGlobal("fetch", vi.fn<Fetch>(async () => new Response(null, { status: 204 })));
    await expect(apiFetch("/x", { token: "t" })).resolves.toBeUndefined();
  });
});

describe("getTokenFromCookies", () => {
  it("lit le cookie accessToken", () => {
    expect(getTokenFromCookies("a=1; accessToken=abc; b=2")).toBe("abc");
    expect(getTokenFromCookies("a=1")).toBeNull();
    expect(getTokenFromCookies(null)).toBeNull();
  });
});
