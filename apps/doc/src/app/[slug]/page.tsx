import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { DocPageContent } from "@/components/doc-page-content";
import { getDocPage, getDocSummaries } from "@/lib/docs";
import { getDocLanguages } from "@/lib/docs-locales";

type DocPageProps = {
  params: Promise<{
    slug: string;
  }>;
};

export const dynamicParams = false;

export async function generateStaticParams() {
  return getDocSummaries().map((doc) => ({ slug: doc.slug }));
}

export async function generateMetadata({ params }: DocPageProps): Promise<Metadata> {
  const { slug } = await params;
  const doc = getDocPage(slug);

  if (!doc) {
    return {
      title: "Not Found",
    };
  }

  return {
    title: doc.title,
    description: doc.summary,
    alternates: {
      canonical: doc.href,
      languages: getDocLanguages(slug),
    },
  };
}

export default async function DocPage({ params }: DocPageProps) {
  const { slug } = await params;
  const doc = getDocPage(slug);

  if (!doc) {
    notFound();
  }

  return <DocPageContent doc={doc} locale="en" />;
}
