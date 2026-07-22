import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { DocPageContent } from "@/components/doc-page-content";
import { getDocPage, getDocSummaries } from "@/lib/docs";
import { docsLocaleDetails, docsLocales, getDocLanguages, isDocsLocale } from "@/lib/docs-locales";

type Props = { params: Promise<{ locale: string; slug: string }> };

export const dynamicParams = false;
export function generateStaticParams() {
  return docsLocales.filter((locale) => locale !== "en").flatMap((locale) => getDocSummaries(locale).map((doc) => ({ locale, slug: doc.slug })));
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { locale, slug } = await params;
  if (!isDocsLocale(locale) || locale === "en") return { title: "Not Found" };
  const doc = getDocPage(slug, locale);
  if (!doc) return { title: "Not Found" };
  return {
    title: doc.title,
    description: doc.summary,
    alternates: { canonical: doc.href, languages: getDocLanguages(slug) },
    openGraph: { locale: docsLocaleDetails[locale].languageTag.replace("-", "_") },
  };
}

export default async function LocalizedDocPage({ params }: Props) {
  const { locale, slug } = await params;
  if (!isDocsLocale(locale) || locale === "en") notFound();
  const doc = getDocPage(slug, locale);
  if (!doc) notFound();
  return <DocPageContent doc={doc} locale={locale} />;
}
