import { LatestLinkCard } from "@/components/latest-link-card";
import {
  formatReleaseDate,
  formatReleaseSize,
  getDocsUrl,
  getDownloadsUrl,
  getLatestRelease,
  getReleaseCatalog,
  getWebsiteUrl,
  type ReleaseRecord,
} from "@/lib/releases";

export const dynamic = "force-dynamic";

const installationGuideUrl = "https://docs.glitchtrader.com/installation-guide-troubleshooting";
const hermesProfileUrl = "https://github.com/GlitchTrader/glitch-hermes-profile";

function ReleaseCard({
  release,
  title,
  description,
  latestPath,
  experimental = false,
}: {
  release: ReleaseRecord | null;
  title: string;
  description: string;
  latestPath: string;
  experimental?: boolean;
}) {
  return (
    <section className="download-grid rounded-[2rem] p-6 sm:p-8">
      <div className="flex flex-wrap items-center gap-3">
        <p className={`text-xs font-semibold uppercase tracking-[0.22em] ${experimental ? "text-glitch-orange" : "text-glitch-teal"}`}>
          {title}
        </p>
        {experimental ? (
          <span className="rounded-full border border-glitch-orange/40 bg-glitch-orange/10 px-2.5 py-1 text-[10px] font-semibold uppercase tracking-[0.16em] text-glitch-orange">
            Experimental
          </span>
        ) : null}
      </div>

      {release ? (
        <>
          <h2 className="mt-4 text-3xl font-semibold tracking-tight text-white">
            Glitch {experimental ? "AI " : ""}{release.version}
          </h2>
          <p className="mt-3 text-sm leading-7 text-zinc-300">{description}</p>

          <dl className="mt-6 grid gap-4 rounded-[1.5rem] border border-white/8 bg-white/[0.02] p-5 text-sm sm:grid-cols-3">
            <div className="min-w-0 sm:col-span-3">
              <dt className="text-xs uppercase tracking-[0.18em] text-zinc-500">File</dt>
              <dd className="mt-2 overflow-hidden text-ellipsis whitespace-nowrap font-medium text-white" title={release.fileName}>
                {release.fileName}
              </dd>
            </div>
            <div>
              <dt className="text-xs uppercase tracking-[0.18em] text-zinc-500">Size</dt>
              <dd className="mt-2 font-medium text-white">{formatReleaseSize(release.size)}</dd>
            </div>
            <div>
              <dt className="text-xs uppercase tracking-[0.18em] text-zinc-500">Released</dt>
              <dd className="mt-2 font-medium text-white">{formatReleaseDate(release.uploadedAt)}</dd>
            </div>
            <div>
              <dt className="text-xs uppercase tracking-[0.18em] text-zinc-500">Status</dt>
              <dd className="mt-2 font-medium capitalize text-white">{release.status}</dd>
            </div>
          </dl>

          <div className="mt-6 flex flex-col gap-3 sm:flex-row">
            <a
              href={latestPath}
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 text-sm font-medium text-white transition-opacity hover:opacity-90"
            >
              Download {experimental ? "AI" : "Standard"}
            </a>
            <a
              href={experimental ? hermesProfileUrl : installationGuideUrl}
              className="inline-flex h-12 items-center justify-center rounded-full border border-glitch-teal px-6 text-sm font-medium text-glitch-teal transition-colors hover:bg-glitch-teal/10"
            >
              {experimental ? "Hermes Setup" : "Installation Guide"}
            </a>
          </div>

          <div className="mt-6 rounded-[1.5rem] border border-white/8 bg-white/[0.02] p-4">
            <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">SHA-256</p>
            <code className="mt-2 block overflow-hidden text-ellipsis whitespace-nowrap font-mono text-[11px] text-zinc-200" title={release.sha256}>
              {release.sha256}
            </code>
          </div>
        </>
      ) : (
        <>
          <h2 className="mt-4 text-2xl font-semibold tracking-tight text-white">Not published yet</h2>
          <p className="mt-3 text-sm leading-7 text-zinc-300">{description}</p>
        </>
      )}
    </section>
  );
}

function ReleaseHistory({
  title,
  releases,
}: {
  title: string;
  releases: ReleaseRecord[];
}) {
  return (
    <section className="download-grid rounded-[2rem] p-6 sm:p-8">
      <div className="flex items-end justify-between gap-4">
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-teal">Versions</p>
          <h2 className="mt-3 text-2xl font-semibold tracking-tight text-white">{title}</h2>
        </div>
        <p className="text-xs uppercase tracking-[0.18em] text-zinc-500">{releases.length} files</p>
      </div>

      <div className="mt-6 overflow-hidden rounded-[1.5rem] border border-white/8">
        {releases.length > 0 ? (
          <div className="divide-y divide-white/8">
            {releases.map((release) => (
              <a
                key={release.pathname}
                href={release.downloadPath}
                className="grid gap-2 px-5 py-4 transition-colors hover:bg-white/[0.03] sm:grid-cols-[minmax(0,1fr)_90px_132px] sm:items-center"
              >
                <div className="min-w-0">
                  <p className="overflow-hidden text-ellipsis whitespace-nowrap font-medium text-white" title={release.fileName}>
                    {release.fileName}
                  </p>
                  <p className="mt-1 overflow-hidden text-ellipsis whitespace-nowrap font-mono text-[10px] text-zinc-500" title={`SHA-256: ${release.sha256}`}>
                    SHA-256: {release.sha256}
                  </p>
                </div>
                <span className="text-sm text-zinc-300 sm:text-center">{formatReleaseSize(release.size)}</span>
                <span className="text-sm text-zinc-400 sm:text-center">{formatReleaseDate(release.uploadedAt)}</span>
              </a>
            ))}
          </div>
        ) : (
          <div className="px-5 py-8 text-sm text-zinc-400">No release has been published on this channel.</div>
        )}
      </div>
    </section>
  );
}

export default async function Home() {
  const websiteUrl = getWebsiteUrl();
  const docsUrl = getDocsUrl();
  const downloadsUrl = getDownloadsUrl().replace(/\/$/, "");
  const [catalog, latestStandard, latestAi] = await Promise.all([
    getReleaseCatalog(),
    getLatestRelease("standard"),
    getLatestRelease("ai"),
  ]);
  const standardReleases = catalog.releases.filter((release) => release.edition === "standard");
  const aiReleases = catalog.releases.filter((release) => release.edition === "ai");

  return (
    <main className="px-4 py-8 sm:px-6 sm:py-10">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
        <header className="download-grid overflow-hidden rounded-[2rem] px-6 py-7 sm:px-8 sm:py-8">
          <div className="flex flex-col gap-8">
            <div className="max-w-3xl">
              <p className="mt-6 inline-flex rounded-full border border-glitch-teal/40 bg-glitch-teal/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-glitch-teal">
                Official Downloads
              </p>
              <h1 className="mt-5 max-w-3xl text-4xl font-semibold tracking-tight text-white sm:text-5xl">
                Download Glitch
                <span className="block">for NinjaTrader 8</span>
              </h1>
              <p className="mt-4 max-w-2xl text-base leading-8 text-zinc-300">
                Standard remains the default update channel. Glitch AI is a separate, explicitly experimental package.
              </p>
            </div>

            <div className="grid gap-4 lg:grid-cols-2">
              <LatestLinkCard
                latestUrl={`${downloadsUrl}/latest`}
                label="Standard latest link"
                note="The stable default used by existing Glitch installations."
              />
              <LatestLinkCard
                latestUrl={`${downloadsUrl}/latest/ai`}
                label="AI latest link"
                note="Experimental AI channel; requires the Glitch Hermes profile."
              />
            </div>
          </div>
        </header>

        {catalog.error ? (
          <section className="download-grid rounded-[2rem] p-6 text-sm text-glitch-orange sm:p-8">
            Release catalog unavailable: {catalog.error}
          </section>
        ) : null}

        <div className="grid gap-8 lg:grid-cols-2">
          <ReleaseCard
            release={latestStandard}
            title="Standard"
            description="The official package for manual trading, account management, analytics, and replication. It contains no Glitch AI tab or Hermes runtime."
            latestPath="/latest"
          />
          <ReleaseCard
            release={latestAi}
            title="Glitch AI"
            description="Experimental AI edition. AI Auto is off after fresh setup. Account selection comes from your configured Glitch group; no profitability, unattended-operation, or live-readiness claim is made."
            latestPath="/latest/ai"
            experimental
          />
        </div>

        <div className="grid gap-8 lg:grid-cols-2">
          <ReleaseHistory title="Standard history" releases={standardReleases} />
          <ReleaseHistory title="AI history" releases={aiReleases} />
        </div>

        <div className="grid gap-8 lg:grid-cols-2">
          <section className="download-grid rounded-[2rem] p-6">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-orange">Standard Install</p>
            <h2 className="mt-3 text-2xl font-semibold tracking-tight text-white">Import into NinjaTrader</h2>
            <ol className="mt-4 space-y-3 text-sm leading-7 text-zinc-300">
              <li>1. Download the Standard ZIP.</li>
              <li>2. NinjaTrader &gt; Tools &gt; Import &gt; NinjaScript Add-On.</li>
              <li>3. Select the ZIP and restart NinjaTrader when prompted.</li>
              <li>4. Open Glitch and validate your license in Settings.</li>
            </ol>
          </section>

          <section className="download-grid rounded-[2rem] p-6">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-teal">AI Install</p>
            <h2 className="mt-3 text-2xl font-semibold tracking-tight text-white">Add the Hermes profile</h2>
            <p className="mt-4 text-sm leading-7 text-zinc-300">
              Import the AI ZIP, then follow the public profile instructions. Profile installation does not start trading or resume cron jobs.
            </p>
            <a
              href={hermesProfileUrl}
              className="mt-5 inline-flex h-11 items-center justify-center rounded-full border border-glitch-teal px-5 text-sm text-glitch-teal transition-colors hover:bg-glitch-teal/10"
            >
              Hermes profile instructions
            </a>
          </section>
        </div>

        <section className="download-grid rounded-[2rem] p-6">
          <p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-teal">Need Help?</p>
          <div className="mt-4 flex flex-col gap-3 sm:flex-row">
            <a href={websiteUrl} className="inline-flex h-11 items-center justify-center rounded-full border border-white/10 px-5 text-sm text-zinc-200 transition-colors hover:bg-white/[0.04]">
              Website
            </a>
            <a href={installationGuideUrl} className="inline-flex h-11 items-center justify-center rounded-full border border-white/10 px-5 text-sm text-zinc-200 transition-colors hover:bg-white/[0.04]">
              Installation Guide
            </a>
            <a href={docsUrl} className="inline-flex h-11 items-center justify-center rounded-full border border-white/10 px-5 text-sm text-zinc-200 transition-colors hover:bg-white/[0.04]">
              Documentation
            </a>
          </div>
        </section>
      </div>
    </main>
  );
}
