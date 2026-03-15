import { LatestLinkCard } from "@/components/latest-link-card";
import {
  formatReleaseDate,
  formatReleaseSize,
  getDocsUrl,
  getDownloadsUrl,
  getLatestRelease,
  getReleaseCatalog,
  getWebsiteUrl,
} from "@/lib/releases";

export const dynamic = "force-dynamic";
const installationGuideUrl = "https://docs.glitchtrader.com/installation-guide-troubleshooting";

function EmptyState({
  error,
}: {
  error: string | null;
}) {
  return (
    <section className="download-grid rounded-[2rem] p-6 sm:p-8">
      <p className="text-xs font-semibold uppercase tracking-[0.24em] text-glitch-orange">Status</p>
      <h2 className="mt-4 text-2xl font-semibold tracking-tight text-white">
        No download is available right now
      </h2>
      <p className="mt-3 max-w-2xl text-sm leading-7 text-zinc-300">
        We are preparing the next release. Please check back shortly.
      </p>
      {error ? (
        <p className="mt-3 text-sm text-zinc-400">Temporary issue: {error}</p>
      ) : null}
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
              <p className="mt-6 inline-flex rounded-full border border-glitch-teal/40 bg-glitch-teal/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-glitch-teal">
                Official Download
              </p>
              <h1 className="mt-5 max-w-3xl text-4xl font-semibold tracking-tight text-white sm:text-5xl">
                Download Glitch
                <span className="block">for NinjaTrader 8</span>
              </h1>
              <p className="mt-4 max-w-2xl text-base leading-8 text-zinc-300">
                Install the latest release in minutes, or pick a specific version from the history below.
              </p>
            </div>

            <LatestLinkCard latestUrl={`${downloadsUrl.replace(/\/$/, "")}/latest`} />
          </div>
        </header>

        {latestRelease ? (
          <section className="download-grid rounded-[2rem] p-6 sm:p-8">
            <div className="flex flex-col gap-6 lg:flex-row lg:items-end lg:justify-between">
              <div className="max-w-3xl">
                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-glitch-teal">Latest Version</p>
                <h2 className="mt-3 text-3xl font-semibold tracking-tight text-white sm:text-4xl">
                  Glitch {latestRelease.version}
                </h2>
                <p className="mt-3 max-w-2xl text-sm leading-7 text-zinc-300">
                  Use this package for a fresh install or upgrade. Download, import in NinjaTrader, and launch Glitch.
                </p>
              </div>

              <div className="grid gap-3 rounded-[1.75rem] border border-white/8 bg-white/[0.02] p-5 text-sm text-zinc-300 sm:grid-cols-[minmax(0,1fr)_110px_140px] sm:gap-4">
                <div className="min-w-0 text-center sm:text-left">
                  <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">File</p>
                  <p className="mt-2 overflow-hidden text-ellipsis whitespace-nowrap font-medium text-white" title={latestRelease.fileName}>
                    {latestRelease.fileName}
                  </p>
                </div>
                <div className="text-center">
                  <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">Size</p>
                  <p className="mt-2 font-medium text-white">{formatReleaseSize(latestRelease.size)}</p>
                </div>
                <div className="text-center">
                  <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">Released</p>
                  <p className="mt-2 font-medium text-white">{formatReleaseDate(latestRelease.uploadedAt)}</p>
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
                href={installationGuideUrl}
                className="inline-flex h-12 items-center justify-center rounded-full border border-glitch-teal px-6 text-sm font-medium text-glitch-teal transition-colors hover:bg-glitch-teal/10"
              >
                Installation Guide
              </a>
            </div>

            <div className="mt-6 rounded-[1.5rem] border border-white/8 bg-white/[0.02] p-4">
              <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">SHA-256</p>
              <code
                className="mt-2 block overflow-hidden text-ellipsis whitespace-nowrap font-mono text-[11px] text-zinc-200"
                title={latestRelease.sha256}
              >
                {latestRelease.sha256}
              </code>
            </div>
          </section>
        ) : (
          <EmptyState error={catalog.error} />
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
              <div className="hidden grid-cols-[minmax(0,1fr)_96px_96px_132px] gap-3 border-b border-white/8 bg-white/[0.03] px-5 py-3 text-xs font-semibold uppercase tracking-[0.18em] text-zinc-500 sm:grid">
                <span>Package</span>
                <span className="text-center">Version</span>
                <span className="text-center">Size</span>
                <span className="text-center">Released</span>
              </div>

              {catalog.releases.length > 0 ? (
                <div className="divide-y divide-white/8">
                  {catalog.releases.map((release) => (
                    <a
                      key={release.pathname}
                      href={release.downloadPath}
                      className="grid gap-3 px-5 py-4 text-center transition-colors hover:bg-white/[0.03] sm:grid-cols-[minmax(0,1fr)_96px_96px_132px] sm:items-center sm:gap-3 sm:text-left"
                    >
                      <div className="min-w-0">
                        <p className="overflow-hidden text-ellipsis whitespace-nowrap font-medium text-white sm:text-left" title={release.fileName}>
                          {release.fileName}
                        </p>
                      </div>
                      <span className="text-center text-sm text-zinc-200">{release.version}</span>
                      <span className="text-center text-sm text-zinc-400">{formatReleaseSize(release.size)}</span>
                      <span className="text-center text-sm text-zinc-400">{formatReleaseDate(release.uploadedAt)}</span>
                      <div
                        className="sm:col-span-4 mt-1 overflow-hidden text-ellipsis whitespace-nowrap rounded-lg border border-white/8 bg-black/25 px-2.5 py-1 text-center font-mono text-[11px] text-zinc-500 sm:text-left"
                        title={`SHA-256: ${release.sha256}`}
                      >
                        SHA-256: {release.sha256}
                      </div>
                    </a>
                  ))}
                </div>
              ) : (
                <div className="px-5 py-8 text-sm text-zinc-400">
                  A release has not been published yet.
                </div>
              )}
            </div>
          </section>

          <aside className="flex flex-col gap-6">
            <section className="download-grid rounded-[2rem] p-6">
              <p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-orange">Quick Install</p>
              <h2 className="mt-3 text-2xl font-semibold tracking-tight text-white">Get up and running</h2>
              <ol className="mt-4 space-y-3 text-sm leading-7 text-zinc-300">
                <li>1. Download the latest zip package.</li>
                <li>2. NinjaTrader &gt; Tools &gt; Import &gt; NinjaScript Add-On.</li>
                <li>3. Select downloaded file, restart NinjaTrader when done.</li>
                <li>4. Open Glitch, validate your license in Settings.</li>
              </ol>
            </section>

            <section className="download-grid rounded-[2rem] p-6">
              <p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-teal">Need Help?</p>
              <div className="mt-4 flex flex-col gap-3">
                <a
                  href={websiteUrl}
                  className="inline-flex h-11 items-center justify-center rounded-full border border-white/10 px-5 text-sm text-zinc-200 transition-colors hover:bg-white/[0.04]"
                >
                  Website
                </a>
                <a
                  href={installationGuideUrl}
                  className="inline-flex h-11 items-center justify-center rounded-full border border-white/10 px-5 text-sm text-zinc-200 transition-colors hover:bg-white/[0.04]"
                >
                  Installation Guide &amp; Troubleshoot
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
