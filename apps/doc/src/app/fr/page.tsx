import { DocsHomeContent } from "@/components/docs-home-content";
import { docsLocaleDetails, getDocLanguages, getDocsHref } from "@/lib/docs-locales";
import { buildDocsPageMetadata } from "@/lib/metadata";

export const metadata = buildDocsPageMetadata({ title: docsLocaleDetails.fr.ui.shellTitle, description: docsLocaleDetails.fr.ui.shellDescription, canonical: getDocsHref("fr"), languages: getDocLanguages(), locale: "fr" });
export default function Page() { return <DocsHomeContent locale="fr" />; }
