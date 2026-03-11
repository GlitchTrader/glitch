import Link from "next/link";
import { CheckList } from "@/components/check-list";
import { FaqList } from "@/components/faq-list";
import { PricingCards } from "@/components/pricing-cards";
import { SiteFooter } from "@/components/site-footer";
import { marketingLinks } from "@/lib/marketing-links";
import { freeTierPricingFeatures, paidAccessComparisonFeatures } from "@/lib/pricing";

export const metadata = {
  title: "Glitch Pricing - Free, Monthly or Annual, and Lifetime access",
  description:
    "Glitch pricing for prop traders: start free, choose monthly or yearly premium billing, or lock in lifetime access.",
};

const faqItems = [
  {
    question: "What is the difference between Monthly / Annual and Lifetime access?",
    answer:
      "The premium product is the same. Monthly / Annual gives you flexible billing. Lifetime access gives you the same premium stack with one payment and no recurring charge.",
  },
  {
    question: "Can I start free and upgrade later?",
    answer:
      "Yes. Start free, validate fit, and upgrade when the workflow has earned a bigger role in your operation.",
  },
  {
    question: "Do all paid plans include the full premium toolset?",
    answer:
      "Yes. Monthly, yearly, and lifetime access all unlock the same premium compliance, replication, analytics, and insight stack.",
  },
  {
    question: "Can I use my own indicators and automation stack?",
    answer:
      "Yes. Glitch is designed to sit on top of your existing setup as the risk and execution assistant layer.",
  },
  {
    question: "Is Glitch suitable for high account-count operations?",
    answer:
      "Yes. Paid access supports up to 10 masters/groups and up to 100 followers per group for serious scaling.",
  },
  {
    question: "Where do download and activation happen?",
    answer:
      "Inside Member Hub. That is where you access the latest build, onboarding steps, updates, and activation guidance after joining.",
  },
  {
    question: "Do promo codes stack?",
    answer: "No. One promo code per order.",
  },
  {
    question: "Is marketplace support coming?",
    answer:
      "Yes. The roadmap includes third-party indicators, strategies, and partner prop firm offers in a unified marketplace.",
  },
];

export default function PricingPage() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-24">
          <p className="inline-flex rounded-full border border-glitch-teal/40 bg-glitch-teal/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.12em] text-glitch-teal">
            Pricing
          </p>
          <h1 className="mt-4 text-3xl font-bold tracking-tight sm:text-4xl">Straight pricing for serious operators.</h1>
          <p className="mt-4 max-w-3xl text-zinc-600 dark:text-zinc-400">
            Three plans. Clear trade-offs. Free gets you the guardrails. Monthly / Annual gives you the full premium
            stack with flexible billing. Lifetime access gives you the same premium stack without recurring charges.
          </p>
          <PricingCards className="mt-10" useAnchors />
          <p className="mt-6 text-sm text-zinc-600 dark:text-zinc-400">
            After checkout,{" "}
            <Link href={marketingLinks.memberHubUrl} className="font-medium text-glitch-teal hover:underline">
              Member Hub
            </Link>{" "}
            is where downloads, onboarding, updates, and activation steps live.
          </p>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">What changes when you upgrade</h2>
          <p className="mt-3 max-w-3xl text-zinc-600 dark:text-zinc-400">
            The jump from Free to paid is not a cosmetic upgrade. It is where Glitch becomes a full operating layer for
            serious multi-account trading.
          </p>
          <div className="mt-8 grid gap-8 lg:grid-cols-2">
            <div className="flex h-full flex-col rounded-[1.75rem] border border-zinc-200 bg-white p-6 sm:p-8 dark:border-zinc-800 dark:bg-zinc-900/80">
              <h3 className="text-lg font-semibold">Free foundation</h3>
              <div className="mt-4">
                <CheckList items={freeTierPricingFeatures} />
              </div>
              <div className="mt-8 border-t border-zinc-200 pt-5 dark:border-zinc-800">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-zinc-500 dark:text-zinc-400">Best for</p>
                <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                  Traders validating the workflow, building discipline, and protecting accounts before scaling.
                </p>
              </div>
            </div>
            <div className="flex h-full flex-col rounded-[1.75rem] border border-glitch-teal/50 bg-glitch-teal/5 p-6 sm:p-8 dark:bg-glitch-teal/10">
              <h3 className="text-lg font-semibold">Every paid plan unlocks</h3>
              <div className="mt-4">
                <CheckList items={paidAccessComparisonFeatures} />
              </div>
              <div className="mt-8 border-t border-glitch-teal/20 pt-5">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-glitch-teal">Best for</p>
                <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                  Traders running larger account stacks, deeper analysis, and faster review loops.
                </p>
              </div>
            </div>
          </div>
          <div className="mt-8 rounded-[1.75rem] border border-zinc-200 bg-zinc-50 p-6 text-sm text-zinc-600 dark:border-zinc-800 dark:bg-zinc-900/70 dark:text-zinc-400">
            Monthly, yearly, and lifetime access all unlock the same premium stack. Only the billing model changes.
          </div>
        </div>
      </section>

      <section id="how-access-works" className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">How access works</h2>
          <div className="mt-8 grid gap-8 lg:grid-cols-2">
            <div className="rounded-[1.75rem] border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold">Upgrade flow</h3>
              <ol className="mt-4 list-inside list-decimal space-y-2 text-zinc-600 dark:text-zinc-400">
                <li>Choose Free, Monthly / Annual, or Lifetime access.</li>
                <li>Complete checkout, then open Member Hub.</li>
                <li>Download the latest build and activate your license.</li>
                <li>Launch Glitch and configure your workflow.</li>
                <li>Review your rules, replication, and daily operating setup.</li>
              </ol>
            </div>

            <div className="rounded-[1.75rem] border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold">Roadmap</h3>
              <CheckList
                className="mt-4"
                items={[
                  "Marketplace for third-party indicators",
                  "Marketplace for third-party strategies",
                  "Integrated partner prop firm offers",
                  "Expanded trading assistant automations and workflows",
                ]}
              />
            </div>
          </div>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">FAQ</h2>
          <div className="mt-6">
            <FaqList items={faqItems} />
          </div>

          <p className="mt-8 text-sm text-zinc-500 dark:text-zinc-400">
            Promo code policy: one promo per order. Attribution details are on Affiliate and Terms pages.
          </p>
        </div>
      </section>

      <SiteFooter />
    </div>
  );
}
