import type { MetadataRoute } from "next";
import { getDocSummaries } from "@/lib/docs";
import { docsSiteUrl, resolveSiteUrl } from "@/lib/site";

export default function sitemap(): MetadataRoute.Sitemap {
  const site = resolveSiteUrl("NEXT_PUBLIC_DOCS_URL", docsSiteUrl);
  const lastModified = new Date();
  const pathnames = ["/", ...getDocSummaries().map((doc) => doc.href)];

  return pathnames.map((pathname) => ({
    url: new URL(pathname, site).toString(),
    lastModified,
  }));
}
