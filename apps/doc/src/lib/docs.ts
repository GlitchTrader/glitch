import fs from "node:fs";
import path from "node:path";
import { cache } from "react";
import {
  docsLocaleDetails,
  getInstallationGuideHref,
  installationGuideSlug,
  type DocsLocale,
} from "@/lib/docs-locales";

const DOCS_ROOT = path.resolve(process.cwd(), "../../ninjatrader/Glitch/Docs");

type DocDefinition = {
  slug: string;
  fileName: string;
  navTitle: string;
  section: "Product" | "Reference";
  summary: string;
  spotlight: string;
};

const localizedInstallationGuideCopy: Record<
  DocsLocale,
  Pick<DocDefinition, "navTitle" | "summary" | "spotlight">
> = {
  en: {
    navTitle: "Installation Guide & Troubleshooting",
    summary:
      "Install or upgrade Standard and Experimental AI editions, configure NinjaTrader and Hermes, preserve learning data, and troubleshoot the complete runtime.",
    spotlight:
      "Use this guide for a fresh PC, an existing Glitch installation, or an existing Hermes profile without mixing editions or losing state.",
  },
  pt: {
    navTitle: "Guia de instalação e solução de problemas",
    summary:
      "Instale ou atualize as edições Standard e AI Experimental, configure NinjaTrader e Hermes, preserve o aprendizado e solucione o runtime completo.",
    spotlight:
      "Use este guia em um PC novo, com uma instalação existente do Glitch ou com um perfil Hermes existente, sem misturar edições nem perder estado.",
  },
  es: {
    navTitle: "Guía de instalación y solución de problemas",
    summary:
      "Instala o actualiza las ediciones Standard y AI Experimental, configura NinjaTrader y Hermes, conserva el aprendizaje y diagnostica todo el runtime.",
    spotlight:
      "Usa esta guía en un PC nuevo, con una instalación existente de Glitch o con un perfil Hermes existente, sin mezclar ediciones ni perder estado.",
  },
  zh: {
    navTitle: "安装与故障排除指南",
    summary: "安装或升级 Standard 与实验性 AI 版本，配置 NinjaTrader 和 Hermes，保留学习数据，并排查完整运行链路。",
    spotlight: "适用于新电脑、已有 Glitch 安装或已有 Hermes 配置；避免混装版本或丢失状态。",
  },
  fr: {
    navTitle: "Guide d’installation et de dépannage",
    summary:
      "Installez ou mettez à niveau les éditions Standard et AI Expérimentale, configurez NinjaTrader et Hermes, préservez l’apprentissage et dépannez l’ensemble du runtime.",
    spotlight:
      "Utilisez ce guide sur un nouveau PC, avec une installation Glitch existante ou avec un profil Hermes existant, sans mélanger les éditions ni perdre l’état.",
  },
  ru: {
    navTitle: "Руководство по установке и устранению неполадок",
    summary:
      "Установите или обновите Standard и экспериментальную AI-редакцию, настройте NinjaTrader и Hermes, сохраните данные обучения и проверьте весь runtime.",
    spotlight:
      "Руководство подходит для нового ПК, существующей установки Glitch или профиля Hermes и помогает не смешать редакции и не потерять состояние.",
  },
};

const publicDocDefinitions: DocDefinition[] = [
  {
    slug: installationGuideSlug,
    fileName: "installation-guide-troubleshooting.md",
    navTitle: localizedInstallationGuideCopy.en.navTitle,
    section: "Product",
    summary: localizedInstallationGuideCopy.en.summary,
    spotlight: localizedInstallationGuideCopy.en.spotlight,
  },
  {
    slug: "architecture",
    fileName: "architecture.md",
    navTitle: "Architecture",
    section: "Product",
    summary: "See how the AddOn and GlitchAnalyticsBridge fit together across UI, services, and shared data flow.",
    spotlight: "Best first read if you want the system map before diving into individual surfaces.",
  },
  {
    slug: "addon",
    fileName: "addon.md",
    navTitle: "AddOn",
    section: "Product",
    summary: "Covers the GlitchAddOn entry point, windows, Chart Trader surfaces, services, and runtime behavior.",
    spotlight: "Use this when you want the operator-facing product surface and service inventory.",
  },
  {
    slug: "indicator",
    fileName: "indicator.md",
    navTitle: "Indicator",
    section: "Product",
    summary: "Breaks down the GlitchAnalyticsBridge indicator, parameter model, signal pipeline, and bridge publishing.",
    spotlight: "Use this when you care about chart-side analytics, scoring, and multi-timeframe signal generation.",
  },
  {
    slug: "data-flow-and-bridge",
    fileName: "data-flow-and-bridge.md",
    navTitle: "Data Flow",
    section: "Product",
    summary: "Follows readings from the indicator into the AddOn feed bus, state model, and compatibility bridge.",
    spotlight: "Best page for understanding how analytics move from charts into the main Glitch UI.",
  },
  {
    slug: "persistence",
    fileName: "persistence.md",
    navTitle: "Persistence",
    section: "Product",
    summary: "Documents GlitchData paths, record types, runtime policy storage, and fallback behavior.",
    spotlight: "Use this for state files, machine persistence, and operational data location rules.",
  },
  {
    slug: "api-reference",
    fileName: "api-reference.md",
    navTitle: "API Reference",
    section: "Reference",
    summary: "Reference view of key types and methods that matter across AddOn, indicator, services, and bridges.",
    spotlight: "Keep this nearby when you need exact names instead of narrative walkthroughs.",
  },
];

export type DocSummary = {
  slug: string;
  href: string;
  navTitle: string;
  title: string;
  section: DocDefinition["section"];
  summary: string;
};

export type DocPage = DocSummary & {
  content: string;
  spotlight: string;
  headings: DocHeading[];
};

export type DocHeading = {
  id: string;
  text: string;
  level: 2 | 3;
};

type DocNavigationSection = {
  title: string;
  items: Array<{
    href: string;
    label: string;
    slug: string | null;
  }>;
};

function readDocFile(fileName: string): string {
  return fs.readFileSync(path.join(DOCS_ROOT, fileName), "utf8");
}

function stripMarkdown(value: string): string {
  return value
    .replace(/`([^`]+)`/g, "$1")
    .replace(/\[([^\]]+)\]\(([^)]+)\)/g, "$1")
    .replace(/[*_~>#-]/g, "")
    .replace(/\|/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function getHeading(markdown: string, fallback: string): string {
  const match = markdown.match(/^#\s+(.+)$/m);
  return match ? stripMarkdown(match[1]) : fallback;
}

export const getDocsLead = cache(() => {
  return {
    intro:
      "Glitch docs explain how the live NinjaTrader AddOn, bridge indicator, persistence layer, and operator workflows fit together in one risk-first trading system.",
    secondary:
      "Use these pages to understand the real product surface: how Glitch publishes analytics, enforces operational discipline, stores state, and exposes stable contracts across the stack.",
  };
});

function slugifyHeading(value: string): string {
  return stripMarkdown(value)
    .toLowerCase()
    .replace(/['".,()]/g, "")
    .replace(/[^a-z0-9\u00C0-\u024F\u0400-\u04FF\u4E00-\u9FFF\s-]/g, "")
    .trim()
    .replace(/\s+/g, "-");
}

function getDocHeadings(markdown: string): DocHeading[] {
  return markdown
    .split(/\r?\n/)
    .map((line) => {
      const match = /^(##|###)\s+(.+)$/.exec(line.trim());
      if (!match) {
        return null;
      }

      const level = match[1] === "##" ? 2 : 3;
      const text = stripMarkdown(match[2]);

      if (!text) {
        return null;
      }

      return {
        id: slugifyHeading(text),
        text,
        level,
      } satisfies DocHeading;
    })
    .filter((heading): heading is DocHeading => Boolean(heading));
}

export const getDocSummaries = cache((): DocSummary[] => {
  return publicDocDefinitions.map((definition) => {
    const markdown = readDocFile(definition.fileName);
    const title = getHeading(markdown, definition.navTitle);

    return {
      slug: definition.slug,
      href: `/${definition.slug}`,
      navTitle: definition.navTitle,
      title,
      section: definition.section,
      summary: definition.summary,
    };
  });
});

export const getDocPage = cache((slug: string): DocPage | null => {
  const definition = publicDocDefinitions.find((item) => item.slug === slug);
  if (!definition) {
    return null;
  }

  const markdown = readDocFile(definition.fileName);
  const title = getHeading(markdown, definition.navTitle);

  return {
    slug: definition.slug,
    href: `/${definition.slug}`,
    navTitle: definition.navTitle,
    title,
    section: definition.section,
    summary: definition.summary,
    spotlight: definition.spotlight,
    content: markdown,
    headings: getDocHeadings(markdown),
  };
});

export const getLocalizedInstallationGuide = cache((locale: DocsLocale): DocPage => {
  const definition = publicDocDefinitions.find((item) => item.slug === installationGuideSlug);
  if (!definition) {
    throw new Error("The installation guide definition is missing.");
  }

  const localeDetails = docsLocaleDetails[locale];
  const copy = localizedInstallationGuideCopy[locale];
  const fileName = `installation-guide-troubleshooting${localeDetails.fileSuffix}.md`;
  const markdown = readDocFile(fileName);

  return {
    slug: installationGuideSlug,
    href: getInstallationGuideHref(locale),
    navTitle: copy.navTitle,
    title: getHeading(markdown, copy.navTitle),
    section: definition.section,
    summary: copy.summary,
    spotlight: copy.spotlight,
    content: markdown,
    headings: getDocHeadings(markdown),
  };
});

export function getDocNavigation(): DocNavigationSection[] {
  const docs = getDocSummaries();

  return [
    {
      title: "Overview",
      items: [{ href: "/", label: "Documentation Home", slug: null }],
    },
    {
      title: "Product",
      items: docs
        .filter((item) => item.section === "Product")
        .map((item) => ({ href: item.href, label: item.navTitle, slug: item.slug })),
    },
    {
      title: "Reference",
      items: docs
        .filter((item) => item.section === "Reference")
        .map((item) => ({ href: item.href, label: item.navTitle, slug: item.slug })),
    },
  ];
}

export function getAdjacentDocs(slug: string) {
  const docs = getDocSummaries();
  const currentIndex = docs.findIndex((item) => item.slug === slug);

  return {
    previous: currentIndex > 0 ? docs[currentIndex - 1] : null,
    next: currentIndex >= 0 && currentIndex < docs.length - 1 ? docs[currentIndex + 1] : null,
  };
}

export function resolveMarkdownHref(href?: string): string | null {
  if (!href) {
    return null;
  }

  if (href.startsWith("#")) {
    return href;
  }

  if (/^(https?:)?\/\//i.test(href) || href.startsWith("mailto:")) {
    return href;
  }

  const [pathname, hash] = href.split("#");

  if (!pathname.endsWith(".md")) {
    return href;
  }

  if (pathname === "README.md") {
    return hash ? `/#${hash}` : "/";
  }

  const slug = pathname.replace(/\.md$/i, "");
  const doc = publicDocDefinitions.find((item) => item.slug === slug);
  if (!doc) {
    return null;
  }

  return hash ? `/${slug}#${hash}` : `/${slug}`;
}
