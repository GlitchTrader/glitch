import { DocsHomeContent } from "@/components/docs-home-content";
import { docsLocaleDetails, getDocLanguages, getDocsHref } from "@/lib/docs-locales";
import { buildDocsPageMetadata } from "@/lib/metadata";

export const metadata = buildDocsPageMetadata({ title: docsLocaleDetails.pt.ui.shellTitle, description: docsLocaleDetails.pt.ui.shellDescription, canonical: getDocsHref("pt"), languages: getDocLanguages(), locale: "pt" });
export default function Page() { return <DocsHomeContent locale="pt" />; }
