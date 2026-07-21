export const docsLocales = ["en", "pt", "es", "zh", "fr", "ru"] as const;

export type DocsLocale = (typeof docsLocales)[number];

export const defaultDocsLocale: DocsLocale = "en";
export const installationGuideSlug = "installation-guide-troubleshooting";

export const docsLocaleDetails: Record<
  DocsLocale,
  {
    languageTag: string;
    label: string;
    fileSuffix: string;
    pageLabels: { language: string; onThisPage: string; previous: string; next: string };
  }
> = {
  en: {
    languageTag: "en-US",
    label: "English",
    fileSuffix: "",
    pageLabels: { language: "Language", onThisPage: "On this page", previous: "Previous", next: "Next" },
  },
  pt: {
    languageTag: "pt-BR",
    label: "Português",
    fileSuffix: ".pt",
    pageLabels: { language: "Idioma", onThisPage: "Nesta página", previous: "Anterior", next: "Próximo" },
  },
  es: {
    languageTag: "es-ES",
    label: "Español",
    fileSuffix: ".es",
    pageLabels: { language: "Idioma", onThisPage: "En esta página", previous: "Anterior", next: "Siguiente" },
  },
  zh: {
    languageTag: "zh-CN",
    label: "中文",
    fileSuffix: ".zh",
    pageLabels: { language: "语言", onThisPage: "本页内容", previous: "上一页", next: "下一页" },
  },
  fr: {
    languageTag: "fr-FR",
    label: "Français",
    fileSuffix: ".fr",
    pageLabels: { language: "Langue", onThisPage: "Sur cette page", previous: "Précédent", next: "Suivant" },
  },
  ru: {
    languageTag: "ru-RU",
    label: "Русский",
    fileSuffix: ".ru",
    pageLabels: { language: "Язык", onThisPage: "На этой странице", previous: "Назад", next: "Далее" },
  },
};

export function isDocsLocale(value: string): value is DocsLocale {
  return docsLocales.includes(value as DocsLocale);
}

export function getInstallationGuideHref(locale: DocsLocale): string {
  return locale === defaultDocsLocale
    ? `/${installationGuideSlug}`
    : `/${locale}/${installationGuideSlug}`;
}

export function getInstallationGuideLanguages(): Record<string, string> {
  return {
    ...Object.fromEntries(
      docsLocales.map((locale) => [docsLocaleDetails[locale].languageTag, getInstallationGuideHref(locale)]),
    ),
    "x-default": getInstallationGuideHref(defaultDocsLocale),
  };
}
