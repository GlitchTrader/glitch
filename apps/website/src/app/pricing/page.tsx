import Link from "next/link";

export const metadata = {
  title: "Pricing — Glitch",
  description: "Glitch membership: $95/mo or $995/yr. One plan, full access. Cancel anytime.",
};

export default function PricingPage() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-24">
          <h1 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Pricing
          </h1>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            One membership. Full access to the Glitch AddOn, analytics module,
            macro context, firm rules, and support. Cancel anytime.
          </p>
          <div className="mt-12 grid gap-8 sm:grid-cols-2">
            <div className="rounded-2xl border-2 border-zinc-200 bg-white p-8 dark:border-zinc-700 dark:bg-zinc-900">
              <p className="text-sm font-medium text-zinc-500 dark:text-zinc-400">Monthly</p>
              <p className="mt-2 text-4xl font-bold">$95<span className="text-xl font-normal text-zinc-500">/mo</span></p>
              <p className="mt-4 text-sm text-zinc-600 dark:text-zinc-400">
                Billed monthly. Cancel anytime.
              </p>
              <Link
                href="#checkout-monthly"
                className="mt-6 inline-block w-full rounded-full bg-glitch-orange py-3 text-center font-medium text-white hover:opacity-90"
              >
                Start at $95/mo
              </Link>
            </div>
            <div className="rounded-2xl border-2 border-glitch-teal bg-glitch-teal/5 p-8 dark:bg-glitch-teal/10">
              <p className="text-sm font-medium text-glitch-teal">Best value</p>
              <p className="mt-2 text-4xl font-bold">$995<span className="text-xl font-normal text-zinc-500 dark:text-zinc-400">/yr</span></p>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Save $145/yr vs monthly. Billed annually.
              </p>
              <Link
                href="#checkout-annual"
                className="mt-6 inline-block w-full rounded-full border-2 border-glitch-teal bg-transparent py-3 text-center font-medium text-glitch-teal hover:bg-glitch-teal/20"
              >
                Get Annual ($995/yr)
              </Link>
            </div>
          </div>
          <div className="mt-12 rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">What’s included</h2>
            <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
              <li>Glitch AddOn (dashboard, compliance, replication, journal)</li>
              <li>Analytics module (multi-timeframe signal framework)</li>
              <li>Macro/Nasdaq context window</li>
              <li>Firm rules framework + ongoing updates</li>
              <li>Localization support</li>
              <li>Ongoing updates + support channel</li>
            </ul>
          </div>
          <p className="mt-8 text-sm text-zinc-500 dark:text-zinc-400">
            Promo code? Enter at checkout. One promo per order. Billing and
            cancel policy available in Terms. Affiliate attribution: see{" "}
            <Link href="/affiliate" className="text-glitch-teal hover:underline">
              Affiliate
            </Link>
            .
          </p>
        </div>
      </section>
      <footer className="border-t border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-8 sm:px-6">
          <div className="flex flex-wrap gap-6 text-sm text-zinc-500 dark:text-zinc-400">
            <Link href="/risk-disclosure" className="hover:text-zinc-700 dark:hover:text-zinc-300">Risk disclosure</Link>
            <Link href="/terms" className="hover:text-zinc-700 dark:hover:text-zinc-300">Terms</Link>
            <Link href="/privacy" className="hover:text-zinc-700 dark:hover:text-zinc-300">Privacy</Link>
          </div>
        </div>
      </footer>
    </div>
  );
}
