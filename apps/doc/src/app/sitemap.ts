import type { MetadataRoute } from "next";
import { getDocSummaries } from "@/lib/docs";
import { docsLocales, getInstallationGuideHref } from "@/lib/docs-locales";
import { docsSiteUrl, resolveSiteUrl } from "@/lib/site";

export default function sitemap(): MetadataRoute.Sitemap {
  const site = resolveSiteUrl("NEXT_PUBLIC_DOCS_URL", docsSiteUrl);
  const lastModified = new Date();
  const pathnames = [
    "/",
    ...getDocSummaries().map((doc) => doc.href),
    ...docsLocales.filter((locale) => locale !== "en").map(getInstallationGuideHref),
  ];

  return pathnames.map((pathname) => ({
    url: new URL(pathname, site).toString(),
    lastModified,
  }));
}
