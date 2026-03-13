import { readBooleanEnv, readOptionalEnv } from "@/lib/env";
import { isProductionRuntime } from "@/lib/security-context";

export type LicensePlan = "free_lite" | "premium";
export type LicenseBillingVariant = "free" | "monthly" | "annual" | "lifetime" | "unknown";

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
export const LICENSE_POLICY_VERSION = "2026-03-12-v2";
const DEFAULT_FREE_LITE_PRODUCT_IDS = ["prod_QX1HIqoZcBywS"];
const DEFAULT_PREMIUM_PRODUCT_IDS = ["prod_nL5dDWWpEq9gF"];

export interface ResolvedLicenseEntitlement {
  plan: LicensePlan;
  billingVariant: LicenseBillingVariant;
  sourceProductId: string | null;
  sourcePlanCode: string | null;
}

function sanitizeCodeToken(value: string | null | undefined): string {
  return (value ?? "")
    .trim()
    .replace(/^['"]|['"]$/g, "")
    .replace(/\\r|\\n|\\t/g, "")
    .trim();
}

function parseCodeSetFromEnv(envName: string, defaults: readonly string[] = []): Set<string> {
  const raw = readOptionalEnv(envName);
  if (!raw) {
    return new Set(
      defaults
        .map((token) => sanitizeCodeToken(token).toLowerCase())
        .filter((token) => token.length > 0),
    );
  }

  return new Set(
    sanitizeCodeToken(raw)
      .split(",")
      .map((token) => sanitizeCodeToken(token).toLowerCase())
      .filter((token) => token.length > 0),
  );
}

function normalizeCode(value: string | null | undefined): string {
  return sanitizeCodeToken(value).toLowerCase();
}

function trimCode(value: string | null | undefined): string | null {
  const trimmed = sanitizeCodeToken(value);
  return trimmed.length > 0 ? trimmed : null;
}

function unionSets(...sets: Set<string>[]): Set<string> {
  const union = new Set<string>();
  for (const set of sets) {
    for (const value of set) {
      union.add(value);
    }
  }

  return union;
}

function readDefaultUnmappedPlan(): LicensePlan {
  const raw = sanitizeCodeToken(readOptionalEnv("WHOP_DEFAULT_ACTIVE_PLAN")).toLowerCase();
  if (raw === "premium") {
    return "premium";
  }

  return "free_lite";
}

function resolveBillingVariant(
  normalizedProductId: string,
  normalizedPlanCode: string,
  explicitFreeLiteProductIds: Set<string>,
  explicitFreeLitePlanCodes: Set<string>,
  explicitPremiumMonthlyPlanCodes: Set<string>,
  explicitPremiumAnnualPlanCodes: Set<string>,
  explicitPremiumLifetimePlanCodes: Set<string>,
): LicenseBillingVariant {
  if (
    (normalizedProductId && explicitFreeLiteProductIds.has(normalizedProductId)) ||
    (normalizedPlanCode && explicitFreeLitePlanCodes.has(normalizedPlanCode)) ||
    normalizedPlanCode === "free_lite"
  ) {
    return "free";
  }

  if (normalizedPlanCode && explicitPremiumMonthlyPlanCodes.has(normalizedPlanCode)) {
    return "monthly";
  }

  if (normalizedPlanCode && explicitPremiumAnnualPlanCodes.has(normalizedPlanCode)) {
    return "annual";
  }

  if (normalizedPlanCode && explicitPremiumLifetimePlanCodes.has(normalizedPlanCode)) {
    return "lifetime";
  }

  return "unknown";
}

export function resolveEntitlementFromSource(
  productId: string | null | undefined,
  planCode: string | null | undefined,
): ResolvedLicenseEntitlement {
  const trimmedProductId = trimCode(productId);
  const trimmedPlanCode = trimCode(planCode);
  const normalizedProductId = normalizeCode(productId);
  const normalizedPlanCode = normalizeCode(planCode);
  const explicitFreeLiteProductIds = parseCodeSetFromEnv("WHOP_FREE_LITE_PRODUCT_IDS", DEFAULT_FREE_LITE_PRODUCT_IDS);
  const explicitPremiumProductIds = parseCodeSetFromEnv("WHOP_PREMIUM_PRODUCT_IDS", DEFAULT_PREMIUM_PRODUCT_IDS);
  const explicitFreeLitePlanCodes = parseCodeSetFromEnv("WHOP_FREE_LITE_PLAN_CODES");
  const explicitPremiumMonthlyPlanCodes = parseCodeSetFromEnv("WHOP_PREMIUM_MONTHLY_PLAN_CODES");
  const explicitPremiumAnnualPlanCodes = parseCodeSetFromEnv("WHOP_PREMIUM_ANNUAL_PLAN_CODES");
  const explicitPremiumLifetimePlanCodes = parseCodeSetFromEnv("WHOP_PREMIUM_LIFETIME_PLAN_CODES");
  const legacyExplicitPremiumPlanCodes = parseCodeSetFromEnv("WHOP_PREMIUM_PLAN_CODES");
  const explicitPremiumPlanCodes = unionSets(
    explicitPremiumMonthlyPlanCodes,
    explicitPremiumAnnualPlanCodes,
    explicitPremiumLifetimePlanCodes,
    legacyExplicitPremiumPlanCodes,
  );
  const defaultUnmappedPlan = readDefaultUnmappedPlan();
  const billingVariant = resolveBillingVariant(
    normalizedProductId,
    normalizedPlanCode,
    explicitFreeLiteProductIds,
    explicitFreeLitePlanCodes,
    explicitPremiumMonthlyPlanCodes,
    explicitPremiumAnnualPlanCodes,
    explicitPremiumLifetimePlanCodes,
  );
  const strictPlanMapping = readBooleanEnv(
    "WHOP_STRICT_PLAN_MAPPING",
    isProductionRuntime() ||
      explicitFreeLiteProductIds.size > 0 ||
      explicitPremiumProductIds.size > 0 ||
      explicitFreeLitePlanCodes.size > 0 ||
      explicitPremiumPlanCodes.size > 0,
  );

  if (normalizedProductId && explicitFreeLiteProductIds.has(normalizedProductId)) {
    return {
      plan: "free_lite",
      billingVariant: "free",
      sourceProductId: trimmedProductId,
      sourcePlanCode: trimmedPlanCode,
    };
  }

  if (normalizedProductId && explicitPremiumProductIds.has(normalizedProductId)) {
    // Product ownership is authoritative for access; plan ids only refine billing/reporting.
    return {
      plan: "premium",
      billingVariant,
      sourceProductId: trimmedProductId,
      sourcePlanCode: trimmedPlanCode,
    };
  }

  if (normalizedPlanCode === "free_lite" || (normalizedPlanCode && explicitFreeLitePlanCodes.has(normalizedPlanCode))) {
    return {
      plan: "free_lite",
      billingVariant: "free",
      sourceProductId: trimmedProductId,
      sourcePlanCode: trimmedPlanCode,
    };
  }

  if (normalizedPlanCode === "premium" || (normalizedPlanCode && explicitPremiumPlanCodes.has(normalizedPlanCode))) {
    return {
      plan: "premium",
      billingVariant,
      sourceProductId: trimmedProductId,
      sourcePlanCode: trimmedPlanCode,
    };
  }

  if (strictPlanMapping) {
    return {
      plan: "free_lite",
      billingVariant,
      sourceProductId: trimmedProductId,
      sourcePlanCode: trimmedPlanCode,
    };
  }

  if (!normalizedProductId && !normalizedPlanCode) {
    return {
      plan: "free_lite",
      billingVariant,
      sourceProductId: null,
      sourcePlanCode: null,
    };
  }

  return {
    plan: defaultUnmappedPlan,
    billingVariant,
    sourceProductId: trimmedProductId,
    sourcePlanCode: trimmedPlanCode,
  };
}

export function resolvePlanFromCode(planCode: string | null | undefined): LicensePlan {
  return resolveEntitlementFromSource(null, planCode).plan;
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
        maxGroups: 10,
        maxFollowersPerGroup: 100,
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
