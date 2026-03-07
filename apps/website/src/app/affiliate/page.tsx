import Link from "next/link";

export const metadata = {
  title: "Affiliate — Glitch",
  description: "Glitch affiliate program: 20% commission, promo tiers, payout and attribution policy.",
};

export default function AffiliatePage() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 sm:py-24">
          <h1 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Glitch Affiliate Program
          </h1>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            Creators and promoters: earn commission on paid Glitch subscriptions
            and run promos within clear rules.
          </p>

          <h2 className="mt-12 text-xl font-bold">Commission</h2>
          <p className="mt-2 text-zinc-600 dark:text-zinc-400">
            20% of net paid revenue (first year; term length defined in legal
            page). Payout timing and attribution window in your affiliate
            dashboard or via our team.
          </p>

          <h2 className="mt-10 text-xl font-bold">Promo tiers</h2>
          <ul className="mt-4 space-y-2 text-zinc-600 dark:text-zinc-400">
            <li><strong className="text-zinc-900 dark:text-zinc-100">Public promo:</strong> up to 20% off</li>
            <li><strong className="text-zinc-900 dark:text-zinc-100">Selected creators:</strong> up to 50% off</li>
            <li><strong className="text-zinc-900 dark:text-zinc-100">Invite-only campaigns:</strong> up to 100% (tightly controlled)</li>
          </ul>

          <h2 className="mt-10 text-xl font-bold">Rules</h2>
          <ul className="mt-4 list-inside list-disc space-y-1 text-zinc-600 dark:text-zinc-400">
            <li>No stacking promos</li>
            <li>No self-referrals</li>
            <li>No commissions on 100% discount orders</li>
            <li>Unique code + UTM required for attribution</li>
          </ul>
          <p className="mt-4 text-sm text-zinc-500 dark:text-zinc-400">
            Abuse (fake traffic, cookie stuffing, etc.) can result in
            disqualification and clawback. Details in affiliate terms.
          </p>

          <div className="mt-12">
            <a
              href="#apply"
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white hover:opacity-90"
            >
              Apply to become an affiliate
            </a>
          </div>
          <p className="mt-6 text-sm text-zinc-500 dark:text-zinc-400">
            Application form coming soon. For now, contact us with your
            channel/platform and audience to get on the list.
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
