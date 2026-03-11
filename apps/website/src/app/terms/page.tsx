import Link from "next/link";
import { SiteFooter } from "@/components/site-footer";

export const metadata = {
  title: "Terms of Service - Glitch",
  description: "Glitch terms of service, billing policy, acceptable use, and liability disclaimers.",
};

const LAST_UPDATED = "2026-03-11";

export default function TermsPage() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <div className="mx-auto max-w-4xl px-4 py-12 sm:px-6 sm:py-24">
        <p className="text-sm text-zinc-500 dark:text-zinc-400">Last updated: {LAST_UPDATED}</p>
        <h1 className="mt-2 text-3xl font-bold tracking-tight">Terms of Service</h1>
        <p className="mt-6 text-zinc-600 dark:text-zinc-400">
          These terms govern your use of Glitch products, software, downloads, websites, APIs, and related services
          (&quot;Services&quot;). By accessing or using Glitch, you agree to these terms. If you do not agree, do not use
          the Services.
        </p>

        <div className="mt-8 space-y-6">
          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">1. Software and services &quot;as is&quot;</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              Glitch software and Services are provided <strong>strictly &quot;as is&quot;</strong> and
              &quot;as available&quot; without warranties of any kind, whether express, implied, or statutory. We
              expressly disclaim all warranties, including but not limited to implied warranties of merchantability,
              fitness for a particular purpose, title, non-infringement, and any warranty arising from course of
              dealing or usage of trade. We do not warrant that the Services will be uninterrupted, error-free, secure,
              or free of defects, or that any errors will be corrected.
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">2. No responsibility for losses or outcomes</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              We take <strong>no responsibility</strong> for any trading losses, missed trades, failed executions,
              disconnections, data delays, software bugs, API or platform outages, third-party service failures, or any
              other losses or damages—whether within or beyond our control. You use the Services entirely at your own
              risk. Trading involves substantial risk of loss; futures and leveraged trading can result in losses
              exceeding your initial investment. You are solely responsible for your trading decisions, risk management,
              and compliance with your broker and any proprietary trading firm (&quot;prop firm&quot;) rules. Do your
              own research (DYOR). Past or hypothetical performance does not guarantee future results.
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">3. Limitation of liability</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              To the maximum extent permitted by applicable law, Glitch, its owners, affiliates, employees, and
              licensors shall not be liable for any direct, indirect, incidental, consequential, special, punitive, or
              exemplary damages—including but not limited to loss of profits, data, goodwill, or trading losses—arising
              from or in connection with your use or inability to use the Services, even if we have been advised of the
              possibility of such damages. In no event shall our aggregate liability exceed the amount you actually
              paid us for the Services in the twelve (12) months preceding the claim. Some jurisdictions do not allow
              exclusion of implied warranties or limitation of liability; in such jurisdictions our liability shall be
              limited to the greatest extent permitted by law.
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">4. Indemnification</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              You agree to indemnify, defend, and hold harmless Glitch, its owners, affiliates, employees, and
              licensors from and against any and all claims, damages, losses, costs, and expenses (including reasonable
              attorneys&apos; fees) arising from or related to your use of the Services, your trading activity, your
              violation of these terms, your violation of any third-party rights, or any dispute between you and a prop
              firm, broker, or other third party in connection with your use of Glitch.
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">5. Prop firms and third-party rules</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              Glitch is designed to support risk and execution workflows that may be used in conjunction with
              proprietary trading firms and funded programs. We are not affiliated with any prop firm. We strive to
              keep our software aligned with common prop-firm rule models, but we do not guarantee that Glitch
              complies with any specific firm&apos;s current or future rules. Rules change; we may miss an update or
              inadvertently break a rule. <strong>You are solely responsible</strong> for verifying that your use of
              Glitch complies with your prop firm&apos;s terms, rules, and updates. We are not responsible for
              disqualification, funding revocation, clawbacks, or any other consequences resulting from your use of
              Glitch in connection with a prop firm account.
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">6. No financial or legal advice</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              Nothing in the Services constitutes financial, investment, tax, or legal advice. We do not recommend any
              securities, strategies, or trading decisions. You should conduct your own research and, if appropriate,
              consult qualified professionals before making any trading or investment decisions.
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">7. Billing and plan terms</h2>
            <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
              <li>Plans may include free, monthly, annual, and lifetime access options.</li>
              <li>Subscription billing cadence and renewal terms are shown at checkout.</li>
              <li>Cancellation and refund policy terms apply as disclosed at purchase time.</li>
              <li>Promo code rules: one code per order, no stacking.</li>
            </ul>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">8. Acceptable use</h2>
            <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
              <li>No abuse of licensing, activation, or distribution mechanisms.</li>
              <li>No reverse engineering beyond applicable legal allowances.</li>
              <li>No fraudulent behavior related to promos, referrals, or affiliates.</li>
              <li>Users remain responsible for all account activity and trading actions.</li>
            </ul>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">9. Changes to terms and services</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              We may update these terms and the Services from time to time. We will use reasonable efforts to keep
              legal pages and product behavior updated and comprehensive, but we may omit an update or introduce errors.
              Continued use of the Services after changes constitutes acceptance of the revised terms. The &quot;Last
              updated&quot; date at the top of this page indicates the last revision.
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">10. Dispute resolution</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              Except where prohibited by law, any dispute arising from or relating to these terms or the Services shall
              be resolved by binding arbitration in accordance with the rules of the American Arbitration Association,
              and judgment on the award may be entered in any court of competent jurisdiction. You waive any right to
              participate in a class action or class-wide arbitration. This section does not prevent either party from
              seeking injunctive or other equitable relief in court for violations of intellectual property or
              confidentiality.
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">11. General</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              These terms constitute the entire agreement between you and Glitch regarding the Services. If any provision
              is held unenforceable, the remaining provisions remain in effect. Our failure to enforce any right does
              not waive that right. You may not assign these terms without our consent. These terms are governed by
              the laws of the United States and the state of our principal place of business, without regard to conflict
              of laws principles.
            </p>
          </section>
        </div>

        <p className="mt-10 text-sm text-zinc-500 dark:text-zinc-400">
          For trading risk disclosures, see our{" "}
          <Link href="/risk-disclosure" className="text-glitch-teal hover:underline">
            Risk Disclosure
          </Link>
          . For data practices, see our{" "}
          <Link href="/privacy" className="text-glitch-teal hover:underline">
            Privacy Policy
          </Link>
          .
        </p>
        <p className="mt-6">
          <Link href="/" className="text-glitch-teal hover:underline">
            Back to Home
          </Link>
        </p>
      </div>
      <SiteFooter />
    </div>
  );
}
