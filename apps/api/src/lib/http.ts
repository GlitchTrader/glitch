import { randomUUID } from "crypto";
import { NextRequest, NextResponse } from "next/server";

const noStoreHeaders = {
  "Cache-Control": "no-store, max-age=0",
} as const;

export function getRequestId(request: NextRequest): string {
  const existing = request.headers.get("x-request-id");
  if (existing && existing.trim().length > 0) {
    return existing.trim();
  }

  return randomUUID();
}

export function toHeaderRecord(headers: Headers): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [key, value] of headers.entries()) {
    result[key.toLowerCase()] = value;
  }

  return result;
}

export function jsonResponse(
  body: unknown,
  {
    status = 200,
    requestId,
  }: {
    status?: number;
    requestId?: string;
  } = {},
): NextResponse {
  const response = NextResponse.json(body, { status });
  response.headers.set("Cache-Control", noStoreHeaders["Cache-Control"]);
  if (requestId) {
    response.headers.set("x-request-id", requestId);
  }
  return response;
}

export function errorResponse(
  requestId: string,
  status: number,
  code: string,
  message: string,
  details?: unknown,
): NextResponse {
  return jsonResponse(
    {
      ok: false,
      error: {
        code,
        message,
        details: details ?? null,
      },
      requestId,
    },
    {
      status,
      requestId,
    },
  );
}

