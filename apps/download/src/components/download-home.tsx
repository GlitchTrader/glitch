import { LatestLinkCard } from "@/components/latest-link-card";
import { downloadCopy, type DownloadLocale } from "@/lib/download-locales";
import {
  formatReleaseDate, formatReleaseSize, getDocsUrl, getDownloadsUrl, getLatestRelease,
  getReleaseCatalog, getWebsiteUrl, type ReleaseRecord,
} from "@/lib/releases";

const hermesProfileUrl = "https://github.com/GlitchTrader/glitch-hermes-profile";

function localizedDocsUrl(locale: DownloadLocale, slug?: string): string {
  const base = getDocsUrl().replace(/\/$/, "");
  const prefix = locale === "en" ? "" : `/${locale}`;
  return `${base}${prefix}${slug ? `/${slug}` : ""}`;
}

function ReleaseCard({ release, locale, ai = false }: { release: ReleaseRecord | null; locale: DownloadLocale; ai?: boolean }) {
  const copy = downloadCopy[locale];
  const description = ai ? copy.aiDescription : copy.standardDescription;
  const latestPath = ai ? "/latest/ai" : "/latest";
  return (
    <section className="download-grid rounded-[2rem] p-6 sm:p-8">
      <div className="flex flex-wrap items-center gap-3">
        <p className={`text-xs font-semibold uppercase tracking-[0.22em] ${ai ? "text-glitch-orange" : "text-glitch-teal"}`}>{ai ? copy.ai : copy.standard}</p>
        {ai ? <span className="rounded-full border border-glitch-orange/40 bg-glitch-orange/10 px-2.5 py-1 text-[10px] font-semibold uppercase tracking-[0.16em] text-glitch-orange">{copy.experimental}</span> : null}
      </div>
      {release ? <>
        <h2 className="mt-4 text-3xl font-semibold tracking-tight text-white">Glitch {ai ? "AI " : ""}{release.version}</h2>
        <p className="mt-3 text-sm leading-7 text-zinc-300">{description}</p>
        <dl className="mt-6 grid gap-4 rounded-[1.5rem] border border-white/8 bg-white/[0.02] p-5 text-sm sm:grid-cols-3">
          <div className="min-w-0 sm:col-span-3"><dt className="text-xs uppercase tracking-[0.18em] text-zinc-500">{copy.file}</dt><dd className="mt-2 overflow-hidden text-ellipsis whitespace-nowrap font-medium text-white" title={release.fileName}>{release.fileName}</dd></div>
          <div><dt className="text-xs uppercase tracking-[0.18em] text-zinc-500">{copy.size}</dt><dd className="mt-2 font-medium text-white">{formatReleaseSize(release.size)}</dd></div>
          <div><dt className="text-xs uppercase tracking-[0.18em] text-zinc-500">{copy.released}</dt><dd className="mt-2 font-medium text-white">{formatReleaseDate(release.uploadedAt, copy.languageTag)}</dd></div>
          <div><dt className="text-xs uppercase tracking-[0.18em] text-zinc-500">{copy.status}</dt><dd className="mt-2 font-medium capitalize text-white">{release.status}</dd></div>
        </dl>
        <div className="mt-6 flex flex-col gap-3 sm:flex-row">
          <a href={latestPath} className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 text-sm font-medium text-white hover:opacity-90">{copy.download} {ai ? "AI" : "Standard"}</a>
          <a href={ai ? hermesProfileUrl : localizedDocsUrl(locale, "installation-guide-troubleshooting")} className="inline-flex h-12 items-center justify-center rounded-full border border-glitch-teal px-6 text-sm font-medium text-glitch-teal hover:bg-glitch-teal/10">{ai ? copy.hermesSetup : copy.installationGuide}</a>
        </div>
        <div className="mt-6 rounded-[1.5rem] border border-white/8 bg-white/[0.02] p-4"><p className="text-xs uppercase tracking-[0.18em] text-zinc-500">{copy.sha}</p><code className="mt-2 block overflow-hidden text-ellipsis whitespace-nowrap font-mono text-[11px] text-zinc-200" title={release.sha256}>{release.sha256}</code></div>
      </> : <><h2 className="mt-4 text-2xl font-semibold text-white">{copy.notPublished}</h2><p className="mt-3 text-sm leading-7 text-zinc-300">{description}</p></>}
    </section>
  );
}

function ReleaseHistory({ title, releases, locale }: { title: string; releases: ReleaseRecord[]; locale: DownloadLocale }) {
  const copy = downloadCopy[locale];
  return <section className="download-grid rounded-[2rem] p-6 sm:p-8">
    <div className="flex items-end justify-between gap-4"><div><p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-teal">{copy.versions}</p><h2 className="mt-3 text-2xl font-semibold text-white">{title}</h2></div><p className="text-xs uppercase tracking-[0.18em] text-zinc-500">{releases.length} {copy.files}</p></div>
    <div className="mt-6 overflow-hidden rounded-[1.5rem] border border-white/8">{releases.length ? <div className="divide-y divide-white/8">{releases.map((release) => <a key={release.pathname} href={release.downloadPath} className="grid gap-2 px-5 py-4 hover:bg-white/[0.03] sm:grid-cols-[minmax(0,1fr)_90px_132px] sm:items-center"><div className="min-w-0"><p className="overflow-hidden text-ellipsis whitespace-nowrap font-medium text-white">{release.fileName}</p><p className="mt-1 overflow-hidden text-ellipsis whitespace-nowrap font-mono text-[10px] text-zinc-500">SHA-256: {release.sha256}</p></div><span className="text-sm text-zinc-300 sm:text-center">{formatReleaseSize(release.size)}</span><span className="text-sm text-zinc-400 sm:text-center">{formatReleaseDate(release.uploadedAt, copy.languageTag)}</span></a>)}</div> : <div className="px-5 py-8 text-sm text-zinc-400">{copy.noRelease}</div>}</div>
  </section>;
}

export async function DownloadHome({ locale }: { locale: DownloadLocale }) {
  const copy = downloadCopy[locale];
  const downloadsUrl = getDownloadsUrl().replace(/\/$/, "");
  const [catalog, latestStandard, latestAi] = await Promise.all([getReleaseCatalog(), getLatestRelease("standard"), getLatestRelease("ai")]);
  const standardReleases = catalog.releases.filter((release) => release.edition === "standard");
  const aiReleases = catalog.releases.filter((release) => release.edition === "ai");
  return <main className="px-4 py-8 sm:px-6 sm:py-10" lang={copy.languageTag}>
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-8">
      <header className="download-grid overflow-hidden rounded-[2rem] px-6 py-7 sm:px-8 sm:py-8"><p className="mt-6 inline-flex rounded-full border border-glitch-teal/40 bg-glitch-teal/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-glitch-teal">{copy.official}</p><h1 className="mt-5 text-4xl font-semibold text-white sm:text-5xl">{copy.title}<span className="block">{copy.subtitle}</span></h1><p className="mt-4 max-w-2xl text-base leading-8 text-zinc-300">{copy.intro}</p><div className="mt-8 grid gap-4 lg:grid-cols-2"><LatestLinkCard latestUrl={`${downloadsUrl}/latest`} label={copy.standardLatest} note={copy.standardLatestNote} copiedLabel={copy.copied} /><LatestLinkCard latestUrl={`${downloadsUrl}/latest/ai`} label={copy.aiLatest} note={copy.aiLatestNote} copiedLabel={copy.copied} /></div></header>
      {catalog.error ? <section className="download-grid rounded-[2rem] p-6 text-sm text-glitch-orange">{copy.catalogUnavailable}: {catalog.error}</section> : null}
      <div className="grid gap-8 lg:grid-cols-2"><ReleaseCard release={latestStandard} locale={locale} /><ReleaseCard release={latestAi} locale={locale} ai /></div>
      <div className="grid gap-8 lg:grid-cols-2"><ReleaseHistory title={copy.standardHistory} releases={standardReleases} locale={locale} /><ReleaseHistory title={copy.aiHistory} releases={aiReleases} locale={locale} /></div>
      <div className="grid gap-8 lg:grid-cols-2">
        <section className="download-grid rounded-[2rem] p-6"><p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-orange">{copy.standardInstall}</p><h2 className="mt-3 text-2xl font-semibold text-white">{copy.importTitle}</h2><ol className="mt-4 space-y-3 text-sm leading-7 text-zinc-300">{copy.installSteps.map((step, index) => <li key={step}>{index + 1}. {step}</li>)}</ol></section>
        <section className="download-grid rounded-[2rem] p-6"><p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-teal">{copy.aiInstall}</p><h2 className="mt-3 text-2xl font-semibold text-white">{copy.profileTitle}</h2><p className="mt-4 text-sm leading-7 text-zinc-300">{copy.profileDescription}</p><a href={hermesProfileUrl} className="mt-5 inline-flex h-11 items-center rounded-full border border-glitch-teal px-5 text-sm text-glitch-teal hover:bg-glitch-teal/10">{copy.profileLink}</a></section>
      </div>
      <section className="download-grid rounded-[2rem] p-6"><p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-teal">{copy.needHelp}</p><div className="mt-4 flex flex-col gap-3 sm:flex-row"><a href={getWebsiteUrl()} className="inline-flex h-11 items-center justify-center rounded-full border border-white/10 px-5 text-sm text-zinc-200">{copy.website}</a><a href={localizedDocsUrl(locale, "installation-guide-troubleshooting")} className="inline-flex h-11 items-center justify-center rounded-full border border-white/10 px-5 text-sm text-zinc-200">{copy.installationGuide}</a><a href={localizedDocsUrl(locale)} className="inline-flex h-11 items-center justify-center rounded-full border border-white/10 px-5 text-sm text-zinc-200">{copy.documentation}</a></div></section>
    </div>
  </main>;
}
