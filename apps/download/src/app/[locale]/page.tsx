import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { DownloadHome } from "@/components/download-home";
import { downloadCopy, downloadLocales, getDownloadHomeHref, isDownloadLocale } from "@/lib/download-locales";

type Props = { params: Promise<{ locale: string }> };
export const dynamic = "force-dynamic";
export const dynamicParams = false;
export function generateStaticParams() { return downloadLocales.filter((locale) => locale !== "en").map((locale) => ({ locale })); }

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { locale } = await params;
  if (!isDownloadLocale(locale) || locale === "en") return { title: "Not Found" };
  const copy = downloadCopy[locale];
  return {
    title: `${copy.title} - NinjaTrader 8`,
    description: copy.intro,
    alternates: {
      canonical: getDownloadHomeHref(locale),
      languages: Object.fromEntries(downloadLocales.map((item) => [downloadCopy[item].languageTag, getDownloadHomeHref(item)])),
    },
    openGraph: { locale: copy.languageTag.replace("-", "_") },
  };
}

export default async function LocalizedDownloadPage({ params }: Props) {
  const { locale } = await params;
  if (!isDownloadLocale(locale) || locale === "en") notFound();
  return <DownloadHome locale={locale} />;
}
