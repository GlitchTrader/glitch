import { CheckList } from "@/components/check-list";
import { CoreCtas } from "@/components/core-ctas";
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
  const content = getMarketingContent(locale).product;
  return buildPageMetadata({
    title: content.metadataTitle,
    description: content.metadataDescription,
    path: `/${locale}/product`,
    locale,
  });
}

export default async function ProductPage({ params }: Props) {
  const { locale } = await params;
  const content = getMarketingContent(locale).product;
  const pricingContent = getPricingContent(locale);
  const ui = getUiContent(locale);

  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-5xl px-4 py-12 text-center sm:px-6 sm:py-24">
          <p className="inline-flex rounded-full border border-glitch-teal/40 bg-glitch-teal/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.12em] text-glitch-teal">
            {content.badge}
          </p>
          <div className="mx-auto mt-4 max-w-[640px]">
            <h1 className="text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl">
              {content.title}
            </h1>
            <p className="mt-6 text-lg text-zinc-600 dark:text-zinc-400">
              {content.lead}
            </p>
            <p className="mt-3 text-zinc-600 dark:text-zinc-400">
              {content.sublead}
            </p>
          </div>
          <CoreCtas className="mt-10" centered />
          <p className="mx-auto mt-4 max-w-[640px] text-sm text-zinc-500 dark:text-zinc-400">
            {content.alreadyJoinedLabel}{" "}
            <ExternalLink href={marketingLinks.memberHubUrl} className="font-medium text-glitch-teal hover:underline">
              {ui.actions.memberHub}
            </ExternalLink>
            .
          </p>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{content.costTitle}</h2>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            {content.costParagraphs[0]}
          </p>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            {content.costParagraphs[1]}
          </p>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{content.startTitle}</h2>
          <div className="mt-8 grid gap-8 lg:grid-cols-2">
            <div id="free-tier" className="flex h-full flex-col rounded-2xl border-2 border-zinc-200 p-6 sm:p-8 dark:border-zinc-700">
              <p className="text-xs font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400">{content.freeEyebrow}</p>
              <h3 className="mt-2 text-xl font-semibold">{content.freeTitle}</h3>
              <div className="mt-4">
                <CheckList items={content.freeHighlights} />
              </div>
              <div className="mt-8 border-t border-zinc-200 pt-5 dark:border-zinc-800">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-zinc-500 dark:text-zinc-400">{content.freeBestForLabel}</p>
                <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                  {content.freeBestForBody}
                </p>
              </div>
            </div>
            <div id="go-pro" className="flex h-full flex-col rounded-2xl border-2 border-glitch-teal bg-glitch-teal/5 p-6 sm:p-8 dark:bg-glitch-teal/10">
              <p className="text-xs font-semibold uppercase tracking-wide text-glitch-teal">{content.proEyebrow}</p>
              <h3 className="mt-2 text-xl font-semibold">{content.proTitle}</h3>
              <div className="mt-4">
                <CheckList items={content.paidHighlights} />
              </div>
              <div className="mt-8 border-t border-glitch-teal/20 pt-5">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-glitch-teal">{content.proBestForLabel}</p>
                <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                  {content.proBestForBody}
                </p>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{content.engineTitle}</h2>
          <div className="mt-8 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            {content.engineCards.map((card) => (
              <div key={card.title} className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
                <h3 className="font-semibold text-glitch-teal">{card.title}</h3>
                <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">{card.body}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{content.pricingTitle}</h2>
          <p className="mt-3 max-w-3xl text-zinc-600 dark:text-zinc-400">
            {content.pricingLead}
          </p>
          <PricingCards className="mt-10" plans={pricingContent.pricingPlans} featureLabel={pricingContent.featureLabel} />
          <p className="mt-6 text-sm text-zinc-600 dark:text-zinc-400">
            {content.alreadyJoinedLabel}{" "}
            <ExternalLink href={marketingLinks.memberHubUrl} className="font-medium text-glitch-teal hover:underline">
              {ui.actions.memberHub}
            </ExternalLink>{" "}
            {content.memberHubBlurb}
          </p>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{content.faqTitle}</h2>
          <div className="mt-6">
            <FaqList items={content.faqItems} />
          </div>
        </div>
      </section>

      <SiteFooter />
    </div>
  );
}
