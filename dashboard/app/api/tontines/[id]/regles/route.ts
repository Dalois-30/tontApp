import { NextRequest, NextResponse } from "next/server";
import { proxyToApi, UUID_REGEX } from "@/lib/proxy";

export async function PUT(request: NextRequest, { params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const body = await request.json().catch(() => null);
  if (!UUID_REGEX.test(id) || !body) return NextResponse.json({ error: "Requête invalide" }, { status: 400 });
  return proxyToApi(request, `/api/v1/tontines/${id}/regles`, {
    method: "PUT",
    body: { adhesionEnCours: String(body.adhesionEnCours), rattrapageAdhesionTardive: Boolean(body.rattrapageAdhesionTardive) },
  });
}
