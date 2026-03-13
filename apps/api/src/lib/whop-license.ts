import type { Membership } from "@whop/sdk/resources/shared";
import {
  EntitlementStoreConfigError,
  projectWhopMembershipEntitlement,
  replaceActiveLicenseBinding,
  revokeActiveLicenseBinding,
} from "@/lib/entitlements-store";
import { resolveEntitlementFromSource } from "@/lib/license-policy";
import { getWhopApiClient, getWhopApiV2BaseUrl } from "@/lib/whop";

export const WHOP_LICENSE_METADATA_INSTALLATION_KEY = "installationId";
export const WHOP_LICENSE_METADATA_FINGERPRINT_KEY = "deviceFingerprintHash";

export type WhopBindingState =
  | "matched"
  | "unbound"
  | "bound_to_other_installation"
  | "device_fingerprint_mismatch"
  | "binding_metadata_conflict";

export class WhopLicenseApiError extends Error {
  code:
    | "whop_membership_lookup_failed"
    | "whop_validate_license_failed"
    | "whop_membership_update_failed";
  status: number | null;
  details: unknown;

  constructor(
    code:
      | "whop_membership_lookup_failed"
      | "whop_validate_license_failed"
      | "whop_membership_update_failed",
    message: string,
    {
      status = null,
      details = null,
    }: {
      status?: number | null;
      details?: unknown;
    } = {},
  ) {
    super(message);
    this.name = "WhopLicenseApiError";
    this.code = code;
    this.status = status;
    this.details = details;
  }
}

export function mapWhopApiErrorToHttpStatus(error: WhopLicenseApiError): number {
  if (error.status === 401 || error.status === 403) {
    return 503;
  }

  if (typeof error.status === "number" && error.status >= 400 && error.status < 500) {
    return 502;
  }

  return 503;
}

export interface WhopBindingInspection {
  state: WhopBindingState;
  metadata: Record<string, unknown>;
  installationId: string | null;
  deviceFingerprintHash: string | null;
}

export interface WhopBindingMetadataSnapshot {
  metadata: Record<string, unknown>;
  installationId: string | null;
  deviceFingerprintHash: string | null;
  hasManagedBinding: boolean;
  hasConflict: boolean;
}

export interface WhopLicenseSnapshot {
  membership: Membership | null;
  active: boolean;
  resolvedEntitlement: ReturnType<typeof resolveEntitlementFromSource>;
}

function normalizeString(value: string | null | undefined): string {
  return (value ?? "").trim();
}

function normalizeFingerprint(value: string | null | undefined): string {
  return normalizeString(value).toLowerCase();
}

function toMetadataRecord(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return {};
  }

  return value as Record<string, unknown>;
}

function readMetadataString(record: Record<string, unknown>, key: string): string | null {
  const rawValue = record[key];
  return typeof rawValue === "string" && rawValue.trim().length > 0
    ? rawValue.trim()
    : null;
}

function buildBindingMetadata(
  installationId: string,
  deviceFingerprintHash: string,
): Record<string, string> {
  return {
    [WHOP_LICENSE_METADATA_INSTALLATION_KEY]: normalizeString(installationId),
    [WHOP_LICENSE_METADATA_FINGERPRINT_KEY]: normalizeFingerprint(deviceFingerprintHash),
  };
}

async function readResponseDetails(response: Response): Promise<unknown> {
  const raw = await response.text();
  if (!raw) {
    return null;
  }

  try {
    return JSON.parse(raw);
  } catch {
    return raw;
  }
}

export async function getWhopMembershipByLicenseKey(
  licenseKey: string,
): Promise<Membership | null> {
  const client = getWhopApiClient();

  try {
    return await client.memberships.retrieve(licenseKey);
  } catch (error) {
    const status = typeof (error as { status?: unknown })?.status === "number"
      ? ((error as { status: number }).status)
      : null;

    if (status === 404) {
      return null;
    }

    throw new WhopLicenseApiError(
      "whop_membership_lookup_failed",
      "Failed to retrieve membership from Whop.",
      {
        status,
        details: error instanceof Error ? error.message : String(error),
      },
    );
  }
}

export function buildWhopLicenseSnapshot(membership: Membership | null): WhopLicenseSnapshot {
  const resolvedEntitlement = membership
    ? resolveEntitlementFromSource(membership.product?.id ?? null, membership.plan?.id ?? null)
    : resolveEntitlementFromSource(null, null);
  const status = normalizeString(membership?.status ?? null).toLowerCase();
  const active =
    status === "active" ||
    status === "trialing" ||
    status === "canceling" ||
    status === "past_due" ||
    status === "completed";

  return {
    membership,
    active,
    resolvedEntitlement,
  };
}

export function inspectWhopMembershipBinding(
  membership: Membership,
  installationId: string,
  deviceFingerprintHash: string,
): WhopBindingInspection {
  const metadata = toMetadataRecord(membership.metadata);
  const metadataKeys = Object.keys(metadata);
  const expectedInstallationId = normalizeString(installationId);
  const expectedFingerprint = normalizeFingerprint(deviceFingerprintHash);
  const actualInstallationId = readMetadataString(
    metadata,
    WHOP_LICENSE_METADATA_INSTALLATION_KEY,
  );
  const actualDeviceFingerprintHash = readMetadataString(
    metadata,
    WHOP_LICENSE_METADATA_FINGERPRINT_KEY,
  );
  const normalizedActualFingerprint = normalizeFingerprint(actualDeviceFingerprintHash);

  if (metadataKeys.length === 0) {
    return {
      state: "unbound",
      metadata,
      installationId: null,
      deviceFingerprintHash: null,
    };
  }

  if (!actualInstallationId && !actualDeviceFingerprintHash) {
    return {
      state: "binding_metadata_conflict",
      metadata,
      installationId: null,
      deviceFingerprintHash: null,
    };
  }

  if (actualInstallationId && actualInstallationId !== expectedInstallationId) {
    return {
      state: "bound_to_other_installation",
      metadata,
      installationId: actualInstallationId,
      deviceFingerprintHash: actualDeviceFingerprintHash,
    };
  }

  if (actualDeviceFingerprintHash && normalizedActualFingerprint !== expectedFingerprint) {
    return {
      state: "device_fingerprint_mismatch",
      metadata,
      installationId: actualInstallationId,
      deviceFingerprintHash: actualDeviceFingerprintHash,
    };
  }

  if (actualInstallationId && actualDeviceFingerprintHash) {
    return {
      state: "matched",
      metadata,
      installationId: actualInstallationId,
      deviceFingerprintHash: actualDeviceFingerprintHash,
    };
  }

  return {
    state: "binding_metadata_conflict",
    metadata,
    installationId: actualInstallationId,
    deviceFingerprintHash: actualDeviceFingerprintHash,
  };
}

export function reasonFromWhopBindingInspection(binding: WhopBindingInspection): string {
  switch (binding.state) {
    case "matched":
      return "active";
    case "unbound":
      return "binding_not_found";
    case "bound_to_other_installation":
      return "bound_to_other_installation";
    case "device_fingerprint_mismatch":
      return "device_fingerprint_mismatch";
    case "binding_metadata_conflict":
    default:
      return "binding_metadata_conflict";
  }
}

export function readWhopMembershipBindingMetadata(
  membership: Membership,
): WhopBindingMetadataSnapshot {
  const metadata = toMetadataRecord(membership.metadata);
  const installationId = readMetadataString(metadata, WHOP_LICENSE_METADATA_INSTALLATION_KEY);
  const deviceFingerprintHash = readMetadataString(
    metadata,
    WHOP_LICENSE_METADATA_FINGERPRINT_KEY,
  );

  return {
    metadata,
    installationId,
    deviceFingerprintHash,
    hasManagedBinding: !!installationId && !!deviceFingerprintHash,
    hasConflict:
      Object.keys(metadata).length > 0 &&
      !(installationId && deviceFingerprintHash),
  };
}

export async function validateWhopMembershipBinding(
  licenseKey: string,
  installationId: string,
  deviceFingerprintHash: string,
): Promise<WhopBindingInspection> {
  const membership = await getWhopMembershipByLicenseKey(licenseKey);
  if (!membership) {
    return {
      state: "binding_metadata_conflict",
      metadata: {},
      installationId: null,
      deviceFingerprintHash: null,
    };
  }

  const initialInspection = inspectWhopMembershipBinding(
    membership,
    installationId,
    deviceFingerprintHash,
  );
  if (initialInspection.state !== "unbound") {
    return initialInspection;
  }

  const client = getWhopApiClient();
  const response = await fetch(
    `${getWhopApiV2BaseUrl()}/memberships/${encodeURIComponent(licenseKey)}/validate_license`,
    {
      method: "POST",
      headers: {
        Authorization: `Bearer ${client.apiKey}`,
        "Content-Type": "application/json",
      },
      cache: "no-store",
      body: JSON.stringify({
        metadata: buildBindingMetadata(installationId, deviceFingerprintHash),
      }),
    },
  );

  if (!response.ok) {
    throw new WhopLicenseApiError(
      "whop_validate_license_failed",
      "Whop rejected the license validation request.",
      {
        status: response.status,
        details: await readResponseDetails(response),
      },
    );
  }

  const refreshedMembership = await getWhopMembershipByLicenseKey(licenseKey);
  if (!refreshedMembership) {
    return {
      state: "binding_metadata_conflict",
      metadata: {},
      installationId: null,
      deviceFingerprintHash: null,
    };
  }

  return inspectWhopMembershipBinding(
    refreshedMembership,
    installationId,
    deviceFingerprintHash,
  );
}

export async function syncWhopMembershipToLocalState(
  membership: Membership,
  {
    installationId,
    deviceFingerprintHash,
  }: {
    installationId?: string | null;
    deviceFingerprintHash?: string | null;
  } = {},
): Promise<void> {
  try {
    const projection = await projectWhopMembershipEntitlement(membership);
    if (
      projection.entitlementId &&
      installationId &&
      installationId.trim().length > 0 &&
      deviceFingerprintHash &&
      deviceFingerprintHash.trim().length > 0
    ) {
      await replaceActiveLicenseBinding(
        projection.entitlementId,
        installationId,
        deviceFingerprintHash,
      );
    }
  } catch (error) {
    if (!(error instanceof EntitlementStoreConfigError)) {
      throw error;
    }
  }
}

export async function rebindWhopMembership(
  licenseKey: string,
  installationId: string,
  deviceFingerprintHash: string,
): Promise<Membership | null> {
  const membership = await getWhopMembershipByLicenseKey(licenseKey);
  if (!membership) {
    return null;
  }

  const client = getWhopApiClient();

  try {
    const reboundMembership = await client.memberships.update(membership.id, {
      metadata: buildBindingMetadata(installationId, deviceFingerprintHash),
    });
    await syncWhopMembershipToLocalState(reboundMembership, {
      installationId,
      deviceFingerprintHash,
    });
    return reboundMembership;
  } catch (error) {
    const status = typeof (error as { status?: unknown })?.status === "number"
      ? ((error as { status: number }).status)
      : null;

    throw new WhopLicenseApiError(
      "whop_membership_update_failed",
      "Failed to update Whop membership metadata.",
      {
        status,
        details: error instanceof Error ? error.message : String(error),
      },
    );
  }
}

export async function clearWhopMembershipBinding(licenseKey: string): Promise<Membership | null> {
  const membership = await getWhopMembershipByLicenseKey(licenseKey);
  if (!membership) {
    return null;
  }

  const client = getWhopApiClient();

  try {
    const clearedMembership = await client.memberships.update(membership.id, {
      metadata: {},
    });
    try {
      const projection = await projectWhopMembershipEntitlement(clearedMembership);
      if (projection.entitlementId) {
        await revokeActiveLicenseBinding(projection.entitlementId);
      }
    } catch (error) {
      if (!(error instanceof EntitlementStoreConfigError)) {
        throw error;
      }
    }
    return clearedMembership;
  } catch (error) {
    const status = typeof (error as { status?: unknown })?.status === "number"
      ? ((error as { status: number }).status)
      : null;

    throw new WhopLicenseApiError(
      "whop_membership_update_failed",
      "Failed to clear Whop membership metadata.",
      {
        status,
        details: error instanceof Error ? error.message : String(error),
      },
    );
  }
}
