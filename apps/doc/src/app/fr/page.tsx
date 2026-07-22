import type { Metadata } from "next";
import { DocsHomeContent } from "@/components/docs-home-content";
import { docsLocaleDetails, getDocLanguages, getDocsHref } from "@/lib/docs-locales";

export const metadata: Metadata = { title: docsLocaleDetails.fr.ui.shellTitle, description: docsLocaleDetails.fr.ui.shellDescription, alternates: { canonical: getDocsHref("fr"), languages: getDocLanguages() }, openGraph: { locale: "fr_FR" } };
export default function Page() { return <DocsHomeContent locale="fr" />; }
