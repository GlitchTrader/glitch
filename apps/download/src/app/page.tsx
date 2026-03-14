import Image from "next/image";
import { getDocsUrl, getDownloadsUrl, getLatestRelease, getReleaseCatalog, getWebsiteUrl, formatReleaseDate, formatReleaseSize } from "@/lib/releases";

export const dynamic = "force-dynamic";

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
                Official Download
              </p>
              <h1 className="mt-5 max-w-3xl text-4xl font-semibold tracking-tight text-white sm:text-5xl">
                Download Glitch for NinjaTrader 8
              </h1>
              <p className="mt-4 max-w-2xl text-base leading-8 text-zinc-300">
                Install the latest release in minutes, or pick a specific version from the history below.
              </p>
            </div>

            <div className="flex flex-col gap-3 rounded-[1.75rem] border border-white/8 bg-black/25 p-5 text-sm text-zinc-300 sm:min-w-[320px]">
              <p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-orange">Latest Link</p>
              <code className="overflow-x-auto rounded-2xl border border-white/8 bg-black/30 px-3 py-3 font-mono text-xs text-white">
                {`${downloadsUrl.replace(/\/$/, "")}/latest`}
              </code>
              <p className="text-xs leading-6 text-zinc-400">
                Bookmark this URL to always download the newest release.
              </p>
            </div>
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

              <div className="grid gap-3 rounded-[1.75rem] border border-white/8 bg-white/[0.02] p-5 text-sm text-zinc-300 sm:grid-cols-3 sm:gap-5">
                <div>
                  <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">Released</p>
                  <p className="mt-2 font-medium text-white">{formatReleaseDate(latestRelease.uploadedAt)}</p>
                </div>
                <div>
                  <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">Size</p>
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
                Version Link
              </a>
            </div>

            <div className="mt-6 rounded-[1.5rem] border border-white/8 bg-white/[0.02] p-4">
              <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">SHA-256</p>
              <code className="mt-2 block break-all font-mono text-[11px] text-zinc-200">{latestRelease.sha256}</code>
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
              <div className="hidden grid-cols-[minmax(0,1.4fr)_140px_140px_160px] gap-4 border-b border-white/8 bg-white/[0.03] px-5 py-3 text-xs font-semibold uppercase tracking-[0.18em] text-zinc-500 sm:grid">
                <span>Package</span>
                <span>Version</span>
                <span>Size</span>
                <span>Released</span>
              </div>

              {catalog.releases.length > 0 ? (
                <div className="divide-y divide-white/8">
                  {catalog.releases.map((release) => (
                    <a
                      key={release.pathname}
                      href={release.downloadPath}
                      className="grid gap-3 px-5 py-4 transition-colors hover:bg-white/[0.03] sm:grid-cols-[minmax(0,1.4fr)_140px_140px_160px] sm:items-center"
                    >
                      <div>
                        <p className="font-medium text-white">{release.fileName}</p>
                        <p className="mt-1 break-all font-mono text-[11px] text-zinc-500">SHA-256: {release.sha256}</p>
                      </div>
                      <span className="text-sm text-zinc-200">{release.version}</span>
                      <span className="text-sm text-zinc-400">{formatReleaseSize(release.size)}</span>
                      <span className="text-sm text-zinc-400">{formatReleaseDate(release.uploadedAt)}</span>
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
                <li>2. In NinjaTrader, go to <span className="text-white">Tools &gt; Import &gt; NinjaScript Add-On</span>.</li>
                <li>3. Select the downloaded file and complete import.</li>
                <li>4. Open Glitch and sign in with your licensed account.</li>
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
                  href={docsUrl}
                  className="inline-flex h-11 items-center justify-center rounded-full border border-white/10 px-5 text-sm text-zinc-200 transition-colors hover:bg-white/[0.04]"
                >
                  Installation Docs
                </a>
              </div>
            </section>
          </aside>
        </div>
      </div>
    </main>
  );
}
