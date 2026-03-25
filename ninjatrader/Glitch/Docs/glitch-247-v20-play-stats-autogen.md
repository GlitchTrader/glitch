# Glitch 247 v20 — per-play PnL, win rate, exits
Source: `C:\Users\alan\OneDrive\Desktop\Glitch 247 Trades Adaptive v20.csv`
`Exit name` mapping: `Target` → TP1, `Target2` → TP2, `Stop` → stop (matches your strategy order names).

## Net PnL & win rate by play type
| play | n | net PnL | win rate | mean $/trade |
|------|---|---------|----------|-------------|
| **LL** | 467 | -827.70 | 43.90% | -1.77 |
| **LS** | 339 | 1,318.10 | 49.26% | 3.89 |
| **HL** | 1325 | 2,154.90 | 57.36% | 1.63 |
| **HS** | 1833 | 7,505.00 | 57.45% | 4.09 |

## Longs — all (LL + HL)
Rows: **1792**
| exit_route | count | % of rows |
|------------|-------|----------|
| tp1 | 924 | 51.56% |
| stop | 747 | 41.69% |
| daily_limit | 50 | 2.79% |
| tp2 | 31 | 1.73% |
| hard_stop_cap | 29 | 1.62% |
| session | 9 | 0.50% |
| time | 2 | 0.11% |

## Longs — LL only
Rows: **467**
| exit_route | count | % of rows |
|------------|-------|----------|
| stop | 247 | 52.89% |
| tp1 | 205 | 43.90% |
| daily_limit | 7 | 1.50% |
| hard_stop_cap | 6 | 1.28% |
| session | 2 | 0.43% |

## Longs — HL only
Rows: **1325**
| exit_route | count | % of rows |
|------------|-------|----------|
| tp1 | 719 | 54.26% |
| stop | 500 | 37.74% |
| daily_limit | 43 | 3.25% |
| tp2 | 31 | 2.34% |
| hard_stop_cap | 23 | 1.74% |
| session | 7 | 0.53% |
| time | 2 | 0.15% |

### Longs (HL) — TP2 vs not TP2 (row counts)
- **TP2 exit** (`Exit name` = Target2): **31** (2.34% of HL rows)
- **Not TP2** (stop, TP1, time, etc.): **1294** (97.66%)
- **TP1 exit** (`Target`): **719** (54.26% of HL rows)

### Longs (LL) — TP1 vs stop (LL has no TP2 in strategy)
- **TP1** (`Target`): **205** / 467
- **Stop**: **247** / 467

## Shorts — all (LS + HS)
Rows: **2172**
| exit_route | count | % of rows |
|------------|-------|----------|
| tp1 | 1137 | 52.35% |
| stop | 816 | 37.57% |
| daily_limit | 92 | 4.24% |
| tp2 | 63 | 2.90% |
| hard_stop_cap | 53 | 2.44% |
| session | 9 | 0.41% |
| time | 2 | 0.09% |

## Shorts — LS only
Rows: **339**
| exit_route | count | % of rows |
|------------|-------|----------|
| tp1 | 166 | 48.97% |
| stop | 162 | 47.79% |
| daily_limit | 6 | 1.77% |
| hard_stop_cap | 4 | 1.18% |
| session | 1 | 0.29% |

## Shorts — HS only
Rows: **1833**
| exit_route | count | % of rows |
|------------|-------|----------|
| tp1 | 971 | 52.97% |
| stop | 654 | 35.68% |
| daily_limit | 86 | 4.69% |
| tp2 | 63 | 3.44% |
| hard_stop_cap | 49 | 2.67% |
| session | 8 | 0.44% |
| time | 2 | 0.11% |

### Shorts (HS) — TP2 vs not TP2
- **TP2** (`Target2`): **63** (3.44% of HS rows)
- **Not TP2**: **1770** (96.56%)
- **TP1** (`Target`): **971** (52.97% of HS rows)

## v21 note
Code defaults are already **HL = 35, 2, 2, 3** (ADX, SL, TP1, TP2). Surrogate v21 was a *small* tweak suggestion (e.g. ADX/TP2), not “change everything.” If you want **no change**, keep **35 / 2 / 2 / 3** until your own read of the table above says otherwise.
