import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { JsonLd } from "@/components/json-ld";
import { SetLangAttr } from "@/components/set-lang-attr";
import { SiteHeader as SiteHeaderClient } from "@/components/site-header";
import { getUiContent } from "@/lib/localized-ui";
import { marketingLinks } from "@/lib/marketing-links";
import { absoluteUrl, resolveSiteUrl, siteDescription, siteName } from "@/lib/site";
import { routing } from "@/i18n/routing";

const socialImagePath = "/images/Glitch%20Banner.png";

export function generateStaticParams() {
  return routing.locales.map((locale) => ({ locale }));
}

type Props = { children: React.ReactNode; params: Promise<{ locale: string }> };

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { locale } = await params;
  const ui = getUiContent(locale);
  const localeMap: Record<string, string> = {
    en: "en_US",
    pt: "pt_BR",
    es: "es_ES",
    zh: "zh_CN",
    fr: "fr_FR",
    ru: "ru_RU",
  };
  const ogLocale = localeMap[locale] ?? "en_US";
  return {
    metadataBase: resolveSiteUrl(),
    applicationName: siteName,
    title: `${siteName} - Risk-First NinjaTrader AddOn for Prop Traders`,
    description: ui.site.description ?? siteDescription,
    keywords: [
      "Glitch",
      "NinjaTrader AddOn",
      "prop trading assistant",
      "trade replication",
      "prop firm compliance",
      "Glitch Score",
    ],
    alternates: {
      canonical: `/${locale}`,
    },
    openGraph: {
      siteName,
      title: `${siteName} - Risk-First NinjaTrader AddOn for Prop Traders`,
      description: ui.site.description ?? siteDescription,
      url: `/${locale}`,
      locale: ogLocale,
      images: [
        {
          url: socialImagePath,
          alt: "Glitch trading assistant banner",
        },
      ],
      type: "website",
    },
    twitter: {
      card: "summary_large_image",
      title: `${siteName} - Risk-First NinjaTrader AddOn for Prop Traders`,
      description: ui.site.description ?? siteDescription,
      images: [socialImagePath],
    },
    icons: {
      icon: "/images/Glitch%20Favicon.png",
      shortcut: "/images/Glitch%20Favicon.png",
      apple: "/images/Glitch%20Favicon.png",
    },
  };
}

async function RenderSiteHeader() {
  const navT = await getTranslations("nav");
  const languageT = await getTranslations("languages");

  return (
    <SiteHeaderClient
      labels={{
        home: navT("home"),
        product: navT("product"),
        pricing: navT("pricing"),
        affiliate: navT("affiliate"),
        docs: navT("docs"),
        memberHub: navT("memberHub"),
        goPro: navT("goPro"),
        ariaHome: navT("ariaHome"),
        languageLabel: languageT("label"),
      }}
      links={{
        docsUrl: marketingLinks.docsUrl,
        memberHubUrl: marketingLinks.memberHubUrl,
        goProCheckoutUrl: marketingLinks.goProCheckoutUrl,
      }}
    />
  );
}

const organizationJsonLd = {
  "@context": "https://schema.org",
  "@type": "Organization",
  name: siteName,
  url: absoluteUrl("/"),
  logo: absoluteUrl("/images/Glitch%20Favicon.png"),
  description: siteDescription,
};

const websiteJsonLd = {
  "@context": "https://schema.org",
  "@type": "WebSite",
  name: siteName,
  url: absoluteUrl("/"),
  description: siteDescription,
};

export default async function LocaleLayout({ children, params }: Props) {
  const { locale } = await params;
  return (
    <>
      <SetLangAttr locale={locale} />
      <JsonLd data={[organizationJsonLd, websiteJsonLd]} />
      <RenderSiteHeader />
      {children}
    </>
  );
}
