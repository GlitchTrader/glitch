export const siteName = "Glitch";
export const siteDescription =
  "Glitch helps prop traders protect eval and funded accounts with compliance enforcement, replication guardrails, Glitch Score analytics, and macro context.";

export function resolveSiteUrl(fallback = "https://www.glitchtrader.com") {
  const value =
    process.env.NEXT_PUBLIC_SITE_URL?.trim() ||
    process.env.VERCEL_PROJECT_PRODUCTION_URL?.trim();

  if (!value) {
    return new URL(fallback);
  }

  return new URL(value.startsWith("http") ? value : `https://${value}`);
}

export function absoluteUrl(path: string) {
  return new URL(path, resolveSiteUrl()).toString();
}
