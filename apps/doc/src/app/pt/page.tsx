import type { Metadata } from "next";
import { DocsHomeContent } from "@/components/docs-home-content";
import { docsLocaleDetails, getDocLanguages, getDocsHref } from "@/lib/docs-locales";

export const metadata: Metadata = { title: docsLocaleDetails.pt.ui.shellTitle, description: docsLocaleDetails.pt.ui.shellDescription, alternates: { canonical: getDocsHref("pt"), languages: getDocLanguages() }, openGraph: { locale: "pt_BR" } };
export default function Page() { return <DocsHomeContent locale="pt" />; }
