import { DocsHomeContent } from "@/components/docs-home-content";
import { docsLocaleDetails, getDocLanguages, getDocsHref } from "@/lib/docs-locales";
import { buildDocsPageMetadata } from "@/lib/metadata";

export const metadata = buildDocsPageMetadata({ title: docsLocaleDetails.zh.ui.shellTitle, description: docsLocaleDetails.zh.ui.shellDescription, canonical: getDocsHref("zh"), languages: getDocLanguages(), locale: "zh" });
export default function Page() { return <DocsHomeContent locale="zh" />; }
