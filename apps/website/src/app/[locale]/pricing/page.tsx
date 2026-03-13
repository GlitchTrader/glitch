import { CheckList } from "@/components/check-list";
import { ExternalLink } from "@/components/external-link";
import { FaqList } from "@/components/faq-list";
import { PricingCards } from "@/components/pricing-cards";
import { SiteFooter } from "@/components/site-footer";
import { getMarketingContent } from "@/lib/localized-marketing";
import { marketingLinks } from "@/lib/marketing-links";
import { getPricingContent } from "@/lib/pricing";
import { getUiContent } from "@/lib/localized-ui";
import { buildPageMetadata } from "@/lib/seo";

type Props = { params: Promise<{ locale: string }> };

export async function generateMetadata({ params }: Props) {
  const { locale } = await params;
  const content = getMarketingContent(locale).pricing;
  return buildPageMetadata({
    title: content.metadataTitle,
    description: content.metadataDescription,
    path: `/${locale}/pricing`,
    locale,
  });
}

export default async function PricingPage({ params }: Props) {
  const { locale } = await params;
  const content = getMarketingContent(locale).pricing;
  const pricingContent = getPricingContent(locale);
  const ui = getUiContent(locale);

  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-24">
          <p className="inline-flex rounded-full border border-glitch-teal/40 bg-glitch-teal/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.12em] text-glitch-teal">
            {content.badge}
          </p>
          <h1 className="mt-4 text-3xl font-bold tracking-tight sm:text-4xl">{content.title}</h1>
          <p className="mt-4 max-w-3xl text-zinc-600 dark:text-zinc-400">
            {content.lead}
          </p>
          <PricingCards
            className="mt-10"
            useAnchors
            plans={pricingContent.pricingPlans}
            featureLabel={pricingContent.featureLabel}
          />
          <p className="mt-6 text-sm text-zinc-600 dark:text-zinc-400">
            {content.memberHubLead}{" "}
            <ExternalLink href={marketingLinks.memberHubUrl} className="font-medium text-glitch-teal hover:underline">
              {ui.actions.memberHub}
            </ExternalLink>{" "}
            {content.memberHubBlurb}
          </p>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{content.upgradeTitle}</h2>
          <p className="mt-3 max-w-3xl text-zinc-600 dark:text-zinc-400">
            {content.upgradeLead}
          </p>
          <div className="mt-8 grid gap-8 lg:grid-cols-2">
            <div className="flex h-full flex-col rounded-[1.75rem] border border-zinc-200 bg-white p-6 sm:p-8 dark:border-zinc-800 dark:bg-zinc-900/80">
              <h3 className="text-lg font-semibold">{content.freeFoundationTitle}</h3>
              <div className="mt-4">
                <CheckList items={pricingContent.freeTierPricingFeatures} />
              </div>
              <div className="mt-8 border-t border-zinc-200 pt-5 dark:border-zinc-800">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-zinc-500 dark:text-zinc-400">{content.freeBestForLabel}</p>
                <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                  {content.freeBestForBody}
                </p>
              </div>
            </div>
            <div className="flex h-full flex-col rounded-[1.75rem] border border-glitch-teal/50 bg-glitch-teal/5 p-6 sm:p-8 dark:bg-glitch-teal/10">
              <h3 className="text-lg font-semibold">{content.paidUnlocksTitle}</h3>
              <div className="mt-4">
                <CheckList items={pricingContent.paidAccessComparisonFeatures} />
              </div>
              <div className="mt-8 border-t border-glitch-teal/20 pt-5">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-glitch-teal">{content.paidBestForLabel}</p>
                <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                  {content.paidBestForBody}
                </p>
              </div>
            </div>
          </div>
          <div className="mt-8 rounded-[1.75rem] border border-zinc-200 bg-zinc-50 p-6 text-sm text-zinc-600 dark:border-zinc-800 dark:bg-zinc-900/70 dark:text-zinc-400">
            {content.paidNote}
          </div>
        </div>
      </section>

      <section id="how-access-works" className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{content.accessTitle}</h2>
          <div className="mt-8 grid gap-8 lg:grid-cols-2">
            <div className="rounded-[1.75rem] border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold">{content.upgradeFlowTitle}</h3>
              <ol className="mt-4 list-inside list-decimal space-y-2 text-zinc-600 dark:text-zinc-400">
                {content.upgradeFlowSteps.map((step) => (
                  <li key={step}>{step}</li>
                ))}
              </ol>
            </div>

            <div className="rounded-[1.75rem] border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold">{content.roadmapTitle}</h3>
              <CheckList
                className="mt-4"
                items={content.roadmapItems}
              />
            </div>
          </div>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{content.faqTitle}</h2>
          <div className="mt-6">
            <FaqList items={content.faqItems} />
          </div>

          <p className="mt-8 text-sm text-zinc-500 dark:text-zinc-400">
            {content.promoCodePolicy}
          </p>
        </div>
      </section>

      <SiteFooter />
    </div>
  );
}
