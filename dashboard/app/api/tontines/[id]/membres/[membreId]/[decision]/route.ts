import { NextRequest, NextResponse } from "next/server";
import { proxyToApi, UUID_REGEX } from "@/lib/proxy";

const DECISIONS = new Set(["accepter", "refuser"]);

export async function POST(
  request: NextRequest,
  { params }: { params: Promise<{ id: string; membreId: string; decision: string }> }
) {
  const { id, membreId, decision } = await params;
  if (!UUID_REGEX.test(id) || !UUID_REGEX.test(membreId) || !DECISIONS.has(decision)) {
    return NextResponse.json({ error: "Requête invalide" }, { status: 400 });
  }
  return proxyToApi(request, `/api/v1/tontines/${id}/membres/${membreId}/${decision}`, { method: "POST" });
}
