const globalRateLimitStoreKey = "__glitchRateLimitStoreV1";
const globalRateLimitCleanupMsKey = "__glitchRateLimitLastCleanupMsV1";
const cleanupThrottleMs = 30 * 1000;

interface RateLimitBucket {
  count: number;
  windowStartedAtMs: number;
}

export interface RateLimitResult {
  allowed: boolean;
  retryAfterSeconds: number;
}

function getStore(): Map<string, RateLimitBucket> {
  const globalScope = globalThis as typeof globalThis & {
    [globalRateLimitStoreKey]?: Map<string, RateLimitBucket>;
  };

  if (!globalScope[globalRateLimitStoreKey]) {
    globalScope[globalRateLimitStoreKey] = new Map<string, RateLimitBucket>();
  }

  return globalScope[globalRateLimitStoreKey]!;
}

function maybeCleanup(windowMs: number): void {
  const globalScope = globalThis as typeof globalThis & {
    [globalRateLimitCleanupMsKey]?: number;
  };

  const nowMs = Date.now();
  const lastCleanupMs = globalScope[globalRateLimitCleanupMsKey] ?? 0;
  if (nowMs - lastCleanupMs < cleanupThrottleMs) {
    return;
  }

  globalScope[globalRateLimitCleanupMsKey] = nowMs;
  const store = getStore();
  for (const [key, bucket] of store.entries()) {
    if (nowMs - bucket.windowStartedAtMs > Math.max(windowMs * 3, 60_000)) {
      store.delete(key);
    }
  }
}

export function checkRateLimit(
  key: string,
  maxRequests: number,
  windowMs: number,
): RateLimitResult {
  const safeMaxRequests = Math.max(1, Math.min(Math.floor(maxRequests), 10_000));
  const safeWindowMs = Math.max(1_000, Math.min(Math.floor(windowMs), 24 * 60 * 60 * 1000));

  maybeCleanup(safeWindowMs);

  const nowMs = Date.now();
  const store = getStore();
  const bucket = store.get(key);

  if (!bucket || nowMs - bucket.windowStartedAtMs >= safeWindowMs) {
    store.set(key, {
      count: 1,
      windowStartedAtMs: nowMs,
    });
    return {
      allowed: true,
      retryAfterSeconds: 0,
    };
  }

  if (bucket.count >= safeMaxRequests) {
    const retryAfterMs = Math.max(0, safeWindowMs - (nowMs - bucket.windowStartedAtMs));
    return {
      allowed: false,
      retryAfterSeconds: Math.max(1, Math.ceil(retryAfterMs / 1000)),
    };
  }

  bucket.count++;
  store.set(key, bucket);
  return {
    allowed: true,
    retryAfterSeconds: 0,
  };
}

