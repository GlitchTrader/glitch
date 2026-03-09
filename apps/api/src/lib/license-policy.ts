export type LicensePlan = "free_lite" | "premium";

export interface LicensePolicy {
  plan: LicensePlan;
  policyVersion: string;
  features: {
    analytics: boolean;
    macro: boolean;
    fundamental: boolean;
    strategies: boolean;
    advancedReplication: boolean;
  };
  limits: {
    maxGroups: number;
    maxFollowersPerGroup: number;
  };
}

export const LICENSE_GRACE_WINDOW_SECONDS = 24 * 60 * 60;
export const LICENSE_DEFAULT_CHECK_IN_SECONDS = 60;
export const LICENSE_POLICY_VERSION = "2026-03-09-v1";

export function resolvePlanFromCode(planCode: string | null | undefined): LicensePlan {
  const normalized = (planCode ?? "").trim().toLowerCase();
  if (!normalized) {
    return "free_lite";
  }

  if (normalized.includes("lite") || normalized.includes("free")) {
    return "free_lite";
  }

  return "premium";
}

export function buildPolicy(plan: LicensePlan): LicensePolicy {
  if (plan === "premium") {
    return {
      plan,
      policyVersion: LICENSE_POLICY_VERSION,
      features: {
        analytics: true,
        macro: true,
        fundamental: true,
        strategies: true,
        advancedReplication: true,
      },
      limits: {
        maxGroups: 25,
        maxFollowersPerGroup: 200,
      },
    };
  }

  return {
    plan: "free_lite",
    policyVersion: LICENSE_POLICY_VERSION,
    features: {
      analytics: false,
      macro: false,
      fundamental: false,
      strategies: false,
      advancedReplication: false,
    },
    limits: {
      maxGroups: 1,
      maxFollowersPerGroup: 2,
    },
  };
}
