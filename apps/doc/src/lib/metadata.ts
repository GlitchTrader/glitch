import type { Metadata } from "next";
import type { DocsLocale } from "@/lib/docs-locales";

export const docsSocialImagePath = "/images/Glitch%20Banner.png";

const ogLocaleByDocsLocale: Record<DocsLocale, string> = {
  en: "en_US",
  pt: "pt_BR",
  es: "es_ES",
  zh: "zh_CN",
  fr: "fr_FR",
  ru: "ru_RU",
};

type DocsMetadataInput = {
  title: string;
  description: string;
  canonical: string;
  languages: Record<string, string>;
  locale: DocsLocale;
};

export function buildDocsPageMetadata({
  title,
  description,
  canonical,
  languages,
  locale,
}: DocsMetadataInput): Metadata {
  return {
    title,
    description,
    alternates: { canonical, languages },
    openGraph: {
      siteName: "Glitch Docs",
      title,
      description,
      url: canonical,
      locale: ogLocaleByDocsLocale[locale],
      type: "website",
      images: [
        {
          url: docsSocialImagePath,
          alt: "Glitch documentation banner",
          width: 1536,
          height: 1024,
        },
      ],
    },
    twitter: {
      card: "summary_large_image",
      title,
      description,
      images: [docsSocialImagePath],
    },
  };
}
