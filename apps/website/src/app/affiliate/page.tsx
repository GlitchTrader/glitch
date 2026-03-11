import Link from "next/link";
import { SiteFooter } from "@/components/site-footer";

export const metadata = {
  title: "Glitch Affiliate Program - Creator and Partner Commissions",
  description: "Join the Glitch affiliate program: 20% commission, promo tiers, attribution rules, and payout governance.",
};

export default function AffiliatePage() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 sm:py-24">
          <p className="inline-flex rounded-full border border-glitch-teal/40 bg-glitch-teal/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.12em] text-glitch-teal">
            Glitch Affiliate Program
          </p>
          <h1 className="mt-4 text-3xl font-bold tracking-tight sm:text-4xl">Promote a serious product. Earn serious commission.</h1>
          <p className="mt-4 max-w-3xl text-zinc-600 dark:text-zinc-400">
            If your audience cares about risk discipline, prop firm compliance, and account longevity, this program was
            designed for you.
          </p>

          <div className="mt-10 grid gap-8 md:grid-cols-2">
            <div className="rounded-2xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h2 className="text-xl font-semibold">Commission model</h2>
              <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
                <li>20% of first-year net paid revenue</li>
                <li>Attribution based on promo code or last valid affiliate click</li>
                <li>No commission on fully discounted (100%) orders</li>
                <li>Refunds and chargebacks reverse pending/approved commissions</li>
              </ul>
            </div>

            <div className="rounded-2xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h2 className="text-xl font-semibold">Promo tiers</h2>
              <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
                <li>Public promo: up to 20% off</li>
                <li>Selected creators: up to 50% off</li>
                <li>Invite-only campaigns: up to 100% (strictly controlled)</li>
              </ul>
            </div>
          </div>

          <div className="mt-8 rounded-2xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="text-xl font-semibold">Program rules (non-negotiable)</h2>
            <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
              <li>No stacking promos</li>
              <li>No self-referrals</li>
              <li>Unique code + UTM required for clean attribution</li>
              <li>No fake traffic, cookie stuffing, or misleading claims</li>
            </ul>
            <p className="mt-4 text-sm text-zinc-500 dark:text-zinc-400">
              Violations may lead to immediate disqualification, payout reversal, and permanent removal.
            </p>
          </div>

          <div className="glitch-cta-row mt-10">
            <a
              href="#apply"
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white hover:opacity-90"
            >
              Apply to become an affiliate
            </a>
            <Link
              href="/pricing"
              className="inline-flex h-12 items-center justify-center rounded-full border border-zinc-300 px-6 font-medium text-zinc-700 hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-900"
            >
              Review pricing plans
            </Link>
          </div>
          <p className="mt-6 text-sm text-zinc-500 dark:text-zinc-400">
            Application form is being finalized. For early access, contact us with your channel, audience profile, and
            promotion style.
          </p>
          <p className="mt-4 text-sm text-zinc-500 dark:text-zinc-400">
            Participation in the affiliate program is subject to our{" "}
            <Link href="/terms" className="text-glitch-teal hover:underline">
              Terms of Service
            </Link>
            . Commission terms, payout timing, and attribution are at our discretion and may change; see Terms for
            limitations of liability and other legal terms.
          </p>
        </div>
      </section>
      <SiteFooter />
    </div>
  );
}
