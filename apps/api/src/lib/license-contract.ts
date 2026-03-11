import {
  buildPolicy,
  LICENSE_DEFAULT_CHECK_IN_SECONDS,
  LICENSE_GRACE_WINDOW_SECONDS,
  type LicensePlan,
} from "@/lib/license-policy";

export type LicenseMode = "stub" | "database";
export type LicenseStatus = "active" | "inactive";

interface BuildLicenseContractBodyInput {
  requestId: string;
  mode: LicenseMode;
  installationId: string;
  deviceFingerprintHash: string;
  clientVersion?: string | null;
  valid: boolean;
  status: LicenseStatus;
  reason: string | null;
  plan: LicensePlan;
  entitlementStatus?: string | null;
  sourcePlanCode?: string | null;
  nextCheckInSeconds?: number;
  licenseToken?: string | null;
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

export function buildLicenseContractBody(input: BuildLicenseContractBodyInput) {
  const nextCheckInSeconds = clamp(
    input.nextCheckInSeconds ?? LICENSE_DEFAULT_CHECK_IN_SECONDS,
    15,
    3600,
  );
  const policy = buildPolicy(input.plan);

  return {
    ok: true,
    mode: input.mode,
    requestId: input.requestId,
    heartbeat: {
      accepted: true,
      nextCheckInSeconds,
    },
    license: {
      valid: input.valid,
      status: input.status,
      reason: input.reason,
      graceWindowSeconds: LICENSE_GRACE_WINDOW_SECONDS,
    },
    policy,
    entitlement: {
      active: input.valid,
      plan: policy.plan,
      sourcePlanCode: input.sourcePlanCode ?? null,
      status: input.entitlementStatus ?? null,
    },
    licenseToken: input.licenseToken ?? null,
    echo: {
      installationId: input.installationId,
      deviceFingerprintHash: input.deviceFingerprintHash,
      clientVersion: input.clientVersion ?? null,
    },
  };
}
