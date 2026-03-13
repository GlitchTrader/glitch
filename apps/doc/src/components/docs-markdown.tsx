import Link from "next/link";
import ReactMarkdown from "react-markdown";
import rehypeSlug from "rehype-slug";
import remarkGfm from "remark-gfm";
import { resolveMarkdownHref } from "@/lib/docs";

type DocsMarkdownProps = {
  content: string;
};

export function DocsMarkdown({ content }: DocsMarkdownProps) {
  return (
    <article className="docs-prose">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        rehypePlugins={[rehypeSlug]}
        components={{
          a({ href, children, ...props }) {
            const resolvedHref = resolveMarkdownHref(href);

            if (!resolvedHref) {
              return <span className="text-zinc-400">{children}</span>;
            }

            if (resolvedHref.startsWith("/") || resolvedHref.startsWith("#")) {
              return (
                <Link href={resolvedHref} className="font-medium text-glitch-teal transition hover:text-white">
                  {children}
                </Link>
              );
            }

            return (
              <a
                href={resolvedHref}
                className="font-medium text-glitch-teal transition hover:text-white"
                target="_blank"
                rel="noreferrer"
                {...props}
              >
                {children}
              </a>
            );
          },
          h1({ children }) {
            return <h1>{children}</h1>;
          },
          h2({ children }) {
            return <h2>{children}</h2>;
          },
          h3({ children }) {
            return <h3>{children}</h3>;
          },
          h4({ children }) {
            return <h4>{children}</h4>;
          },
          pre({ children }) {
            return <pre>{children}</pre>;
          },
          code({ className, children, ...props }) {
            const isInline = !className;

            if (isInline) {
              return (
                <code className="rounded-md border border-white/10 bg-white/5 px-1.5 py-0.5 text-[0.9em] text-zinc-100" {...props}>
                  {children}
                </code>
              );
            }

            return (
              <code className={className} {...props}>
                {children}
              </code>
            );
          },
        }}
      >
        {content}
      </ReactMarkdown>
    </article>
  );
}
