# v12 Analysis (trades + trade events)

- Trades file: `C:\Users\alan\OneDrive\Desktop\Glitch 247 Trades Adaptive v12.csv`
- Events file: `C:\Users\alan\Documents\NinjaTrader 8\GlitchData\Telemetry\Glitch247TradeEvents12.csv`
- ADX join coverage: `71.3%`
- Time offset applied (events -> trades): `0 days 02:00:00`
- Net PnL: all `$7,754.10`, long `$2,110.90`, short `$5,643.20`

## By play type

      trades     net    avg  winrate     pf     mae     mfe
play                                                       
HL      1759  2683.6  1.526    0.479  1.063  30.881  42.964
HS      2187  6955.1  3.180    0.510  1.115  34.412  50.621
LL       447  -572.7 -1.281    0.345  0.903  15.436  20.820
LS       374 -1311.9 -3.508    0.283  0.766  16.939  21.269

## By play x exit type

                            trades      net     avg
play Exit name                                     
HL   Target                    817  43163.8   52.83
     Target2                     3    402.7  134.23
     Exit on session close      11    323.0   29.36
     TimeExit                    4    250.6   62.65
     DailyPnLLimit              64   -547.3   -8.55
     Stop                      860 -40909.2  -47.57
HS   Target                   1083  64114.7   59.20
     Target2                     6    518.9   86.48
     Exit on session close       9    216.8   24.09
     TimeExit                    1    104.4  104.40
     HardStopCap                 3   -702.6 -234.20
     DailyPnLLimit             101  -2620.6  -25.95
     Stop                      984 -54676.5  -55.57
LL   Target                    151   5317.4   35.21
     Exit on session close       4     -6.9   -1.72
     DailyPnLLimit               7   -132.2  -18.89
     Stop                      285  -5751.0  -20.18
LS   Target                    104   4286.6   41.22
     Exit on session close       2      4.3    2.15
     DailyPnLLimit               3    -62.8  -20.93
     HardStopCap                 1   -117.1 -117.10
     Stop                      264  -5422.9  -20.54

## ADX winners vs losers
- `LL` winners median ADX `29.67`, losers `28.72`, delta `0.95`
- `LS` winners median ADX `28.75`, losers `28.57`, delta `0.18`
- `HL` winners median ADX `31.46`, losers `30.60`, delta `0.87`
- `HS` winners median ADX `32.86`, losers `31.20`, delta `1.65`

## ADX what-ifs (entry filter only)
- `LL` best-net floor: `ADX >= 34` -> n `97`, net `$446.30`, avg `$4.60`, win `44.3%`, PF `1.427`
  - balanced floor (>=60% sample): `ADX >= 18` -> n `323`, net `$10.20`, avg `$0.03`, win `36.2%`, PF `1.003`
- `LS` best-net floor: `ADX >= 32` -> n `99`, net `$164.10`, avg `$1.66`, win `37.4%`, PF `1.130`
  - balanced floor (>=60% sample): `ADX >= 28` -> n `184`, net `$-397.90`, avg `$-2.16`, win `29.9%`, PF `0.842`
- `HL` best-net floor: `ADX >= 18` -> n `1263`, net `$5,804.70`, avg `$4.60`, win `50.7%`, PF `1.202`
  - balanced floor (>=60% sample): `ADX >= 18` -> n `1263`, net `$5,804.70`, avg `$4.60`, win `50.7%`, PF `1.202`
- `HS` best-net floor: `ADX >= 34` -> n `622`, net `$2,808.40`, avg `$4.52`, win `55.1%`, PF `1.157`
  - balanced floor (>=60% sample): `ADX >= 30` -> n `943`, net `$2,073.00`, avg `$2.20`, win `52.9%`, PF `1.079`

## Stop/Target what-ifs (using realized MAE/MFE bounds)
- `LL` n `323`: mean R pnl `-0.016`, med R pnl `-1.050`, med R MAE `1.000`, med R MFE `1.167`
  - fraction reaching R targets (MFE>=R): 1.00:0.54 1.25:0.48 1.50:0.44 1.75:0.40 2.00:0.28 2.50:0.03 3.00:0.02
- `LS` n `287`: mean R pnl `-0.238`, med R pnl `-1.059`, med R MAE `1.000`, med R MFE `0.854`
  - fraction reaching R targets (MFE>=R): 1.00:0.46 1.25:0.41 1.50:0.37 1.75:0.30 2.00:0.23 2.50:0.01 3.00:0.01
- `HL` n `1263`: mean R pnl `0.242`, med R pnl `0.802`, med R MAE `0.917`, med R MFE `1.481`
  - fraction reaching R targets (MFE>=R): 1.00:0.59 1.25:0.55 1.50:0.45 1.75:0.11 2.00:0.06 2.50:0.01 3.00:0.01
- `HS` n `1524`: mean R pnl `0.243`, med R pnl `0.915`, med R MAE `0.850`, med R MFE `1.484`
  - fraction reaching R targets (MFE>=R): 1.00:0.60 1.25:0.55 1.50:0.44 1.75:0.13 2.00:0.06 2.50:0.02 3.00:0.01
