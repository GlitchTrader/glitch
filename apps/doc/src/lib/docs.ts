import fs from "node:fs";
import path from "node:path";
import { cache } from "react";

const DOCS_ROOT = path.resolve(process.cwd(), "../../ninjatrader/Glitch/Docs");

type DocDefinition = {
  slug: string;
  fileName: string;
  navTitle: string;
  section: "Product" | "Reference";
  summary: string;
  spotlight: string;
};

const publicDocDefinitions: DocDefinition[] = [
  {
    slug: "installation-guide-troubleshooting",
    fileName: "installation-guide-troubleshooting.md",
    navTitle: "Installation Guide & Troubleshooting",
    section: "Product",
    summary:
      "Step-by-step install, license activation, bridge indicator setup, replication workflow, risk controls, and troubleshooting FAQ.",
    spotlight:
      "Best operational walkthrough for getting live quickly while validating account mapping, risk settings, and chart-linked context.",
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
