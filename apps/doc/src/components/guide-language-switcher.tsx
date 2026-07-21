import Link from "next/link";
import {
  docsLocaleDetails,
  docsLocales,
  getInstallationGuideHref,
  type DocsLocale,
} from "@/lib/docs-locales";

export function GuideLanguageSwitcher({ locale }: { locale: DocsLocale }) {
  return (
    <nav aria-label="Guide language" className="rounded-[1.5rem] border border-white/10 bg-white/[0.03] p-4">
      <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-glitch-teal">
        {docsLocaleDetails[locale].pageLabels.language}
      </p>
      <div className="mt-3 flex flex-wrap gap-2">
        {docsLocales.map((item) => {
          const active = item === locale;

          return (
            <Link
              key={item}
              href={getInstallationGuideHref(item)}
              hrefLang={docsLocaleDetails[item].languageTag}
              lang={docsLocaleDetails[item].languageTag}
              aria-current={active ? "page" : undefined}
              className={`rounded-full border px-3 py-1.5 text-sm transition ${
                active
                  ? "border-glitch-teal/60 bg-glitch-teal/15 text-white"
                  : "border-white/10 text-zinc-300 hover:border-white/25 hover:text-white"
              }`}
            >
              {docsLocaleDetails[item].label}
            </Link>
          );
        })}
      </div>
    </nav>
  );
}
