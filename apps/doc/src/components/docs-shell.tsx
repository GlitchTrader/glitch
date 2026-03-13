import Image from "next/image";
import Link from "next/link";
import { getDocNavigation } from "@/lib/docs";
import { websiteUrl } from "@/lib/site";

type DocsShellProps = {
  activeSlug: string | null;
  children: React.ReactNode;
};

function NavGroups({ activeSlug }: { activeSlug: string | null }) {
  const groups = getDocNavigation();

  return (
    <div className="space-y-6">
      {groups.map((group) => (
        <div key={group.title}>
          <p className="mb-3 text-[11px] font-semibold uppercase tracking-[0.22em] text-glitch-teal">{group.title}</p>
          <div className="space-y-1.5">
            {group.items.map((item) => {
              const isActive = item.slug === activeSlug || (item.slug === null && activeSlug === null);

              return (
                <Link
                  key={item.href}
                  href={item.href}
                  className={`block rounded-2xl px-3 py-2.5 text-sm transition ${
                    isActive
                      ? "bg-glitch-teal/12 text-white ring-1 ring-glitch-teal/35"
                      : "text-zinc-400 hover:bg-white/[0.04] hover:text-white"
                  }`}
                >
                  {item.label}
                </Link>
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
}

export function DocsShell({ activeSlug, children }: DocsShellProps) {
  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100">
      <header className="sticky top-0 z-40 border-b border-white/10 bg-zinc-950/92 backdrop-blur-xl">
        <div className="mx-auto flex max-w-[1440px] items-center justify-between gap-4 px-4 py-3 sm:px-6 lg:px-8">
          <Link href="/" className="flex items-center gap-3" aria-label="Glitch Docs home">
            <Image
              src="/images/branding/Glitch Wordmark.svg"
              alt="Glitch"
              width={130}
              height={40}
              className="h-7 w-auto"
              unoptimized
              priority
            />
            <span className="rounded-full border border-white/10 bg-white/[0.04] px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-zinc-300">
              Docs
            </span>
          </Link>

          <nav className="hidden items-center gap-3 sm:flex">
            <a
              href={websiteUrl}
              className="rounded-full border border-white/10 px-4 py-2 text-sm text-zinc-300 transition hover:border-white/20 hover:bg-white/[0.04] hover:text-white"
            >
              Website
            </a>
            <a
              href={`${websiteUrl}/pricing`}
              className="rounded-full bg-glitch-orange px-4 py-2 text-sm font-medium text-white transition hover:opacity-90"
            >
              Pricing
            </a>
          </nav>
        </div>
      </header>

      <div className="mx-auto max-w-[1440px] px-4 pb-16 pt-6 sm:px-6 lg:px-8">
        <div className="mb-6 lg:hidden">
          <details className="rounded-[1.5rem] border border-white/10 bg-white/[0.03]">
            <summary className="cursor-pointer list-none px-5 py-4 text-sm font-medium text-zinc-100">Documentation menu</summary>
            <div className="border-t border-white/10 px-4 py-4">
              <NavGroups activeSlug={activeSlug} />
            </div>
          </details>
        </div>

        <div className="grid gap-8 lg:grid-cols-[280px_minmax(0,1fr)] xl:grid-cols-[300px_minmax(0,1fr)] xl:gap-10">
          <aside className="hidden lg:block">
            <div className="sticky top-24 rounded-[2rem] border border-white/10 bg-white/[0.03] p-5 shadow-[0_30px_80px_rgba(0,0,0,0.28)]">
              <div className="rounded-[1.5rem] border border-white/10 bg-[radial-gradient(circle_at_top,rgba(26,188,156,0.16),transparent_58%),linear-gradient(180deg,rgba(255,255,255,0.05),rgba(255,255,255,0.02))] p-4">
                <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-glitch-teal">Glitch Docs</p>
                <p className="mt-3 text-sm leading-6 text-zinc-300">
                  Product documentation for the AddOn, bridge indicator, persistence model, and contracts that make Glitch work in real NinjaTrader workflows.
                </p>
              </div>
              <div className="mt-6">
                <NavGroups activeSlug={activeSlug} />
              </div>
              <div className="mt-6 rounded-[1.5rem] border border-white/10 bg-white/[0.02] p-4">
                <p className="text-sm font-semibold text-white">Looking for pricing or onboarding?</p>
                <p className="mt-2 text-sm leading-6 text-zinc-400">
                  Pricing, offer pages, and member actions stay on the main website.
                </p>
                <a
                  href={websiteUrl}
                  className="mt-4 inline-flex rounded-full border border-white/10 px-4 py-2 text-sm text-zinc-200 transition hover:border-white/20 hover:bg-white/[0.04] hover:text-white"
                >
                  Back to glitchtrader.com
                </a>
              </div>
            </div>
          </aside>

          <main className="min-w-0">{children}</main>
        </div>
      </div>
    </div>
  );
}
