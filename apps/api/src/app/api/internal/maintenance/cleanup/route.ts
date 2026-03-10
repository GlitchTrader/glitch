import { NextRequest } from "next/server";
import { requireCronToken } from "@/lib/admin-auth";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { runMaintenanceCleanup } from "@/lib/maintenance";

export const runtime = "nodejs";

export async function GET(request: NextRequest) {
  const requestId = getRequestId(request);
  const auth = requireCronToken(request);
  if (!auth.ok) {
    return errorResponse(requestId, 401, auth.code, auth.message);
  }

  try {
    const summary = await runMaintenanceCleanup();
    return jsonResponse({
      ok: true,
      requestId,
      summary,
    });
  } catch (error) {
    return errorResponse(
      requestId,
      500,
      "maintenance_cleanup_failed",
      "Failed to execute maintenance cleanup.",
      error instanceof Error ? error.message : String(error),
    );
  }
}
