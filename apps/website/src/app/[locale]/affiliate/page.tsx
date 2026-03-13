import { ExternalLink } from "@/components/external-link";
import { SiteFooter } from "@/components/site-footer";
import { getMarketingContent } from "@/lib/localized-marketing";
import { marketingLinks } from "@/lib/marketing-links";
import { buildPageMetadata } from "@/lib/seo";
import { Link } from "@/i18n/navigation";

type Props = { params: Promise<{ locale: string }> };

export async function generateMetadata({ params }: Props) {
  const { locale } = await params;
  const content = getMarketingContent(locale).affiliate;
  return buildPageMetadata({
    title: content.metadataTitle,
    description: content.metadataDescription,
    path: `/${locale}/affiliate`,
    locale,
  });
}

export default async function AffiliatePage({ params }: Props) {
  const { locale } = await params;
  const content = getMarketingContent(locale).affiliate;

  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 sm:py-24">
          <p className="inline-flex rounded-full border border-glitch-teal/40 bg-glitch-teal/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.12em] text-glitch-teal">
            {content.badge}
          </p>
          <h1 className="mt-4 text-3xl font-bold tracking-tight sm:text-4xl">{content.title}</h1>
          <p className="mt-4 max-w-3xl text-zinc-600 dark:text-zinc-400">
            {content.lead}
          </p>

          <div className="mt-10 grid gap-8 md:grid-cols-2">
            <div className="rounded-2xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h2 className="text-xl font-semibold">{content.commissionTitle}</h2>
              <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
                {content.commissionBullets.map((item) => (
                  <li key={item}>{item}</li>
                ))}
              </ul>
              <p className="mt-4 text-sm text-zinc-500 dark:text-zinc-400">
                {content.commissionNote}
              </p>
            </div>

            <div className="rounded-2xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h2 className="text-xl font-semibold">{content.promoTiersTitle}</h2>
              <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
                {content.promoTiersBullets.map((item) => (
                  <li key={item}>{item}</li>
                ))}
              </ul>
            </div>
          </div>

          <div className="mt-8 rounded-2xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="text-xl font-semibold">{content.rulesTitle}</h2>
            <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
              {content.rulesBullets.map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
            <p className="mt-4 text-sm text-zinc-500 dark:text-zinc-400">
              {content.rulesNote}
            </p>
          </div>

          <div className="glitch-cta-row mt-10">
            <ExternalLink
              href={marketingLinks.affiliateDashboardUrl}
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white hover:opacity-90"
            >
              {content.dashboardCta}
            </ExternalLink>
            <ExternalLink
              href={marketingLinks.goProCheckoutUrl}
              className="inline-flex h-12 items-center justify-center rounded-full border border-zinc-300 px-6 font-medium text-zinc-700 hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-900"
            >
              {content.productPageCta}
            </ExternalLink>
          </div>
          <p className="mt-6 text-sm text-zinc-500 dark:text-zinc-400">
            {content.dashboardNote}
          </p>
          <p className="mt-4 text-sm text-zinc-500 dark:text-zinc-400">
            {content.termsLead}{" "}
            <Link href="/terms" className="text-glitch-teal hover:underline">
              {content.termsLink}
            </Link>
            . {content.termsTail}
          </p>
        </div>
      </section>
      <SiteFooter />
    </div>
  );
}
