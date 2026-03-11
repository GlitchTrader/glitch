import type { ReactNode } from "react";

type CheckListProps = {
  items: ReactNode[];
  className?: string;
  itemClassName?: string;
};

export function CheckList({ items, className, itemClassName }: CheckListProps) {
  return (
    <ul className={`space-y-2 ${className ?? ""}`.trim()}>
      {items.map((item, index) => (
        <li
          key={index}
          className={`flex items-start gap-3 text-sm ${itemClassName ?? "text-zinc-700 dark:text-zinc-300"}`.trim()}
        >
          <span className="mt-0.5 inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-glitch-teal/20 text-glitch-teal">
            <svg viewBox="0 0 20 20" fill="none" aria-hidden="true" className="h-3.5 w-3.5">
              <path d="M5 10.5L8.5 14L15 7.5" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </span>
          <span>{item}</span>
        </li>
      ))}
    </ul>
  );
}
