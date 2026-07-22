import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { DocPageContent } from "@/components/doc-page-content";
import { getDocPage, getDocSummaries } from "@/lib/docs";
import { docsLocaleDetails, docsLocales, getDocLanguages, isDocsLocale, type DocsLocale } from "@/lib/docs-locales";

type Props = { params: Promise<{ segments: string[] }> };

function resolveArticle(segments: string[]): { locale: DocsLocale; slug: string } | null {
  if (segments.length === 1) return { locale: "en", slug: segments[0] };
  if (segments.length === 2 && isDocsLocale(segments[0]) && segments[0] !== "en") {
    return { locale: segments[0], slug: segments[1] };
  }
  return null;
}

export const dynamicParams = false;

export function generateStaticParams() {
  return [
    ...getDocSummaries("en").map((doc) => ({ segments: [doc.slug] })),
    ...docsLocales
      .filter((locale) => locale !== "en")
      .flatMap((locale) => getDocSummaries(locale).map((doc) => ({ segments: [locale, doc.slug] }))),
  ];
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const route = resolveArticle((await params).segments);
  if (!route) return { title: "Not Found" };
  const doc = getDocPage(route.slug, route.locale);
  if (!doc) return { title: "Not Found" };
  return {
    title: doc.title,
    description: doc.summary,
    alternates: { canonical: doc.href, languages: getDocLanguages(route.slug) },
    openGraph: { locale: docsLocaleDetails[route.locale].languageTag.replace("-", "_") },
  };
}

export default async function ArticlePage({ params }: Props) {
  const route = resolveArticle((await params).segments);
  if (!route) notFound();
  const doc = getDocPage(route.slug, route.locale);
  if (!doc) notFound();
  return <DocPageContent doc={doc} locale={route.locale} />;
}
