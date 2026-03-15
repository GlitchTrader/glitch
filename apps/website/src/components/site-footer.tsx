import Image from "next/image";
import { getTranslations } from "next-intl/server";
import { CoreCtas } from "@/components/core-ctas";
import { ExternalLink } from "@/components/external-link";
import { marketingLinks } from "@/lib/marketing-links";
import { Link } from "@/i18n/navigation";

const installationGuideUrl = "https://docs.glitchtrader.com/installation-guide-troubleshooting";
const downloadsUrl = process.env.NEXT_PUBLIC_DOWNLOADS_URL?.trim() || "https://download.glitchtrader.com";

export async function SiteFooter() {
  const t = await getTranslations("footer");
  const navT = await getTranslations("nav");
  return (
    <footer className="border-t border-zinc-200 dark:border-zinc-800">
      <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
        <div className="w-full md:max-w-2xl">
            <Image
              src="/images/branding/Glitch%20Logo.svg"
              alt="Glitch"
              width={110}
              height={30}
              className="h-7 w-auto"
              unoptimized
            />
            <h2 className="mt-5 text-lg font-semibold tracking-tight">{t("readyTitle")}</h2>
            <p className="mt-2 max-w-2xl text-sm text-zinc-600 dark:text-zinc-400">
              {t("readyDescription")}
            </p>
            <CoreCtas className="mt-6" />
            <p className="mt-4 text-sm text-zinc-500 dark:text-zinc-400">
              {t("alreadyJoined")}{" "}
              <ExternalLink href={marketingLinks.memberHubUrl} className="font-medium text-glitch-teal hover:underline">
                {t("openMemberHub")}
              </ExternalLink>
              .
            </p>
        </div>

        <div className="mt-8 flex flex-wrap items-center justify-between gap-4 border-t border-zinc-200 pt-6 text-sm text-zinc-500 dark:border-zinc-800 dark:text-zinc-400">
          <span>{t("copyright")}</span>
          <div className="flex flex-wrap gap-6">
            <ExternalLink href={downloadsUrl} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Download
            </ExternalLink>
            <ExternalLink href={installationGuideUrl} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Guide
            </ExternalLink>
            <ExternalLink href={marketingLinks.docsUrl} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {navT("docs")}
            </ExternalLink>
            <Link href="/risk-disclosure" className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Risk
            </Link>
            <Link href="/terms" className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {t("terms")}
            </Link>
            <Link href="/privacy" className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {t("privacy")}
            </Link>
          </div>
        </div>
      </div>
    </footer>
  );
}
