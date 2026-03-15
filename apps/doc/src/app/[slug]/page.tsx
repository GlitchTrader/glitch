import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { DocsMarkdown } from "@/components/docs-markdown";
import { DocsShell } from "@/components/docs-shell";
import { JsonLd } from "@/components/json-ld";
import { getAdjacentDocs, getDocPage, getDocSummaries } from "@/lib/docs";
import { docsSiteUrl, resolveSiteUrl, websiteUrl } from "@/lib/site";

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
    },
  };
}

export default async function DocPage({ params }: DocPageProps) {
  const { slug } = await params;
  const doc = getDocPage(slug);

  if (!doc) {
    notFound();
  }

  const adjacent = getAdjacentDocs(slug);
  const docsOrigin = resolveSiteUrl("NEXT_PUBLIC_DOCS_URL", docsSiteUrl).toString().replace(/\/$/, "");
  const pageUrl = `${docsOrigin}${doc.href}`;

  const jsonLd = [
    {
      "@context": "https://schema.org",
      "@type": "TechArticle",
      headline: doc.title,
      description: doc.summary,
      url: pageUrl,
      mainEntityOfPage: pageUrl,
      inLanguage: "en-US",
      author: {
        "@type": "Organization",
        name: "Glitch",
      },
      publisher: {
        "@type": "Organization",
        name: "Glitch",
        url: websiteUrl,
      },
      isPartOf: {
        "@type": "WebSite",
        name: "Glitch Docs",
        url: docsOrigin,
      },
      about: [doc.section, "NinjaTrader", "Glitch AddOn", "GlitchAnalyticsBridge"],
    },
    {
      "@context": "https://schema.org",
      "@type": "BreadcrumbList",
      itemListElement: [
        {
          "@type": "ListItem",
          position: 1,
          name: "Documentation Home",
          item: docsOrigin,
        },
        {
          "@type": "ListItem",
          position: 2,
          name: doc.navTitle,
          item: pageUrl,
        },
      ],
    },
  ];

  return (
    <DocsShell activeSlug={doc.slug}>
      <JsonLd data={jsonLd} />
      <div className="space-y-6">
        <section className="rounded-[2rem] border border-white/10 bg-white/[0.03] p-7 shadow-[0_30px_90px_rgba(0,0,0,0.22)] sm:p-9">
          <p className="text-[11px] font-semibold uppercase tracking-[0.24em] text-glitch-teal">{doc.section}</p>
          <h1 className="mt-4 text-4xl font-bold tracking-tight text-white sm:text-5xl">{doc.title}</h1>
          <p className="mt-4 max-w-3xl text-lg leading-8 text-zinc-300">{doc.summary}</p>
          <p className="mt-3 max-w-3xl text-sm leading-7 text-zinc-400">{doc.spotlight}</p>
        </section>

        <section className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_250px]">
          <div className="rounded-[2rem] border border-white/10 bg-white/[0.03] p-6 shadow-[0_30px_90px_rgba(0,0,0,0.18)] sm:p-8">
            <DocsMarkdown content={doc.content} />
          </div>

          <aside className="space-y-4">
            {doc.headings.length > 0 ? (
              <div className="xl:sticky xl:top-24 rounded-[1.5rem] border border-white/10 bg-white/[0.03] p-5">
                <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-glitch-teal">On this page</p>
                <div className="mt-4 space-y-2">
                  {doc.headings.map((heading) => (
                    <a
                      key={heading.id}
                      href={`#${heading.id}`}
                      className={`block rounded-xl px-3 py-2 text-sm transition hover:bg-white/[0.04] hover:text-white ${
                        heading.level === 3 ? "ml-3 text-zinc-500" : "text-zinc-300"
                      }`}
                    >
                      {heading.text}
                    </a>
                  ))}
                </div>
              </div>
            ) : null}
          </aside>
        </section>

        <nav className="grid gap-3 sm:grid-cols-2">
          {adjacent.previous ? (
            <Link
              href={adjacent.previous.href}
              className="rounded-[1.5rem] border border-white/10 bg-white/[0.03] p-5 transition hover:border-white/20 hover:bg-white/[0.05]"
            >
              <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">Previous</p>
              <p className="mt-2 text-lg font-semibold text-white">{adjacent.previous.navTitle}</p>
            </Link>
          ) : (
            <div />
          )}

          {adjacent.next ? (
            <Link
              href={adjacent.next.href}
              className="rounded-[1.5rem] border border-white/10 bg-white/[0.03] p-5 text-left transition hover:border-white/20 hover:bg-white/[0.05] sm:ml-auto"
            >
              <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">Next</p>
              <p className="mt-2 text-lg font-semibold text-white">{adjacent.next.navTitle}</p>
            </Link>
          ) : (
            <div />
          )}
        </nav>
      </div>
    </DocsShell>
  );
}
