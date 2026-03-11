type FaqItem = {
  question: string;
  answer: string;
};

type FaqListProps = {
  items: FaqItem[];
};

export function FaqList({ items }: FaqListProps) {
  return (
    <div className="space-y-3">
      {items.map((item) => (
        <details key={item.question} className="group rounded-xl border border-zinc-200 bg-white/60 p-4 dark:border-zinc-800 dark:bg-zinc-900/60">
          <summary className="cursor-pointer list-none font-semibold text-zinc-900 dark:text-zinc-100">
            <span className="flex items-center justify-between gap-3">
              <span>{item.question}</span>
              <span className="text-zinc-500 transition-transform group-open:rotate-45 dark:text-zinc-400">+</span>
            </span>
          </summary>
          <p className="mt-3 text-sm text-zinc-600 dark:text-zinc-400">{item.answer}</p>
        </details>
      ))}
    </div>
  );
}
