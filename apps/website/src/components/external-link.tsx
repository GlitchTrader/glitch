import type { ComponentPropsWithoutRef } from "react";

type ExternalLinkProps = ComponentPropsWithoutRef<"a"> & {
  href: string;
};

export function ExternalLink({ href, rel, target = "_blank", ...props }: ExternalLinkProps) {
  const resolvedRel = target === "_blank" ? rel ?? "noopener noreferrer" : rel;

  return <a href={href} target={target} rel={resolvedRel} {...props} />;
}
