import { DocsHomeContent } from "@/components/docs-home-content";
import { docsLocaleDetails, getDocLanguages, getDocsHref } from "@/lib/docs-locales";
import { buildDocsPageMetadata } from "@/lib/metadata";

export const metadata = buildDocsPageMetadata({ title: docsLocaleDetails.ru.ui.shellTitle, description: docsLocaleDetails.ru.ui.shellDescription, canonical: getDocsHref("ru"), languages: getDocLanguages(), locale: "ru" });
export default function Page() { return <DocsHomeContent locale="ru" />; }
