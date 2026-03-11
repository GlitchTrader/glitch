import Link from "next/link";
import { SiteFooter } from "@/components/site-footer";
import { buildPageMetadata } from "@/lib/seo";

export const metadata = buildPageMetadata({
  title: "Privacy Policy - Glitch",
  description:
    "Read how Glitch collects, uses, stores, and protects data across the website, checkout, member hub, and related services.",
  path: "/privacy",
});

const LAST_UPDATED = "2026-03-11";

export default function PrivacyPage() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <div className="mx-auto max-w-4xl px-4 py-12 sm:px-6 sm:py-24">
        <p className="text-sm text-zinc-500 dark:text-zinc-400">Last updated: {LAST_UPDATED}</p>
        <h1 className="mt-2 text-3xl font-bold tracking-tight">Privacy Policy</h1>
        <p className="mt-6 text-zinc-600 dark:text-zinc-400">
          We collect only the data needed to operate Glitch services, process access, and improve product quality. This
          policy describes what we collect, how we use it, and your rights.
        </p>

        <div className="mt-8 space-y-6">
          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">Data we may collect</h2>
            <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
              <li>Account and contact details needed for access and support</li>
              <li>Billing and subscription status from payment providers</li>
              <li>Website analytics and diagnostic usage data</li>
              <li>Support communications and troubleshooting logs</li>
              <li>License activation and product usage data necessary for entitlement and anti-abuse</li>
            </ul>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">How data is used</h2>
            <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
              <li>Provision, activation, and maintenance of product access</li>
              <li>Billing operations and fraud prevention</li>
              <li>Product reliability, support, and service improvements</li>
              <li>Operational messaging related to your account</li>
              <li>Compliance with legal and operational requirements</li>
            </ul>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">Cookies and similar technologies</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              We and our service providers may use cookies, local storage, and similar technologies to operate the
              website, remember preferences, analyze usage, and improve security. Essential cookies are necessary for
              the site to function; analytics and functional cookies help us understand how the site is used. You can
              control non-essential cookies through your browser settings. Blocking certain cookies may affect site
              functionality. We do not use cookies to sell your personal information to third parties.
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">Third parties and payment data</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              Payment details are processed by approved payment providers (e.g., Stripe, Whop). We do not store full
              payment card data on our systems; such data is handled according to the provider&apos;s terms and
              policies. We may share data with service providers (hosting, analytics, support tools) only as needed to
              operate the Services, under appropriate agreements. We do not sell your personal data.
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">Data retention</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              We retain data for as long as necessary to provide the Services, resolve disputes, enforce our terms,
              and comply with legal obligations. Account and billing-related data may be retained after account
              closure where required by law or for legitimate business purposes (e.g., tax, fraud prevention).
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">Your rights and data handling</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              We use reasonable technical and organizational safeguards to protect your data. Depending on your
              jurisdiction, you may have rights to access, correct, delete, or port your data, or to object to or
              restrict certain processing. To exercise these rights or request account data updates or deletion,
              contact us (e.g., via the contact method provided on the website or in your member hub). Requests are
              subject to verification and any legal or operational requirements we must satisfy.
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">Changes to this policy</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              We may update this Privacy Policy from time to time. The &quot;Last updated&quot; date at the top
              indicates the last revision. Continued use of the Services after changes constitutes acceptance of the
              updated policy. Material changes may be communicated via email or a notice on the website where
              appropriate.
            </p>
          </section>
        </div>

        <p className="mt-10 text-sm text-zinc-500 dark:text-zinc-400">
          For terms of use and liability, see our{" "}
          <Link href="/terms" className="text-glitch-teal hover:underline">
            Terms of Service
          </Link>
          . For trading risk, see our{" "}
          <Link href="/risk-disclosure" className="text-glitch-teal hover:underline">
            Risk Disclosure
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
