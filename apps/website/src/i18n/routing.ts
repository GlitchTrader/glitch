import { defineRouting } from "next-intl/routing";

/** Locales aligned with AddOn: en-US, pt-BR, es-ES, zh-CN, fr-FR, ru-RU. Use short codes in URL. */
export const locales = ["en", "pt", "es", "zh", "fr", "ru"] as const;
export type Locale = (typeof locales)[number];

export const routing = defineRouting({
  locales: [...locales],
  defaultLocale: "en",
  localePrefix: "always",
  localeDetection: true,
});

export function isLocale(value: string): value is Locale {
  return locales.includes(value as Locale);
}

export function localizePathname(pathname: string, locale: Locale): string {
  const segments = pathname.split("/").filter(Boolean);

  while (segments.length > 0 && isLocale(segments[0])) {
    segments.shift();
  }

  segments.unshift(locale);

  return `/${segments.join("/")}`;
}
