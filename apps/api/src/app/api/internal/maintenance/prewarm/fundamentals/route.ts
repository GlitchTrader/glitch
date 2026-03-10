import { NextRequest } from "next/server";
import { requireCronToken } from "@/lib/admin-auth";
import { readOptionalEnv } from "@/lib/env";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getMarketFundamentalSnapshot } from "@/lib/market-fundamentals";

export const runtime = "nodejs";

const defaultInstruments = [
  "MNQ",
  "MGC",
  "MES",
  "MYM",
  "M2K",
  "MCL",
  "NG",
  "HG",
  "BTCUSD",
  "ETHUSD",
];

function readPrewarmInstruments(): string[] {
  const raw = readOptionalEnv("FUNDAMENTALS_PREWARM_INSTRUMENTS");
  if (!raw) {
    return defaultInstruments;
  }

  const values = raw
    .split(",")
    .map((value) => value.trim().toUpperCase())
    .filter((value) => value.length > 0);

  return values.length > 0 ? values : defaultInstruments;
}

export async function GET(request: NextRequest) {
  const requestId = getRequestId(request);
  const auth = requireCronToken(request);
  if (!auth.ok) {
    return errorResponse(requestId, 401, auth.code, auth.message);
  }

  const instruments = readPrewarmInstruments();
  try {
    const warmedAtByInstrument: Record<string, string> = {};
    for (const instrument of instruments) {
      const snapshot = await getMarketFundamentalSnapshot(instrument);
      warmedAtByInstrument[instrument] = snapshot.generatedAtIso;
    }

    return jsonResponse({
      ok: true,
      requestId,
      warmedCount: instruments.length,
      warmedInstruments: instruments,
      warmedAtByInstrument,
    });
  } catch (error) {
    return errorResponse(
      requestId,
      500,
      "maintenance_prewarm_failed",
      "Failed to prewarm fundamentals cache.",
      error instanceof Error ? error.message : String(error),
    );
  }
}
