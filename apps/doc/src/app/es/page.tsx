import type { Metadata } from "next";
import { DocsHomeContent } from "@/components/docs-home-content";
import { docsLocaleDetails, getDocLanguages, getDocsHref } from "@/lib/docs-locales";

export const metadata: Metadata = { title: docsLocaleDetails.es.ui.shellTitle, description: docsLocaleDetails.es.ui.shellDescription, alternates: { canonical: getDocsHref("es"), languages: getDocLanguages() }, openGraph: { locale: "es_ES" } };
export default function Page() { return <DocsHomeContent locale="es" />; }
