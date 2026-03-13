"use client";

import Image from "next/image";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { isLocale, locales, localizePathname, type Locale } from "@/i18n/routing";
import { startTransition, useCallback, useState } from "react";

const flagSize = 20;
const flagSrcByLocale: Record<Locale, string> = {
  en: "/images/flags/square/en.svg",
  es: "/images/flags/square/es.svg",
  fr: "/images/flags/square/fr.svg",
  pt: "/images/flags/square/br.svg",
  ru: "/images/flags/square/ru.svg",
  zh: "/images/flags/square/zh.svg",
};

function Flag({ locale }: { locale: Locale }) {
  return (
    <span className="inline-flex h-5 w-5 shrink-0 overflow-hidden rounded-full border border-zinc-300/70 bg-zinc-100 dark:border-zinc-600 dark:bg-zinc-900">
      <Image
        src={flagSrcByLocale[locale]}
        alt=""
        width={flagSize}
        height={flagSize}
        className="h-full w-full object-cover"
        unoptimized
      />
    </span>
  );
}

export function LanguageSwitcher() {
  const t = useTranslations("languages");
  const intlLocale = useLocale() as Locale;
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const [open, setOpen] = useState(false);
  const pathnameLocale = pathname?.split("/").filter(Boolean)[0] ?? "";
  const currentLocale = isLocale(pathnameLocale) ? pathnameLocale : intlLocale;

  const handleSelect = useCallback(
    (newLocale: Locale) => {
      if (newLocale === currentLocale) {
        setOpen(false);
        return;
      }

      const nextPathname = localizePathname(pathname || "/", newLocale);
      const query = searchParams.toString();
      const hash = typeof window !== "undefined" ? window.location.hash : "";
      const nextHref = `${nextPathname}${query ? `?${query}` : ""}${hash}`;

      startTransition(() => {
        router.replace(nextHref, { scroll: false });
      });

      setOpen(false);
    },
    [currentLocale, pathname, router, searchParams]
  );

  return (
    <div className="relative">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="flex h-9 items-center gap-2 rounded-md border border-zinc-200 bg-transparent px-2.5 py-1.5 text-sm text-zinc-700 transition-colors hover:bg-zinc-100/70 dark:border-zinc-700 dark:bg-transparent dark:text-zinc-200 dark:hover:bg-zinc-900/70"
        aria-expanded={open}
        aria-haspopup="listbox"
        aria-label={t("label")}
      >
        <Flag locale={currentLocale} />
        <span className="hidden sm:inline">{t(currentLocale)}</span>
        <svg
          className="h-4 w-4 shrink-0 text-zinc-500"
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          aria-hidden
        >
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      {open && (
        <>
          <div
            className="fixed inset-0 z-40"
            aria-hidden
            onClick={() => setOpen(false)}
          />
          <ul
            role="listbox"
            className="absolute right-0 top-full z-50 mt-1 min-w-[180px] rounded-lg border border-zinc-200 bg-zinc-800 py-1 shadow-lg dark:border-zinc-700"
          >
            {locales.map((loc) => {
              const isSelected = loc === currentLocale;
              return (
                <li key={loc} role="option" aria-selected={isSelected}>
                  <button
                    type="button"
                    onClick={() => handleSelect(loc)}
                    className={`flex w-full items-center gap-3 px-3 py-2.5 text-left text-sm text-zinc-100 transition-colors hover:bg-zinc-700 ${
                      isSelected ? "bg-glitch-teal text-white hover:bg-glitch-teal/90" : ""
                    }`}
                  >
                    <Flag locale={loc} />
                    <span>{t(loc)}</span>
                  </button>
                </li>
              );
            })}
          </ul>
        </>
      )}
    </div>
  );
}
