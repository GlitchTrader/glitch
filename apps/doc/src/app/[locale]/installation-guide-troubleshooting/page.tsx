import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { DocPageContent } from "@/components/doc-page-content";
import { getLocalizedInstallationGuide } from "@/lib/docs";
import {
  docsLocaleDetails,
  docsLocales,
  getInstallationGuideLanguages,
  isDocsLocale,
} from "@/lib/docs-locales";

type LocalizedGuidePageProps = {
  params: Promise<{
    locale: string;
  }>;
};

export const dynamicParams = false;

export function generateStaticParams() {
  return docsLocales.filter((locale) => locale !== "en").map((locale) => ({ locale }));
}

export async function generateMetadata({ params }: LocalizedGuidePageProps): Promise<Metadata> {
  const { locale: localeValue } = await params;
  if (!isDocsLocale(localeValue) || localeValue === "en") {
    return { title: "Not Found" };
  }

  const doc = getLocalizedInstallationGuide(localeValue);
  return {
    title: doc.title,
    description: doc.summary,
    alternates: {
      canonical: doc.href,
      languages: getInstallationGuideLanguages(),
    },
    openGraph: {
      locale: docsLocaleDetails[localeValue].languageTag.replace("-", "_"),
    },
  };
}

export default async function LocalizedGuidePage({ params }: LocalizedGuidePageProps) {
  const { locale: localeValue } = await params;
  if (!isDocsLocale(localeValue) || localeValue === "en") {
    notFound();
  }

  return <DocPageContent doc={getLocalizedInstallationGuide(localeValue)} locale={localeValue} />;
}
