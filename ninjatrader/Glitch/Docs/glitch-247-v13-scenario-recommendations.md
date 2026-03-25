# v13 Recommendations from v12 + Telemetry10

- Trades: `C:\Users\alan\OneDrive\Desktop\Glitch 247 Trades Adaptive v12.csv`
- Events: `C:\Users\alan\Documents\NinjaTrader 8\GlitchData\Telemetry\Glitch247TradeEvents12.csv`
- Telemetry: `C:\Users\alan\Documents\NinjaTrader 8\GlitchData\Telemetry\Glitch247Telemetry10.csv`
- Join offset used: `0 days 02:00:00`
- Joined ATR/ADX coverage: `71.3%`

## v12 baseline by play

      trades     net    avg  winrate     pf     mae     mfe
play                                                       
HL      1759  2683.6  1.526    0.479  1.063  30.881  42.964
HS      2187  6955.1  3.180    0.510  1.115  34.412  50.621
LL       447  -572.7 -1.281    0.345  0.903  15.436  20.820
LS       374 -1311.9 -3.508    0.283  0.766  16.939  21.269

## v12 by play x exit

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

## Telemetry10 ATR distribution by play (bar-level)

        bars  atr_mean  atr_q25  atr_q50  atr_q75
play                                             
LL    983647    24.476   11.999   19.121   32.384
LS    706533    23.426   11.922   18.553   30.444
HL    251395    38.079   21.241   30.837   46.640
HS    249534    41.016   22.431   34.131   51.578

## ADX filter sweeps (from joined v12 entries)

      best_k  best_n  best_net  best_wr  best_pf  balanced_k  balanced_n  balanced_net
play                                                                                  
LL        34      97     446.3    0.443    1.427          18         323          10.2
LS        32      99     164.1    0.374    1.130          28         184        -397.9
HL        18    1263    5804.7    0.507    1.202          18        1263        5804.7
HS        34     622    2808.4    0.551    1.157          30         943        2073.0

## Stop/Target scenario recommendations (ATR units)

      n_joined  rec_stop_atr  rec_tp_sl_ratio  rec_tp_atr  p_tp_at_rec  p_sl_at_rec  exp_r_proxy  telemetry_atr_ticks_q50  implied_stop_ticks_q50  implied_tp_ticks_q50
play                                                                                                                                                                   
LL         323          1.75             1.50       2.625        0.393        0.037        0.553                   19.121                  33.461                50.191
LS         287          1.75             1.25       2.188        0.376        0.084        0.387                   18.553                  32.467                40.584
HL        1263          0.80             2.50       2.000        0.591        0.714        0.764                   30.837                  24.669                61.673
HS        1524          0.80             2.50       2.000        0.598        0.666        0.830                   34.131                  27.304                68.261

## Notes on 1.25R ambiguity (1/1.25 vs 2/2.5)

- Same R ratio does **not** imply same behavior. In absolute ATR space, larger stop/target pairs change hit probabilities because price noise and session/time exits are finite.
- This report therefore recommends both **ratio** and **absolute ATR stop** per play.

## Top scenario candidates per play (proxy ranking)

### LL
 stop_atr  ratio_tp_over_sl  tp_atr  p_tp  p_sl  exp_r_proxy
     1.75              1.50   2.625 0.393 0.037        0.553
     1.75              1.25   2.188 0.443 0.037        0.516
     2.00              1.25   2.500 0.418 0.034        0.488
     1.75              1.00   1.750 0.498 0.037        0.461
     2.00              1.00   2.000 0.452 0.034        0.418
     0.80              2.50   2.000 0.452 0.746        0.384
     1.00              2.50   2.500 0.418 0.709        0.336
     1.50              1.75   2.625 0.393 0.359        0.329

### LS
 stop_atr  ratio_tp_over_sl  tp_atr  p_tp  p_sl  exp_r_proxy
     1.75              1.25   2.188 0.376 0.084        0.387
     1.75              1.50   2.625 0.303 0.084        0.371
     1.75              1.00   1.750 0.436 0.084        0.352
     2.00              1.00   2.000 0.397 0.073        0.324
     2.00              1.25   2.500 0.317 0.073        0.323
     0.80              2.50   2.000 0.397 0.805        0.188
     2.00              1.50   3.000 0.157 0.073        0.162
     1.50              1.50   2.250 0.366 0.449        0.099

### HL
 stop_atr  ratio_tp_over_sl  tp_atr  p_tp  p_sl  exp_r_proxy
     0.80              2.50    2.00 0.591 0.714        0.764
     1.00              2.50    2.50 0.545 0.657        0.705
     0.80              2.00    1.60 0.644 0.714        0.573
     1.00              2.00    2.00 0.591 0.657        0.526
     1.25              2.00    2.50 0.545 0.607        0.482
     2.00              1.25    2.50 0.545 0.226        0.455
     0.80              1.75    1.40 0.667 0.714        0.454
     1.00              1.75    1.75 0.620 0.657        0.428

### HS
 stop_atr  ratio_tp_over_sl  tp_atr  p_tp  p_sl  exp_r_proxy
     0.80              2.50   2.000 0.598 0.666        0.830
     1.00              2.50   2.500 0.545 0.627        0.736
     0.80              2.00   1.600 0.652 0.666        0.637
     1.00              2.00   2.000 0.598 0.627        0.570
     0.80              1.75   1.400 0.680 0.666        0.524
     1.25              2.00   2.500 0.545 0.571        0.520
     1.00              1.75   1.750 0.631 0.627        0.477
     1.25              1.75   2.188 0.578 0.571        0.441

