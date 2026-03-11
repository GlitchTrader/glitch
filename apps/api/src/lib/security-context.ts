import { readOptionalEnv } from "@/lib/env";

function normalize(value: string | null): string {
  return (value ?? "").trim().toLowerCase();
}

export function isProductionRuntime(): boolean {
  const vercelEnv = normalize(readOptionalEnv("VERCEL_ENV"));
  if (vercelEnv === "production") {
    return true;
  }

  const nodeEnv = normalize(readOptionalEnv("NODE_ENV"));
  return nodeEnv === "production";
}

