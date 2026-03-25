# v4 vs v7 trade export comparison
## v4
- **Trades:** 3513 (long 700, short 2813)
- **Net PnL (all):** $10,657.20
- **Net long:** $-587.90 | **Net short:** $11,245.10
- **Approx max DD (chronological, long-only series):** $-1,985.40
- **Approx max DD (chronological, short-only series):** $-2,320.10
- **Win rate:** 51.98% | Avg trade: $3.03
- **Largest win / loss:** $501.80 / $-235.20

### Exit mix (sum pnl, count)
                                       sum  count    mean
Market pos. Exit name                                    
Long        DailyPnLLimit           -880.3     30  -29.34
            Exit on session close    -30.8      2  -15.40
            HardStopCap             -348.3      2 -174.15
            Stop                  -13934.5    296  -47.08
            Target                 13484.8    347   38.86
            Target2                 1018.4     21   48.50
            TimeExit                 102.8      2   51.40
Short       DailyPnLLimit           -472.3    137   -3.45
            Exit on session close    -23.0      6   -3.83
            HardStopCap             -351.3      2 -175.65
            Stop                  -52570.9   1259  -41.76
            Target                 51353.3   1157   44.38
            Target2                13215.0    250   52.86
            TimeExit                  94.3      2   47.15
## v7
- **Trades:** 3436 (long 255, short 3181)
- **Net PnL (all):** $-4,425.40
- **Net long:** $591.70 | **Net short:** $-5,017.10
- **Approx max DD (chronological, long-only series):** $-993.30
- **Approx max DD (chronological, short-only series):** $-7,921.00
- **Win rate:** 47.53% | Avg trade: $-1.29
- **Largest win / loss:** $495.40 / $-233.20

### Exit mix (sum pnl, count)
                                       sum  count    mean
Market pos. Exit name                                    
Long        DailyPnLLimit            758.6      6  126.43
            Exit on session close      2.3      2    1.15
            Stop                   -5714.2    122  -46.84
            Target                  5117.2    118   43.37
            Target2                  427.8      7   61.11
Short       DailyPnLLimit           1389.5     55   25.26
            Exit on session close     98.6      6   16.43
            HardStopCap            -2222.4     13 -170.95
            Stop                  -70783.6   1640  -43.16
            Target                 54511.5   1235   44.14
            Target2                11968.4    231   51.81
            TimeExit                  20.9      1   20.90
## Delta (v7 − v4)
- Δ net all: $-15,082.60
- Δ net long: $1,179.60
- Δ net short: $-16,262.20
- Δ short count: 368
- Δ long count: -445

## Telemetry (Glitch247Telemetry.csv, chunked aggregates)
| play (bar label) | bars | mean ADX | mean ATR ticks |
|---|---:|---:|---:|
| LL | 983647 | 24.72 | 24.04 |
| LS | 706533 | 24.53 | 23.17 |
| HL | 251395 | 25.52 | 38.75 |
| HS | 249534 | 27.08 | 42.80 |

*Exporter default ATR period may be 5; strategy uses 10 — compare like-for-like when tuning.*
