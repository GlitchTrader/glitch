import type { Metadata } from "next";
import type { DownloadLocale } from "@/lib/download-locales";

export const downloadSocialImagePath = "/images/Glitch%20Banner.png";

const ogLocaleByDownloadLocale: Record<DownloadLocale, string> = {
  en: "en_US",
  pt: "pt_BR",
  es: "es_ES",
  zh: "zh_CN",
  fr: "fr_FR",
  ru: "ru_RU",
};

type DownloadMetadataInput = {
  title: string;
  description: string;
  canonical: string;
  languages: Record<string, string>;
  locale: DownloadLocale;
};

export function buildDownloadPageMetadata({
  title,
  description,
  canonical,
  languages,
  locale,
}: DownloadMetadataInput): Metadata {
  return {
    title,
    description,
    alternates: { canonical, languages },
    openGraph: {
      siteName: "Glitch Downloads",
      title,
      description,
      url: canonical,
      locale: ogLocaleByDownloadLocale[locale],
      type: "website",
      images: [
        {
          url: downloadSocialImagePath,
          alt: "Glitch download banner",
          width: 1536,
          height: 1024,
        },
      ],
    },
    twitter: {
      card: "summary_large_image",
      title,
      description,
      images: [downloadSocialImagePath],
    },
  };
}
