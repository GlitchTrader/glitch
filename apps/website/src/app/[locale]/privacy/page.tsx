import { SiteFooter } from "@/components/site-footer";
import { Link } from "@/i18n/navigation";
import { getLegalContent } from "@/lib/localized-legal";
import { buildPageMetadata } from "@/lib/seo";

const LAST_UPDATED = "2026-03-13";

type Props = { params: Promise<{ locale: string }> };

export async function generateMetadata({ params }: Props) {
  const { locale } = await params;
  const content = getLegalContent(locale);
  return buildPageMetadata({
    title: content.privacy.metadataTitle,
    description: content.privacy.metadataDescription,
    path: `/${locale}/privacy`,
    locale,
  });
}

export default async function PrivacyPage({ params }: Props) {
  const { locale } = await params;
  const content = getLegalContent(locale);

  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <div className="mx-auto max-w-4xl px-4 py-12 sm:px-6 sm:py-24">
        <p className="text-sm text-zinc-500 dark:text-zinc-400">
          {content.shared.lastUpdatedLabel}: {LAST_UPDATED}
        </p>
        <h1 className="mt-2 text-3xl font-bold tracking-tight">{content.privacy.title}</h1>
        <p className="mt-6 text-zinc-600 dark:text-zinc-400">{content.privacy.intro}</p>

        <div className="mt-8 space-y-6">
          {content.privacy.sections.map((section) => (
            <section key={section.title} className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h2 className="font-semibold">{section.title}</h2>
              {section.paragraphs?.map((paragraph) => (
                <p key={paragraph} className="mt-4 text-zinc-600 dark:text-zinc-400">
                  {paragraph}
                </p>
              ))}
              {section.bullets ? (
                <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
                  {section.bullets.map((bullet) => (
                    <li key={bullet}>{bullet}</li>
                  ))}
                </ul>
              ) : null}
            </section>
          ))}
        </div>

        <p className="mt-10 text-sm text-zinc-500 dark:text-zinc-400">
          <Link href="/terms" className="text-glitch-teal hover:underline">
            {content.shared.terms}
          </Link>
          {" · "}
          <Link href="/risk-disclosure" className="text-glitch-teal hover:underline">
            {content.shared.riskDisclosure}
          </Link>
        </p>
        <p className="mt-6">
          <Link href="/" className="text-glitch-teal hover:underline">
            {content.shared.backToHome}
          </Link>
        </p>
      </div>
      <SiteFooter />
    </div>
  );
}
