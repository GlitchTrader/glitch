import { readOptionalEnv } from "@/lib/env";

const FINNHUB_BASE_URL = "https://finnhub.io/api/v1";
const FRED_BASE_URL = "https://api.stlouisfed.org/fred";
const MARKET_CACHE_KEY = "market_fundamentals_v1";
const MARKET_CACHE_TTL_SECONDS = 300;
const MARKET_CACHE_STALE_FALLBACK_SECONDS = 3600;
const REQUEST_TIMEOUT_MS = 9000;

const globalPoolKey = "__glitchMarketFundamentalsPoolV1";
const globalSchemaReadyKey = "__glitchMarketFundamentalsSchemaReadyV1";
const globalRefreshPromiseKey = "__glitchMarketFundamentalsRefreshPromiseV1";

const mag7Weights: Record<string, number> = {
  AAPL: 0.09,
  MSFT: 0.1,
  NVDA: 0.12,
  AMZN: 0.06,
  GOOGL: 0.05,
  META: 0.04,
  TSLA: 0.025,
};

const mag7Symbols = Object.keys(mag7Weights);

type DbPool = {
  query<T = unknown>(text: string, params?: unknown[]): Promise<{ rows: T[]; rowCount: number | null }>;
};

type MarketCacheRow = {
  cache_key: string;
  payload: unknown;
  updated_at: string | Date;
  expires_at: string | Date;
};

type FinnhubQuote = {
  c?: number;
  pc?: number;
  h?: number;
  l?: number;
  o?: number;
  t?: number;
};

type FinnhubCompanyNews = {
  id?: number;
  datetime?: number;
  headline?: string;
  summary?: string;
  source?: string;
  url?: string;
};

type FinnhubEarningsCalendar = {
  earningsCalendar?: Array<{
    symbol?: string;
    date?: string;
    epsEstimate?: number | null;
    epsActual?: number | null;
  }>;
};

type FredReleaseDates = {
  release_dates?: Array<{
    release_id?: string;
    release_name?: string;
    date?: string;
  }>;
};

type MarketHeadline = {
  symbol: string;
  datetime: number;
  headline: string;
  summary: string;
  source: string;
  url: string;
};

type MarketQuote = {
  current: number;
  previousClose: number;
  dayHigh: number;
  dayLow: number;
  dayOpen: number;
  timestamp: number;
};

type MarketEarnings = {
  symbol: string;
  date: string;
  epsEstimate: number | null;
  epsActual: number | null;
};

type MarketReleaseDate = {
  releaseId: string;
  releaseName: string;
  date: string;
};

type RawFundamentalPayload = {
  fetchedAtIso: string;
  quotes: Record<string, MarketQuote>;
  headlines: MarketHeadline[];
  earnings: MarketEarnings[];
  releaseDates: MarketReleaseDate[];
  status: {
    finnhub: string;
    fred: string;
  };
};

export type MarketFundamentalSnapshot = {
  generatedAtIso: string;
  scoreSectionTitle: string;
  mag7InfluenceScore: number;
  newsSentiment: string;
  earningsAnalysis: string;
  officialNews: string;
  isNewsLockoutActive: boolean;
  newsLockoutText: string;
  mag7ScoreLines: string[];
  latestHeadlineLines: string[];
  officialNewsLines: string[];
};

function toIso(input: string | Date | null | undefined): string | null {
  if (!input) {
    return null;
  }

  const parsed = input instanceof Date ? input : new Date(input);
  if (Number.isNaN(parsed.valueOf())) {
    return null;
  }

  return parsed.toISOString();
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

function normalizeInstrumentRoot(value: string | null | undefined): string {
  const raw = (value ?? "").trim().toUpperCase();
  if (!raw) {
    return "";
  }

  const withoutSpace = raw.split(" ")[0];
  const withoutDot = withoutSpace.split(".")[0];
  return withoutDot.startsWith("@") ? withoutDot.slice(1) : withoutDot;
}

function resolveScoreSectionTitle(instrumentRoot: string): string {
  if (!instrumentRoot) {
    return "Instrument Scoring";
  }

  if (instrumentRoot === "NQ" || instrumentRoot === "MNQ" || instrumentRoot === "NDX") {
    return "MAG7 Influence Score";
  }

  return "Instrument Scoring";
}

function scoreToSignalLabel(score: number): string {
  if (score <= -0.75) return "Strong Sell";
  if (score <= -0.35) return "Sell";
  if (score <= -0.1) return "Weak Sell";
  if (score < 0.1) return "Neutral";
  if (score < 0.35) return "Weak Buy";
  if (score < 0.75) return "Buy";
  return "Strong Buy";
}

function pctText(value: number): string {
  const sign = value >= 0 ? "+" : "-";
  return `${sign}${Math.abs(value).toFixed(2)}%`;
}

function safeNumber(input: unknown, fallback = 0): number {
  if (typeof input === "number" && Number.isFinite(input)) {
    return input;
  }

  if (typeof input === "string") {
    const parsed = Number.parseFloat(input);
    if (Number.isFinite(parsed)) {
      return parsed;
    }
  }

  return fallback;
}

function normalizeHeadlineText(value: string | null | undefined): string {
  return (value ?? "").replace(/\s+/g, " ").trim();
}

function sentimentFromHeadline(headline: string, summary: string): number {
  const text = `${headline} ${summary}`.toLowerCase();
  if (!text) {
    return 0;
  }

  const strongNegative = [
    "bankruptcy",
    "fraud",
    "sec charges",
    "guidance cut",
    "investigation",
    "downgrade",
  ];
  const mildNegative = ["misses estimates", "lawsuit", "profit warning", "missed estimates"];
  const strongPositive = [
    "raises guidance",
    "beats estimates",
    "record revenue",
    "upgrade",
    "buyback",
  ];
  const mildPositive = ["new contract", "partnership", "expands", "launches"];

  if (strongNegative.some((token) => text.includes(token))) return -1;
  if (strongPositive.some((token) => text.includes(token))) return 1;
  if (mildNegative.some((token) => text.includes(token))) return -0.5;
  if (mildPositive.some((token) => text.includes(token))) return 0.5;
  return 0;
}

function quoteSignal(quote: MarketQuote | null | undefined): number {
  if (!quote || quote.previousClose <= 0) {
    return 0;
  }

  const pct = (quote.current - quote.previousClose) / quote.previousClose;
  return clamp(pct / 0.02, -1, 1);
}

function buildMag7Sentiment(payload: RawFundamentalPayload) {
  let weightedTotal = 0;
  let weightSum = 0;
  const lines: string[] = [];

  for (const symbol of mag7Symbols) {
    const quote = payload.quotes[symbol];
    const symbolHeadlines = payload.headlines
      .filter((item) => item.symbol === symbol)
      .sort((a, b) => b.datetime - a.datetime)
      .slice(0, 20);

    const newsScore =
      symbolHeadlines.length === 0
        ? 0
        : symbolHeadlines.reduce((acc, item) => acc + sentimentFromHeadline(item.headline, item.summary), 0) /
          symbolHeadlines.length;

    const quoteScore = quoteSignal(quote);
    const combinedScore = clamp((quoteScore * 0.8) + (newsScore * 0.2), -1, 1);
    const weight = mag7Weights[symbol] ?? 0;

    weightedTotal += combinedScore * weight;
    weightSum += weight;

    const pct =
      quote && quote.previousClose > 0
        ? ((quote.current - quote.previousClose) / quote.previousClose) * 100
        : 0;
    lines.push(`${symbol}: ${pctText(pct)} | ${scoreToSignalLabel(combinedScore)} (${combinedScore.toFixed(2)})`);
  }

  const influence = weightSum > 0 ? clamp(weightedTotal / weightSum, -1, 1) : 0;
  return {
    influence,
    lines,
  };
}

function buildLatestHeadlineLines(payload: RawFundamentalPayload): string[] {
  if (payload.headlines.length === 0) {
    return ["Latest headlines unavailable."];
  }

  return payload.headlines
    .slice()
    .sort((a, b) => b.datetime - a.datetime)
    .slice(0, 8)
    .map((item) => {
      const ts = new Date(item.datetime * 1000);
      const iso = Number.isNaN(ts.valueOf()) ? "unknown time" : ts.toISOString().replace("T", " ").slice(0, 16);
      return `${iso} UTC | ${item.symbol} | ${item.headline}`;
    });
}

function buildOfficialNewsLines(payload: RawFundamentalPayload): string[] {
  if (payload.releaseDates.length === 0) {
    if (payload.status.fred.startsWith("missing")) {
      return ["Official News feed unavailable: server missing FRED_API_KEY."];
    }
    return ["Official News: no upcoming scheduled releases."];
  }

  return payload.releaseDates
    .slice()
    .sort((a, b) => (a.date < b.date ? -1 : a.date > b.date ? 1 : 0))
    .slice(0, 8)
    .map((item) => `${item.date} | ${item.releaseName}`);
}

function buildEarningsAnalysis(payload: RawFundamentalPayload): string {
  if (payload.earnings.length === 0) {
    if (payload.status.finnhub.startsWith("missing")) {
      return "Earnings feed unavailable: server missing FINNHUB_API_KEY.";
    }
    return "No upcoming MAG7 earnings found.";
  }

  const lines = payload.earnings
    .slice()
    .sort((a, b) => (a.date < b.date ? -1 : a.date > b.date ? 1 : 0))
    .slice(0, 5)
    .map((item) => {
      const est = item.epsEstimate == null ? "n/a" : item.epsEstimate.toFixed(2);
      const act = item.epsActual == null ? "n/a" : item.epsActual.toFixed(2);
      return `${item.symbol} ${item.date} | EPS est: ${est} | EPS act: ${act}`;
    });

  return lines.join("\n");
}

function buildOfficialNewsText(officialNewsLines: string[]): string {
  return officialNewsLines.slice(0, 3).join("\n");
}

async function fetchJson(url: string): Promise<unknown> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

  try {
    const response = await fetch(url, {
      method: "GET",
      headers: {
        Accept: "application/json",
        "User-Agent": "glitch-api/market-fundamentals",
      },
      signal: controller.signal,
    });

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }

    return await response.json();
  } finally {
    clearTimeout(timeout);
  }
}

function buildUrl(base: string, params: Record<string, string>): string {
  const url = new URL(base);
  for (const [key, value] of Object.entries(params)) {
    if (value) {
      url.searchParams.set(key, value);
    }
  }
  return url.toString();
}

async function fetchRawFundamentalPayload(): Promise<RawFundamentalPayload> {
  const finnhubApiKey = readOptionalEnv("FINNHUB_API_KEY");
  const fredApiKey = readOptionalEnv("FRED_API_KEY");
  const now = new Date();
  const fromDate = new Date(now.getTime() - (3 * 24 * 60 * 60 * 1000)).toISOString().slice(0, 10);
  const toDate = now.toISOString().slice(0, 10);
  const earningsToDate = new Date(now.getTime() + (30 * 24 * 60 * 60 * 1000)).toISOString().slice(0, 10);

  const quotes: Record<string, MarketQuote> = {};
  const headlines: MarketHeadline[] = [];
  const earnings: MarketEarnings[] = [];
  const releaseDates: MarketReleaseDate[] = [];

  let finnhubStatus = "ok";
  let fredStatus = "ok";

  if (!finnhubApiKey) {
    finnhubStatus = "missing_finnhub_api_key";
  } else {
    try {
      const quotePromises = mag7Symbols.map(async (symbol) => {
        const url = buildUrl(`${FINNHUB_BASE_URL}/quote`, {
          symbol,
          token: finnhubApiKey,
        });
        const quote = (await fetchJson(url)) as FinnhubQuote;
        quotes[symbol] = {
          current: safeNumber(quote.c),
          previousClose: safeNumber(quote.pc),
          dayHigh: safeNumber(quote.h),
          dayLow: safeNumber(quote.l),
          dayOpen: safeNumber(quote.o),
          timestamp: Math.floor(safeNumber(quote.t, Date.now() / 1000)),
        };
      });

      const newsPromises = mag7Symbols.map(async (symbol) => {
        const url = buildUrl(`${FINNHUB_BASE_URL}/company-news`, {
          symbol,
          from: fromDate,
          to: toDate,
          token: finnhubApiKey,
        });
        const payload = (await fetchJson(url)) as FinnhubCompanyNews[];
        for (const item of payload ?? []) {
          const headline = normalizeHeadlineText(item.headline);
          if (!headline) {
            continue;
          }
          headlines.push({
            symbol,
            datetime: Math.floor(safeNumber(item.datetime, 0)),
            headline,
            summary: normalizeHeadlineText(item.summary),
            source: normalizeHeadlineText(item.source) || "finnhub",
            url: normalizeHeadlineText(item.url),
          });
        }
      });

      const earningsUrl = buildUrl(`${FINNHUB_BASE_URL}/calendar/earnings`, {
        from: toDate,
        to: earningsToDate,
        token: finnhubApiKey,
      });
      const earningsPayload = (await fetchJson(earningsUrl)) as FinnhubEarningsCalendar;
      for (const row of earningsPayload.earningsCalendar ?? []) {
        const symbol = normalizeHeadlineText(row.symbol).toUpperCase();
        if (!symbol || !mag7Weights[symbol]) {
          continue;
        }
        const date = normalizeHeadlineText(row.date);
        if (!date) {
          continue;
        }
        earnings.push({
          symbol,
          date,
          epsEstimate: row.epsEstimate == null ? null : safeNumber(row.epsEstimate),
          epsActual: row.epsActual == null ? null : safeNumber(row.epsActual),
        });
      }

      await Promise.all([...quotePromises, ...newsPromises]);
    } catch (error) {
      finnhubStatus = `error:${error instanceof Error ? error.message : String(error)}`;
    }
  }

  if (!fredApiKey) {
    fredStatus = "missing_fred_api_key";
  } else {
    try {
      const fredUrl = buildUrl(`${FRED_BASE_URL}/releases/dates`, {
        api_key: fredApiKey,
        file_type: "json",
        realtime_start: toDate,
        realtime_end: earningsToDate,
        include_release_dates_with_no_data: "true",
        sort_order: "asc",
        limit: "40",
      });
      const fredPayload = (await fetchJson(fredUrl)) as FredReleaseDates;
      for (const row of fredPayload.release_dates ?? []) {
        const releaseName = normalizeHeadlineText(row.release_name);
        const date = normalizeHeadlineText(row.date);
        if (!releaseName || !date) {
          continue;
        }
        releaseDates.push({
          releaseId: normalizeHeadlineText(row.release_id),
          releaseName,
          date,
        });
      }
    } catch (error) {
      fredStatus = `error:${error instanceof Error ? error.message : String(error)}`;
    }
  }

  return {
    fetchedAtIso: new Date().toISOString(),
    quotes,
    headlines,
    earnings,
    releaseDates,
    status: {
      finnhub: finnhubStatus,
      fred: fredStatus,
    },
  };
}

function parseRawPayload(input: unknown): RawFundamentalPayload | null {
  if (!input || typeof input !== "object") {
    return null;
  }

  const record = input as Partial<RawFundamentalPayload>;
  if (!record.fetchedAtIso || !record.status || !record.quotes || !record.headlines || !record.earnings || !record.releaseDates) {
    return null;
  }

  return {
    fetchedAtIso: record.fetchedAtIso,
    status: {
      finnhub: record.status.finnhub ?? "unknown",
      fred: record.status.fred ?? "unknown",
    },
    quotes: record.quotes,
    headlines: Array.isArray(record.headlines) ? record.headlines : [],
    earnings: Array.isArray(record.earnings) ? record.earnings : [],
    releaseDates: Array.isArray(record.releaseDates) ? record.releaseDates : [],
  };
}

function readDatabaseUrl(): string | null {
  return readOptionalEnv("DATABASE_URL");
}

async function getPool(): Promise<DbPool> {
  const globalScope = globalThis as typeof globalThis & {
    [globalPoolKey]?: DbPool;
  };

  if (globalScope[globalPoolKey]) {
    return globalScope[globalPoolKey];
  }

  const connectionString = readDatabaseUrl();
  if (!connectionString) {
    throw new Error("DATABASE_URL is required.");
  }

  const pgModule = (await import("pg")) as typeof import("pg");
  const pool = new pgModule.Pool({
    connectionString,
    max: 5,
    idleTimeoutMillis: 30000,
  });

  globalScope[globalPoolKey] = pool;
  return pool;
}

async function ensureSchema(pool: DbPool): Promise<void> {
  const globalScope = globalThis as typeof globalThis & {
    [globalSchemaReadyKey]?: boolean;
  };

  if (globalScope[globalSchemaReadyKey]) {
    return;
  }

  await pool.query(`
    CREATE TABLE IF NOT EXISTS market_cache (
      cache_key TEXT PRIMARY KEY,
      payload JSONB NOT NULL,
      updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      expires_at TIMESTAMPTZ NOT NULL
    );
  `);

  await pool.query(`
    CREATE INDEX IF NOT EXISTS market_cache_expires_at_idx
      ON market_cache (expires_at);
  `);

  globalScope[globalSchemaReadyKey] = true;
}

async function readCache(pool: DbPool): Promise<{
  payload: RawFundamentalPayload | null;
  expiresAtIso: string | null;
  updatedAtIso: string | null;
} | null> {
  const result = await pool.query<MarketCacheRow>(
    `
      SELECT cache_key, payload, updated_at, expires_at
      FROM market_cache
      WHERE cache_key = $1
      LIMIT 1;
    `,
    [MARKET_CACHE_KEY],
  );

  const row = result.rows[0];
  if (!row) {
    return null;
  }

  const payload = parseRawPayload(row.payload);
  return {
    payload,
    expiresAtIso: toIso(row.expires_at),
    updatedAtIso: toIso(row.updated_at),
  };
}

async function writeCache(pool: DbPool, payload: RawFundamentalPayload): Promise<void> {
  await pool.query(
    `
      INSERT INTO market_cache (cache_key, payload, updated_at, expires_at)
      VALUES ($1, $2::jsonb, NOW(), NOW() + ($3::int * INTERVAL '1 second'))
      ON CONFLICT (cache_key)
      DO UPDATE SET
        payload = EXCLUDED.payload,
        updated_at = NOW(),
        expires_at = EXCLUDED.expires_at;
    `,
    [MARKET_CACHE_KEY, JSON.stringify(payload), MARKET_CACHE_TTL_SECONDS],
  );
}

async function refreshRawPayloadShared(pool: DbPool): Promise<RawFundamentalPayload> {
  const globalScope = globalThis as typeof globalThis & {
    [globalRefreshPromiseKey]?: Promise<RawFundamentalPayload>;
  };

  if (globalScope[globalRefreshPromiseKey]) {
    return globalScope[globalRefreshPromiseKey]!;
  }

  const promise = (async () => {
    const fresh = await fetchRawFundamentalPayload();
    await writeCache(pool, fresh);
    return fresh;
  })();

  globalScope[globalRefreshPromiseKey] = promise;
  try {
    return await promise;
  } finally {
    delete globalScope[globalRefreshPromiseKey];
  }
}

async function getRawPayload(pool: DbPool): Promise<RawFundamentalPayload> {
  const cached = await readCache(pool);
  const now = Date.now();

  if (cached?.payload && cached.expiresAtIso) {
    const expiresAtMs = new Date(cached.expiresAtIso).valueOf();
    if (Number.isFinite(expiresAtMs) && expiresAtMs > now) {
      return cached.payload;
    }
  }

  try {
    return await refreshRawPayloadShared(pool);
  } catch (refreshError) {
    if (cached?.payload && cached.updatedAtIso) {
      const updatedAtMs = new Date(cached.updatedAtIso).valueOf();
      if (Number.isFinite(updatedAtMs) && (now - updatedAtMs) <= (MARKET_CACHE_STALE_FALLBACK_SECONDS * 1000)) {
        return cached.payload;
      }
    }

    throw refreshError;
  }
}

function projectSnapshot(payload: RawFundamentalPayload, instrument: string | null | undefined): MarketFundamentalSnapshot {
  const instrumentRoot = normalizeInstrumentRoot(instrument);
  const mag7 = buildMag7Sentiment(payload);
  const latestHeadlineLines = buildLatestHeadlineLines(payload);
  const officialNewsLines = buildOfficialNewsLines(payload);
  const earningsAnalysis = buildEarningsAnalysis(payload);
  const officialNews = buildOfficialNewsText(officialNewsLines);

  return {
    generatedAtIso: new Date().toISOString(),
    scoreSectionTitle: resolveScoreSectionTitle(instrumentRoot),
    mag7InfluenceScore: mag7.influence,
    newsSentiment: `${scoreToSignalLabel(mag7.influence)} (${mag7.influence.toFixed(2)})`,
    earningsAnalysis,
    officialNews,
    isNewsLockoutActive: false,
    newsLockoutText: "-",
    mag7ScoreLines: mag7.lines,
    latestHeadlineLines,
    officialNewsLines,
  };
}

export async function getMarketFundamentalSnapshot(
  instrument: string | null | undefined,
): Promise<MarketFundamentalSnapshot> {
  const pool = await getPool();
  await ensureSchema(pool);
  const payload = await getRawPayload(pool);
  return projectSnapshot(payload, instrument);
}

