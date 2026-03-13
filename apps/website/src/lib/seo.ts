import type { Metadata } from "next";
import { locales } from "@/i18n/routing";
import { siteName } from "@/lib/site";

const socialImagePath = "/images/Glitch%20Banner.png";

const localeToOgLocale: Record<string, string> = {
  en: "en_US",
  pt: "pt_BR",
  es: "es_ES",
  zh: "zh_CN",
  fr: "fr_FR",
  ru: "ru_RU",
};

type PageMetadataInput = {
  title: string;
  description: string;
  path: string;
  locale?: string;
};

export function buildPageMetadata({ title, description, path, locale }: PageMetadataInput): Metadata {
  const ogLocale = (locale && localeToOgLocale[locale]) || "en_US";
  const localePattern = new RegExp(`^/(${locales.join("|")})(?=$|/)`);
  const pathWithoutLocale = path.replace(localePattern, "") || "/";
  const languageAlternates =
    locale && localePattern.test(path)
      ? Object.fromEntries(locales.map((item) => [item, item === "en" ? `/en${pathWithoutLocale === "/" ? "" : pathWithoutLocale}` : `/${item}${pathWithoutLocale === "/" ? "" : pathWithoutLocale}`]))
      : undefined;

  return {
    title,
    description,
    alternates: {
      canonical: path,
      languages: languageAlternates,
    },
    openGraph: {
      title,
      description,
      url: path,
      type: "website",
      siteName,
      locale: ogLocale,
      images: [
        {
          url: socialImagePath,
          alt: "Glitch trading assistant banner",
        },
      ],
    },
    twitter: {
      card: "summary_large_image",
      title,
      description,
      images: [socialImagePath],
    },
  };
}
