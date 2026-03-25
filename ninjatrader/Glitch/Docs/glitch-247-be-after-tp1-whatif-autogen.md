# HL/HS — breakeven after TP1 (what-if)

**Method:** Group rows by `Entry name` + `Entry time`. Sort legs by `Exit time` (then Trade number). If leg1 is **TP1** (`Target`) and leg2 is **Stop**, replace leg2 PnL with **0** (breakeven; ignores minor commission asymmetry).

- HL+HS rows: **3158**
- Groups with **2 rows** (typical 2-leg): **483** groups
- Groups with **1 row**: **2192**
- Groups with **>2 rows**: **0** (excluded from pairing logic)

## Pairs: TP1 then Stop (BE helps second leg)

- Count: **34**
- **Δ PnL** (replace leg2 with 0): **1,166.40**
  - HL: **443.90** (14 pairs)
  - HS: **722.50** (20 pairs)

## Pairs: TP1 then TP2 (unchanged under BE rule)

- Count: **94**

## Portfolio impact (HL+HS rows only)

- **HL+HS net (actual):** 9,659.90
- **HL+HS net (what-if):** 10,826.30
- **Δ on HL+HS:** 1,166.40

## All trades

- **Full strategy net (actual):** 10,150.30
- **Full strategy net (what-if):** 11,316.70
- **Δ total:** 1,166.40

### Second-leg PnL (Stop) before BE replacement

- Mean: **-34.31**, Median: **-32.85**, Min: **-58.60**, Max: **-18.10**
