import type { MetadataRoute } from "next";
import { absoluteUrl } from "@/lib/site";
import { locales } from "@/i18n/routing";

const pathnames = [
  "",
  "/offer",
  "/pricing",
  "/affiliate",
  "/privacy",
  "/risk-disclosure",
  "/terms",
];

export default function sitemap(): MetadataRoute.Sitemap {
  const lastModified = new Date("2026-03-13T00:00:00.000Z");
  const entries: MetadataRoute.Sitemap = [];

  for (const locale of locales) {
    for (const path of pathnames) {
      const pathname = path ? `/${locale}${path}` : `/${locale}`;
      entries.push({
        url: absoluteUrl(pathname),
        lastModified,
      });
    }
  }

  return entries;
}
