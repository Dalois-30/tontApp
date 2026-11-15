import { NextRequest, NextResponse } from "next/server";
import { proxyToApi, UUID_REGEX } from "@/lib/proxy";

export async function POST(request: NextRequest) {
  const body = await request.json().catch(() => null);
  if (
    !body ||
    !UUID_REGEX.test(body.tontineId ?? "") ||
    !UUID_REGEX.test(body.tourId ?? "") ||
    !UUID_REGEX.test(body.membreId ?? "") ||
    typeof body.montant !== "number"
  ) {
    return NextResponse.json({ error: "Données invalides" }, { status: 400 });
  }
  return proxyToApi(request, "/api/v1/versements/manuel", {
    method: "POST",
    body: {
      tontineId: body.tontineId,
      tourId: body.tourId,
      membreId: body.membreId,
      montant: body.montant,
      descriptionPreuve: String(body.descriptionPreuve ?? ""),
    },
  });
}
