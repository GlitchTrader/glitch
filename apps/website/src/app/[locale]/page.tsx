import Image from "next/image";
import { getTranslations } from "next-intl/server";
import { CompatibilityStrip } from "@/components/compatibility-strip";
import { CheckList } from "@/components/check-list";
import { CoreCtas } from "@/components/core-ctas";
import { ExternalLink } from "@/components/external-link";
import { FaqList } from "@/components/faq-list";
import { HeroScreenshotsCarousel } from "@/components/hero-screenshots-carousel";
import { JsonLd } from "@/components/json-ld";
import { PricingCards } from "@/components/pricing-cards";
import { SiteFooter } from "@/components/site-footer";
import { getMarketingContent } from "@/lib/localized-marketing";
import { marketingLinks } from "@/lib/marketing-links";
import { getPricingContent } from "@/lib/pricing";
import { getUiContent } from "@/lib/localized-ui";
import { buildPageMetadata } from "@/lib/seo";
import { absoluteUrl } from "@/lib/site";

type Props = { params: Promise<{ locale: string }> };

export async function generateMetadata({ params }: Props) {
  const { locale } = await params;
  const ui = getUiContent(locale);
  return buildPageMetadata({
    title: ui.site.homeMetadataTitle,
    description: ui.site.homeMetadataDescription,
    path: `/${locale}`,
    locale,
  });
}

export default async function Home({ params }: Props) {
  const { locale } = await params;
  const t = await getTranslations("home");
  const marketingContent = getMarketingContent(locale);
  const pricingContent = getPricingContent(locale);
  const ui = getUiContent(locale);

  const softwareApplicationJsonLd = {
    "@context": "https://schema.org",
    "@type": "SoftwareApplication",
    name: "Glitch NinjaTrader AddOn",
    applicationCategory: "FinanceApplication",
    operatingSystem: "Windows",
    offers: [
      {
        "@type": "Offer",
        price: "0",
        priceCurrency: "USD",
        url: marketingLinks.freeAccessUrl,
        category: "Free access",
      },
      {
        "@type": "Offer",
        price: "95",
        priceCurrency: "USD",
        url: marketingLinks.goProCheckoutUrl,
        category: "Monthly access",
      },
    ],
    description: ui.site.softwareApplicationDescription,
    brand: {
      "@type": "Brand",
      name: "Glitch",
    },
    image: absoluteUrl("/images/Glitch%20Banner.png"),
    url: absoluteUrl("/"),
  };
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <JsonLd
        data={[
          softwareApplicationJsonLd,
          {
            "@context": "https://schema.org",
            "@type": "FAQPage",
            mainEntity: marketingContent.home.faqItems.map((item) => ({
              "@type": "Question",
              name: item.question,
              acceptedAnswer: {
                "@type": "Answer",
                text: item.answer,
              },
            })),
          },
        ]}
      />

      <section aria-label="Glitch banner">
        <Image
          src="/images/Glitch Banner 4-1 .jpg"
          alt="Glitch trading assistant for NinjaTrader prop traders"
          width={3438}
          height={860}
          priority
          className="h-auto w-full"
          sizes="100vw"
        />
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 pb-0 pt-12 sm:px-6 sm:pt-24">
          <p className="inline-flex rounded-full border border-glitch-teal/40 bg-glitch-teal/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.12em] text-glitch-teal">
            {t("badge")}
          </p>
          <h1 className="mt-4 max-w-4xl text-4xl font-bold tracking-tight sm:text-5xl xl:text-6xl">
            {t("title")}
          </h1>
          <p className="mt-6 max-w-3xl text-lg text-zinc-600 dark:text-zinc-400">
            {t("lead")}
          </p>
          <p className="mt-3 max-w-3xl text-zinc-600 dark:text-zinc-400">
            {t("sublead")}
          </p>
          <div className="mt-5 flex flex-wrap gap-2 text-xs font-medium uppercase tracking-wide text-zinc-500 dark:text-zinc-400">
            {marketingContent.home.featurePills.map((pill) => (
              <span key={pill} className="rounded-full border border-zinc-200 px-3 py-1 dark:border-zinc-700">
                {pill}
              </span>
            ))}
          </div>
          <CoreCtas className="mt-10" />
          <p className="mt-4 text-sm text-zinc-500 dark:text-zinc-400">
            {t("alreadyJoined")}{" "}
            <ExternalLink href={marketingLinks.memberHubUrl} className="font-medium text-glitch-teal hover:underline">
              {t("openMemberHub")}
            </ExternalLink>
            .
          </p>
        </div>
        <div className="mt-14 pb-14 sm:mt-16 sm:pb-24">
          <HeroScreenshotsCarousel />
        </div>
      </section>

      <CompatibilityStrip />

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 sm:py-20">
          <div className="flex flex-col gap-8 md:flex-row md:items-end md:gap-12">
            <div className="w-full md:flex-1">
              <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{t("whyFailTitle")}</h2>
              <p className="mt-4 text-zinc-600 dark:text-zinc-400">
                {t("whyFailP1")}
              </p>
              <p className="mt-4 text-zinc-600 dark:text-zinc-400">
                {t("whyFailP2")}
              </p>
            </div>
            <div className="-mb-12 ml-auto h-auto w-[200px] sm:-mb-20 sm:w-[260px] md:ml-0">
              <div className="overflow-hidden rounded-2xl">
                <Image
                  src="/images/character/idea.png"
                  alt="Glitch trading assistant illustration"
                  width={1200}
                  height={1200}
                  className="h-auto w-full object-cover"
                />
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{t("growTitle")}</h2>
          <p className="mt-3 max-w-3xl text-zinc-600 dark:text-zinc-400">
            {t("growLead")}
          </p>

          <div className="mt-8 grid gap-8 lg:grid-cols-2">
            <div id="free-tier" className="rounded-2xl border-2 border-zinc-200 bg-white p-6 sm:p-8 dark:border-zinc-700 dark:bg-zinc-900">
              <p className="text-xs font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400">{t("startFree")}</p>
              <h3 className="mt-2 text-xl font-semibold">{t("freeTitle")}</h3>
              <div className="mt-5">
                <CheckList items={pricingContent.homeFreeTierFeatures} />
              </div>
              <ExternalLink
                href={marketingLinks.freeAccessUrl}
                className="mt-6 inline-flex h-11 items-center justify-center rounded-full border border-zinc-300 px-5 text-sm font-medium text-zinc-700 hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-800"
              >
                {t("startFree")}
              </ExternalLink>
            </div>

            <div id="go-pro" className="rounded-2xl border-2 border-glitch-teal bg-glitch-teal/5 p-6 sm:p-8 dark:bg-glitch-teal/10">
              <p className="text-xs font-semibold uppercase tracking-wide text-glitch-teal">{t("goProLabel")}</p>
              <h3 className="mt-2 text-xl font-semibold">{t("goProTitle")}</h3>
              <div className="mt-5">
                <CheckList items={pricingContent.homePaidAccessFeatures} />
              </div>
              <div className="mt-6 flex flex-wrap gap-2 text-xs font-semibold uppercase tracking-wide text-zinc-600 dark:text-zinc-400">
                <span className="rounded-full border border-zinc-200 px-3 py-1 dark:border-zinc-700">$95 / month</span>
                <span className="rounded-full border border-zinc-200 px-3 py-1 dark:border-zinc-700">$995 / year</span>
                <span className="rounded-full border border-zinc-200 px-3 py-1 dark:border-zinc-700">$2,450 one time</span>
              </div>
              <p className="mt-3 text-sm text-zinc-600 dark:text-zinc-400">
                {marketingContent.home.premiumCheckoutNote}
              </p>
              <ExternalLink
                href={marketingLinks.goProCheckoutUrl}
                className="mt-5 inline-flex h-11 items-center justify-center rounded-full bg-glitch-orange px-5 text-sm font-medium text-white hover:opacity-90"
              >
                {t("goProLabel")}
              </ExternalLink>
            </div>
          </div>
        </div>
      </section>

      <section id="pricing" className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{t("pricingTitle")}</h2>
          <p className="mt-3 max-w-3xl text-zinc-600 dark:text-zinc-400">
            {t("pricingLead")}
          </p>
          <PricingCards
            className="mt-10"
            plans={pricingContent.pricingPlans}
            featureLabel={pricingContent.featureLabel}
          />
          <p className="mt-6 text-sm text-zinc-600 dark:text-zinc-400">
            {t("alreadyJoined")}{" "}
            <ExternalLink href={marketingLinks.memberHubUrl} className="font-medium text-glitch-teal hover:underline">
              {ui.actions.memberHub}
            </ExternalLink>{" "}
            {t("memberHubBlurb")}
          </p>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{t("setupTitle")}</h2>
          <div className="mt-8 grid gap-8 md:grid-cols-2">
            <div className="rounded-2xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold">{t("activationTitle")}</h3>
              <ol className="mt-4 list-inside list-decimal space-y-2 text-zinc-600 dark:text-zinc-400">
                {marketingContent.home.activationSteps.map((step) => (
                  <li key={step}>{step}</li>
                ))}
              </ol>
            </div>
            <div className="rounded-2xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold">{t("dailyTitle")}</h3>
              <ol className="mt-4 list-inside list-decimal space-y-2 text-zinc-600 dark:text-zinc-400">
                {marketingContent.home.dailySteps.map((step) => (
                  <li key={step}>{step}</li>
                ))}
              </ol>
            </div>
          </div>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{t("faqTitle")}</h2>
          <p className="mt-2 max-w-3xl text-zinc-600 dark:text-zinc-400">
            {t("faqLead")}
          </p>
          <div className="mt-6">
            <FaqList items={marketingContent.home.faqItems} />
          </div>
        </div>
      </section>

      <SiteFooter />
    </div>
  );
}
