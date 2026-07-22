import { DocsHomeContent } from "@/components/docs-home-content";
import { docsLocaleDetails, getDocLanguages, getDocsHref } from "@/lib/docs-locales";
import { buildDocsPageMetadata } from "@/lib/metadata";

export const metadata = buildDocsPageMetadata({ title: docsLocaleDetails.es.ui.shellTitle, description: docsLocaleDetails.es.ui.shellDescription, canonical: getDocsHref("es"), languages: getDocLanguages(), locale: "es" });
export default function Page() { return <DocsHomeContent locale="es" />; }
