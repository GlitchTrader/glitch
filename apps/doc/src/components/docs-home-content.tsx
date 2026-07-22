import Link from "next/link";
import { DocsShell } from "@/components/docs-shell";
import { JsonLd } from "@/components/json-ld";
import { getDocsLead, getDocSummaries } from "@/lib/docs";
import { docsLocaleDetails, installationGuideSlug, type DocsLocale } from "@/lib/docs-locales";
import { docsSiteUrl, resolveSiteUrl, websiteUrl } from "@/lib/site";

const copy: Record<DocsLocale, { eyebrow: string; title: string; guide: string; start: string; useCases: string; open: string }> = {
  en: { eyebrow: "Glitch Documentation", title: "Understand how Standard Glitch actually works.", guide: "Installation and troubleshooting", start: "Start here", useCases: "Focused references", open: "Open page" },
  pt: { eyebrow: "Documentação do Glitch", title: "Entenda como o Glitch Standard realmente funciona.", guide: "Instalação e solução de problemas", start: "Comece aqui", useCases: "Referências específicas", open: "Abrir página" },
  es: { eyebrow: "Documentación de Glitch", title: "Entiende cómo funciona realmente Glitch Standard.", guide: "Instalación y solución de problemas", start: "Empieza aquí", useCases: "Referencias específicas", open: "Abrir página" },
  zh: { eyebrow: "Glitch 文档", title: "了解 Glitch Standard 的实际工作方式。", guide: "安装与故障排除", start: "从这里开始", useCases: "专项参考", open: "打开页面" },
  fr: { eyebrow: "Documentation Glitch", title: "Comprenez le fonctionnement réel de Glitch Standard.", guide: "Installation et dépannage", start: "Commencer ici", useCases: "Références ciblées", open: "Ouvrir la page" },
  ru: { eyebrow: "Документация Glitch", title: "Узнайте, как на самом деле работает Glitch Standard.", guide: "Установка и устранение неполадок", start: "Начните здесь", useCases: "Тематические справочники", open: "Открыть страницу" },
};

export function DocsHomeContent({ locale }: { locale: DocsLocale }) {
  const docs = getDocSummaries(locale);
  const lead = getDocsLead(locale);
  const text = copy[locale];
  const details = docsLocaleDetails[locale];
  const docsOrigin = resolveSiteUrl("NEXT_PUBLIC_DOCS_URL", docsSiteUrl).toString().replace(/\/$/, "");
  const guide = docs.find((doc) => doc.slug === installationGuideSlug) ?? null;
  const references = docs.filter((doc) => doc.slug !== installationGuideSlug);

  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "CollectionPage",
    name: text.eyebrow,
    description: lead.intro,
    url: `${docsOrigin}${locale === "en" ? "" : `/${locale}`}`,
    inLanguage: details.languageTag,
    publisher: { "@type": "Organization", name: "Glitch", url: websiteUrl },
    hasPart: docs.map((doc) => ({ "@type": "TechArticle", headline: doc.title, description: doc.summary, url: `${docsOrigin}${doc.href}` })),
  };

  return (
    <DocsShell activeSlug={null} locale={locale}>
      <JsonLd data={jsonLd} />
      <div className="space-y-8" lang={details.languageTag}>
        <section className="overflow-hidden rounded-[2rem] border border-white/10 bg-[radial-gradient(circle_at_top_right,rgba(255,66,0,0.18),transparent_28%),radial-gradient(circle_at_top_left,rgba(26,188,156,0.18),transparent_32%),linear-gradient(180deg,rgba(255,255,255,0.04),rgba(255,255,255,0.02))] p-7 shadow-[0_30px_90px_rgba(0,0,0,0.28)] sm:p-9">
          <p className="text-[11px] font-semibold uppercase tracking-[0.24em] text-glitch-teal">{text.eyebrow}</p>
          <h1 className="mt-4 max-w-4xl text-4xl font-bold tracking-tight text-white sm:text-5xl">{text.title}</h1>
          <p className="mt-5 max-w-3xl text-lg leading-8 text-zinc-300">{lead.intro}</p>
          <p className="mt-3 max-w-3xl leading-7 text-zinc-400">{lead.secondary}</p>
        </section>

        {guide ? (
          <Link href={guide.href} className="group block rounded-[1.75rem] border border-white/10 bg-white/[0.03] p-6 transition hover:border-glitch-teal/40 hover:bg-white/[0.05] sm:p-7">
            <p className="text-[11px] font-semibold uppercase tracking-[0.2em] text-glitch-teal">{text.guide}</p>
            <h2 className="mt-3 text-2xl font-semibold text-white group-hover:text-glitch-teal sm:text-[2rem]">{guide.navTitle}</h2>
            <p className="mt-3 max-w-4xl text-base leading-7 text-zinc-300">{guide.summary}</p>
            <span className="mt-5 inline-flex text-sm font-medium text-zinc-200">{text.open}</span>
          </Link>
        ) : null}

        <section>
          <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-glitch-teal">{text.start}</p>
          <h2 className="mt-3 text-2xl font-semibold text-white">{text.useCases}</h2>
          <div className="mt-6 grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {references.map((doc) => (
              <Link key={doc.slug} href={doc.href} className="group rounded-[1.6rem] border border-white/10 bg-white/[0.03] p-5 transition hover:border-glitch-teal/40 hover:bg-white/[0.05]">
                <p className="text-[11px] font-semibold uppercase tracking-[0.2em] text-glitch-teal">{doc.section}</p>
                <h3 className="mt-3 text-xl font-semibold text-white group-hover:text-glitch-teal">{doc.navTitle}</h3>
                <p className="mt-3 text-sm leading-6 text-zinc-400">{doc.summary}</p>
                <span className="mt-5 inline-flex text-sm font-medium text-zinc-200">{text.open}</span>
              </Link>
            ))}
          </div>
        </section>
      </div>
    </DocsShell>
  );
}
