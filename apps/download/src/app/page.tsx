import Image from "next/image";
import { getDocsUrl, getDownloadsUrl, getLatestRelease, getReleaseCatalog, getWebsiteUrl, formatReleaseDate, formatReleaseSize } from "@/lib/releases";

export const dynamic = "force-dynamic";

function EmptyState({
  configured,
  error,
  prefix,
}: {
  configured: boolean;
  error: string | null;
  prefix: string;
}) {
  return (
    <section className="download-grid rounded-[2rem] p-6 sm:p-8">
      <p className="text-xs font-semibold uppercase tracking-[0.24em] text-glitch-orange">
        Release Store
      </p>
      <h2 className="mt-4 text-2xl font-semibold tracking-tight text-white">
        No downloadable release is live yet
      </h2>
      <p className="mt-3 max-w-2xl text-sm leading-7 text-zinc-300">
        {configured
          ? "Blob is configured, but this project does not see any zip releases yet."
          : "This project is not connected to a Vercel Blob store yet, so there is nowhere to list or redirect downloads from."}
      </p>
      {error ? (
        <p className="mt-3 text-sm text-zinc-400">{error}</p>
      ) : null}
      <div className="mt-6 rounded-3xl border border-white/8 bg-white/[0.02] p-5">
        <p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-teal">
          Next Step
        </p>
        <ol className="mt-3 space-y-2 text-sm text-zinc-300">
          <li>1. Create a Vercel Blob store and connect it to this downloads project.</li>
          <li>2. Upload your NinjaTrader export zip under the prefix <code className="rounded bg-white/5 px-1.5 py-0.5 text-zinc-100">{prefix}</code>.</li>
          <li>3. Name the file with a version, for example <code className="rounded bg-white/5 px-1.5 py-0.5 text-zinc-100">Glitch-NT8-0.1.0.zip</code>.</li>
          <li>4. Point Whop to <code className="rounded bg-white/5 px-1.5 py-0.5 text-zinc-100">/latest</code> on this domain.</li>
        </ol>
      </div>
    </section>
  );
}

export default async function Home() {
  const websiteUrl = getWebsiteUrl();
  const docsUrl = getDocsUrl();
  const downloadsUrl = getDownloadsUrl();
  const [catalog, latestRelease] = await Promise.all([getReleaseCatalog(), getLatestRelease()]);

  return (
    <main className="px-4 py-8 sm:px-6 sm:py-10">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
        <header className="download-grid overflow-hidden rounded-[2rem] px-6 py-7 sm:px-8 sm:py-8">
          <div className="flex flex-col gap-8 lg:flex-row lg:items-end lg:justify-between">
            <div className="max-w-3xl">
              <Image
                src="/images/branding/Glitch Logo.svg"
                alt="Glitch"
                width={128}
                height={34}
                className="h-8 w-auto"
                priority
                unoptimized
              />
              <p className="mt-6 inline-flex rounded-full border border-glitch-teal/40 bg-glitch-teal/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-glitch-teal">
                Official Downloads
              </p>
              <h1 className="mt-5 max-w-3xl text-4xl font-semibold tracking-tight text-white sm:text-5xl">
                Clean release downloads for NinjaTrader operators
              </h1>
              <p className="mt-4 max-w-2xl text-base leading-8 text-zinc-300">
                This is the branded release endpoint for Glitch NinjaTrader builds. Use the stable latest link for
                Whop, and use the version list below when you need a specific release.
              </p>
            </div>

            <div className="flex flex-col gap-3 rounded-[1.75rem] border border-white/8 bg-black/25 p-5 text-sm text-zinc-300 sm:min-w-[320px]">
              <p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-orange">Stable Whop URL</p>
              <code className="overflow-x-auto rounded-2xl border border-white/8 bg-black/30 px-3 py-3 font-mono text-xs text-white">
                {`${downloadsUrl.replace(/\/$/, "")}/latest`}
              </code>
              <p className="text-xs leading-6 text-zinc-400">
                Point Whop to this URL. It redirects to the newest uploaded release and triggers the actual file download.
              </p>
            </div>
          </div>
        </header>

        {latestRelease ? (
          <section className="download-grid rounded-[2rem] p-6 sm:p-8">
            <div className="flex flex-col gap-6 lg:flex-row lg:items-end lg:justify-between">
              <div className="max-w-3xl">
                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-glitch-teal">Latest Release</p>
                <h2 className="mt-3 text-3xl font-semibold tracking-tight text-white sm:text-4xl">
                  Glitch NT8 {latestRelease.version}
                </h2>
                <p className="mt-3 max-w-2xl text-sm leading-7 text-zinc-300">
                  Official NinjaTrader import package for the Glitch AddOn and bridge indicator. Use the direct download
                  button below, or send the stable <code className="rounded bg-white/5 px-1.5 py-0.5 text-zinc-100">/latest</code> route to Whop.
                </p>
              </div>

              <div className="grid gap-3 rounded-[1.75rem] border border-white/8 bg-white/[0.02] p-5 text-sm text-zinc-300 sm:grid-cols-3 sm:gap-5">
                <div>
                  <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">Uploaded</p>
                  <p className="mt-2 font-medium text-white">{formatReleaseDate(latestRelease.uploadedAt)}</p>
                </div>
                <div>
                  <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">Package</p>
                  <p className="mt-2 font-medium text-white">{formatReleaseSize(latestRelease.size)}</p>
                </div>
                <div>
                  <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">File</p>
                  <p className="mt-2 truncate font-medium text-white">{latestRelease.fileName}</p>
                </div>
              </div>
            </div>

            <div className="mt-7 flex flex-col gap-3 sm:flex-row">
              <a
                href="/latest"
                className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 text-sm font-medium text-white transition-opacity hover:opacity-90"
              >
                Download Latest
              </a>
              <a
                href={`/download/${latestRelease.slug}`}
                className="inline-flex h-12 items-center justify-center rounded-full border border-glitch-teal px-6 text-sm font-medium text-glitch-teal transition-colors hover:bg-glitch-teal/10"
              >
                Direct Version Link
              </a>
            </div>
          </section>
        ) : (
          <EmptyState configured={catalog.configured} error={catalog.error} prefix={catalog.prefix} />
        )}

        <div className="grid gap-8 xl:grid-cols-[minmax(0,1.5fr)_minmax(320px,0.9fr)]">
          <section className="download-grid rounded-[2rem] p-6 sm:p-8">
            <div className="flex items-end justify-between gap-4">
              <div>
                <p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-teal">Versions</p>
                <h2 className="mt-3 text-2xl font-semibold tracking-tight text-white">Release history</h2>
              </div>
              <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">{catalog.releases.length} files</p>
            </div>

            <div className="mt-6 overflow-hidden rounded-[1.5rem] border border-white/8">
              <div className="hidden grid-cols-[minmax(0,1.4fr)_140px_140px_160px] gap-4 border-b border-white/8 bg-white/[0.03] px-5 py-3 text-xs font-semibold uppercase tracking-[0.18em] text-zinc-500 sm:grid">
                <span>Release</span>
                <span>Version</span>
                <span>Size</span>
                <span>Uploaded</span>
              </div>

              {catalog.releases.length > 0 ? (
                <div className="divide-y divide-white/8">
                  {catalog.releases.map((release) => (
                    <a
                      key={release.pathname}
                      href={`/download/${release.slug}`}
                      className="grid gap-3 px-5 py-4 transition-colors hover:bg-white/[0.03] sm:grid-cols-[minmax(0,1.4fr)_140px_140px_160px] sm:items-center"
                    >
                      <div>
                        <p className="font-medium text-white">{release.fileName}</p>
                        <p className="mt-1 text-xs text-zinc-500">{release.pathname}</p>
                      </div>
                      <span className="text-sm text-zinc-200">{release.version}</span>
                      <span className="text-sm text-zinc-400">{formatReleaseSize(release.size)}</span>
                      <span className="text-sm text-zinc-400">{formatReleaseDate(release.uploadedAt)}</span>
                    </a>
                  ))}
                </div>
              ) : (
                <div className="px-5 py-8 text-sm text-zinc-400">
                  Upload your first zip to Vercel Blob and it will appear here automatically.
                </div>
              )}
            </div>
          </section>

          <aside className="flex flex-col gap-6">
            <section className="download-grid rounded-[2rem] p-6">
              <p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-orange">Install Flow</p>
              <h2 className="mt-3 text-2xl font-semibold tracking-tight text-white">How customers use this</h2>
              <ol className="mt-4 space-y-3 text-sm leading-7 text-zinc-300">
                <li>1. Join through Whop.</li>
                <li>2. Whop opens the stable <code className="rounded bg-white/5 px-1.5 py-0.5 text-zinc-100">/latest</code> link.</li>
                <li>3. The file downloads immediately.</li>
                <li>4. In NinjaTrader, use <span className="text-white">Tools &gt; Import &gt; NinjaScript Add-On</span>.</li>
                <li>5. Validate the license inside Glitch after import.</li>
              </ol>
            </section>

            <section className="download-grid rounded-[2rem] p-6">
              <p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-teal">Operator Links</p>
              <div className="mt-4 flex flex-col gap-3">
                <a
                  href={websiteUrl}
                  className="inline-flex h-11 items-center justify-center rounded-full border border-white/10 px-5 text-sm text-zinc-200 transition-colors hover:bg-white/[0.04]"
                >
                  Website
                </a>
                <a
                  href={docsUrl}
                  className="inline-flex h-11 items-center justify-center rounded-full border border-white/10 px-5 text-sm text-zinc-200 transition-colors hover:bg-white/[0.04]"
                >
                  Documentation
                </a>
              </div>
            </section>
          </aside>
        </div>
      </div>
    </main>
  );
}
