"use client";

import { useEffect, useRef, useState } from "react";

type LatestLinkCardProps = {
  latestUrl: string;
};

function fallbackCopyToClipboard(value: string): boolean {
  const input = document.createElement("textarea");
  input.value = value;
  input.setAttribute("readonly", "true");
  input.style.position = "fixed";
  input.style.opacity = "0";
  document.body.appendChild(input);
  input.select();

  let copied = false;
  try {
    copied = document.execCommand("copy");
  } catch {
    copied = false;
  }

  document.body.removeChild(input);
  return copied;
}

export function LatestLinkCard({ latestUrl }: LatestLinkCardProps) {
  const [copied, setCopied] = useState(false);
  const timeoutRef = useRef<number | null>(null);

  useEffect(
    () => () => {
      if (timeoutRef.current !== null) {
        window.clearTimeout(timeoutRef.current);
      }
    },
    [],
  );

  const handleCopyClick = async () => {
    let success = false;

    try {
      await navigator.clipboard.writeText(latestUrl);
      success = true;
    } catch {
      success = fallbackCopyToClipboard(latestUrl);
    }

    if (!success) {
      return;
    }

    setCopied(true);
    if (timeoutRef.current !== null) {
      window.clearTimeout(timeoutRef.current);
    }

    timeoutRef.current = window.setTimeout(() => {
      setCopied(false);
    }, 3000);
  };

  return (
    <div className="flex flex-col gap-3 rounded-[1.75rem] border border-white/8 bg-black/25 p-5 text-sm text-zinc-300 sm:min-w-[320px]">
      <p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-orange">Latest Link</p>
      <button
        type="button"
        onClick={handleCopyClick}
        className="relative flex h-[46px] w-full cursor-pointer items-center rounded-2xl border border-white/8 bg-black/30 px-3 font-mono text-[11px] leading-4 text-white transition-colors hover:bg-black/40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-glitch-teal"
        aria-live="polite"
      >
        <span
          className={`block w-full overflow-hidden text-ellipsis whitespace-nowrap text-left ${copied ? "invisible" : ""}`}
        >
          {latestUrl}
        </span>
        <span
          className={`pointer-events-none absolute inset-0 flex items-center justify-center text-glitch-teal transition-opacity ${copied ? "opacity-100" : "opacity-0"}`}
        >
          Copied!
        </span>
      </button>
      <p className="text-xs text-zinc-400">Bookmark this URL for newest release.</p>
    </div>
  );
}
