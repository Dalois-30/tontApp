import { NextRequest, NextResponse } from "next/server";
import { proxyToApi, UUID_REGEX } from "@/lib/proxy";

const ACTIONS = new Set(["valider", "rejeter"]);

export async function POST(
  request: NextRequest,
  { params }: { params: Promise<{ id: string; action: string }> }
) {
  const { id, action } = await params;
  if (!UUID_REGEX.test(id) || !ACTIONS.has(action)) {
    return NextResponse.json({ error: "Requête invalide" }, { status: 400 });
  }
  const body = await request.json().catch(() => ({}));
  return proxyToApi(request, `/api/v1/versements/${id}/${action}`, {
    method: "POST",
    body: action === "rejeter" ? { raison: String(body.raison ?? "") } : { reference: body.reference ?? null },
  });
}
