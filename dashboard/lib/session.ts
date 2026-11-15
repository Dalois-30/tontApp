import { cookies } from "next/headers";
import { decodeJwt } from "jose";

export interface Session { nom: string; role: string; telephone: string }

/** Display-only identity read from the JWT cookie (the middleware has already verified it). */
export async function getSession(): Promise<Session> {
  const token = (await cookies()).get("accessToken")?.value;
  if (!token) return { nom: "", role: "", telephone: "" };
  try {
    const p = decodeJwt(token) as Record<string, unknown>;
    return {
      nom: String(p["nom"] ?? ""),
      role: String(p["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] ?? ""),
      telephone: String(p["telephone"] ?? ""),
    };
  } catch {
    return { nom: "", role: "", telephone: "" };
  }
}
