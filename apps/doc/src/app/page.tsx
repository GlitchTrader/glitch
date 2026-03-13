import Link from "next/link";
import { DocsShell } from "@/components/docs-shell";
import { getDocsLead, getDocSummaries } from "@/lib/docs";

export default function DocsHomePage() {
  const docs = getDocSummaries();
  const lead = getDocsLead();
  const startHere = docs.filter((doc) => ["architecture", "addon", "indicator"].includes(doc.slug));
  const operationalDocs = docs.filter((doc) => ["data-flow-and-bridge", "persistence", "api-reference"].includes(doc.slug));

  return (
    <DocsShell activeSlug={null}>
      <div className="space-y-8">
        <section className="overflow-hidden rounded-[2rem] border border-white/10 bg-[radial-gradient(circle_at_top_right,rgba(255,66,0,0.18),transparent_28%),radial-gradient(circle_at_top_left,rgba(26,188,156,0.18),transparent_32%),linear-gradient(180deg,rgba(255,255,255,0.04),rgba(255,255,255,0.02))] p-7 shadow-[0_30px_90px_rgba(0,0,0,0.28)] sm:p-9">
          <p className="text-[11px] font-semibold uppercase tracking-[0.24em] text-glitch-teal">Glitch Documentation</p>
          <h1 className="mt-4 max-w-4xl text-4xl font-bold tracking-tight text-white sm:text-5xl">
            Understand how Glitch actually works, not just how it is marketed.
          </h1>
          <p className="mt-5 max-w-3xl text-lg leading-8 text-zinc-300">{lead.intro}</p>
          <p className="mt-3 max-w-3xl leading-7 text-zinc-400">{lead.secondary}</p>
          <div className="mt-6 flex flex-wrap gap-2 text-xs font-semibold uppercase tracking-[0.16em] text-zinc-300">
            <span className="rounded-full border border-white/10 bg-white/[0.04] px-3 py-1.5">AddOn + Indicator</span>
            <span className="rounded-full border border-white/10 bg-white/[0.04] px-3 py-1.5">Operational workflows</span>
            <span className="rounded-full border border-white/10 bg-white/[0.04] px-3 py-1.5">Live product docs</span>
          </div>
        </section>

        <section className="grid gap-6 lg:grid-cols-[minmax(0,1.2fr)_minmax(0,0.8fr)]">
          <div className="rounded-[1.75rem] border border-white/10 bg-white/[0.03] p-6">
            <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-glitch-teal">Start here</p>
            <h2 className="mt-3 text-2xl font-semibold text-white">Read in the order an operator would need it.</h2>
            <div className="mt-6 grid gap-4">
              {startHere.map((doc, index) => (
                <Link
                  key={doc.slug}
                  href={doc.href}
                  className="group rounded-[1.35rem] border border-white/10 bg-white/[0.02] p-4 transition hover:border-glitch-teal/40 hover:bg-white/[0.04]"
                >
                  <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-zinc-500">Step {index + 1}</p>
                  <div className="mt-2 flex items-center justify-between gap-4">
                    <h3 className="text-lg font-semibold text-white transition group-hover:text-glitch-teal">{doc.navTitle}</h3>
                    <span className="text-sm text-zinc-500">Open</span>
                  </div>
                  <p className="mt-3 text-sm leading-6 text-zinc-400">{doc.summary}</p>
                </Link>
              ))}
            </div>
          </div>

          <div className="rounded-[1.75rem] border border-white/10 bg-white/[0.03] p-6">
            <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-glitch-teal">Use cases</p>
            <h2 className="mt-3 text-2xl font-semibold text-white">Jump straight to the layer you care about.</h2>
            <div className="mt-6 space-y-4">
              {operationalDocs.map((doc) => (
                <Link
                  key={doc.slug}
                  href={doc.href}
                  className="block rounded-[1.35rem] border border-white/10 bg-white/[0.02] p-4 transition hover:border-glitch-teal/40 hover:bg-white/[0.04]"
                >
                  <p className="text-sm font-semibold text-white">{doc.navTitle}</p>
                  <p className="mt-2 text-sm leading-6 text-zinc-400">{doc.summary}</p>
                </Link>
              ))}
            </div>
          </div>
        </section>

        <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {docs.map((doc) => (
            <Link
              key={doc.slug}
              href={doc.href}
              className="group rounded-[1.6rem] border border-white/10 bg-white/[0.03] p-5 transition hover:border-glitch-teal/40 hover:bg-white/[0.05]"
            >
              <p className="text-[11px] font-semibold uppercase tracking-[0.2em] text-glitch-teal">{doc.section}</p>
              <h2 className="mt-3 text-xl font-semibold text-white transition group-hover:text-glitch-teal">{doc.navTitle}</h2>
              <p className="mt-3 text-sm leading-6 text-zinc-400">{doc.summary}</p>
              <span className="mt-5 inline-flex text-sm font-medium text-zinc-200">Open page</span>
            </Link>
          ))}
        </section>
      </div>
    </DocsShell>
  );
}
