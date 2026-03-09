import { createHash } from "crypto";
import { type UnwrapWebhookEvent } from "@whop/sdk/resources/webhooks";
import { NextRequest } from "next/server";
import { projectWhopMembershipEntitlement } from "@/lib/entitlements-store";
import { errorResponse, getRequestId, jsonResponse, toHeaderRecord } from "@/lib/http";
import { markWebhookEventProcessed, registerWebhookEvent } from "@/lib/idempotency-store";
import { getWhopWebhookClient, getWhopWebhookSecretCandidates } from "@/lib/whop";

export const runtime = "nodejs";

type WhopWebhookEvent = UnwrapWebhookEvent;

interface WebhookHandlingResult {
  handled: boolean;
  reason: string | null;
}

function sha256(input: string): string {
  return createHash("sha256").update(input, "utf8").digest("hex");
}

async function handleWebhookEvent(event: WhopWebhookEvent): Promise<WebhookHandlingResult> {
  switch (event.type) {
    case "membership.activated":
    case "membership_activated":
    case "membership.deactivated":
    case "membership_deactivated":
    case "membership.cancel_at_period_end_changed":
    case "membership_cancel_at_period_end_changed": {
      return projectWhopMembershipEntitlement(event.data);
    }
    default: {
      return {
        handled: false,
        reason: "event_not_mapped",
      };
    }
  }
}

export async function POST(request: NextRequest) {
  const requestId = getRequestId(request);
  let rawBody = "";

  try {
    rawBody = await request.text();
    if (rawBody.trim().length === 0) {
      return errorResponse(
        requestId,
        400,
        "empty_body",
        "Webhook request body is required.",
      );
    }
  } catch (error) {
    return errorResponse(
      requestId,
      400,
      "body_read_error",
      "Unable to read webhook body.",
      error instanceof Error ? error.message : String(error),
    );
  }

  let webhookEvent: WhopWebhookEvent | null = null;
  try {
    const headerRecord = toHeaderRecord(request.headers);
    const client = getWhopWebhookClient();
    const secretCandidates = getWhopWebhookSecretCandidates();
    let lastError: unknown = null;

    for (const key of secretCandidates) {
      try {
        webhookEvent = client.webhooks.unwrap(rawBody, {
          headers: headerRecord,
          key,
        });
        break;
      } catch (error) {
        lastError = error;
      }
    }

    if (!webhookEvent) {
      throw lastError ?? new Error("Unable to verify webhook signature.");
    }
  } catch (error) {
    return errorResponse(
      requestId,
      401,
      "invalid_signature",
      "Webhook signature verification failed.",
      {
        reason: error instanceof Error ? error.message : String(error),
        hint: "Ensure WHOP_WEBHOOK_SECRET matches this webhook endpoint secret.",
      },
    );
  }

  if (!webhookEvent?.id || !webhookEvent?.type) {
    return errorResponse(
      requestId,
      400,
      "invalid_webhook_payload",
      "Webhook payload is missing required fields.",
    );
  }

  const eventId = webhookEvent.id;
  let insertResult:
    | {
        inserted: boolean;
      }
    | undefined;
  try {
    insertResult = await registerWebhookEvent({
      eventId,
      provider: "whop",
      eventType: webhookEvent.type,
      payloadSha256: sha256(rawBody),
    });
  } catch (error) {
    return errorResponse(
      requestId,
      500,
      "idempotency_store_error",
      "Failed to persist webhook event.",
      error instanceof Error ? error.message : String(error),
    );
  }

  if (!insertResult.inserted) {
    return jsonResponse({
      ok: true,
      duplicate: true,
      eventId,
      eventType: webhookEvent.type,
      requestId,
    });
  }

  try {
    const handlingResult = await handleWebhookEvent(webhookEvent);
    await markWebhookEventProcessed(eventId, "processed");

    return jsonResponse({
      ok: true,
      duplicate: false,
      eventId,
      eventType: webhookEvent.type,
      handled: handlingResult.handled,
      handlingReason: handlingResult.reason,
      requestId,
    });
  } catch (error) {
    try {
      await markWebhookEventProcessed(
        eventId,
        "failed",
        error instanceof Error ? error.message : String(error),
      );
    } catch {
      // Marking failed is best-effort in this pass to avoid masking root errors.
    }

    return errorResponse(
      requestId,
      500,
      "webhook_processing_error",
      "Webhook accepted but processing failed.",
      {
        eventId,
        eventType: webhookEvent.type,
      },
    );
  }
}
