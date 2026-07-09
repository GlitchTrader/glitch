# Tradovate + NinjaTrader + Apex — instrument universe

**Captured:** 2026-07-08 (operator)  
**Source:** Tradovate / Apex tradable list as available through NT + Apex stack  
**Status:** Reference catalog for future work — not wired into product code yet

## Intent (operator note)

These are the assets we should learn to **bridge, indicate, normalize, ingest, and mine** as the AI program (v0.0.2.x / Hermes) matures.

Planned workflow:

1. Use the analytics bridge to export **~2 years** of history in the **same normalized units** the bridge will emit at runtime (so offline training matches live inference).
2. Operator may already have partial exports; goal is to **improve the bridge** to collect richer, more diverse normalized fields, then re-export.
3. **Mine** exported corpus → connect **real-time** ingest after metadata/registry and normalization are stable.

Related backlog: **GL-025** (instrument metadata + bridge normalization), **GL-026** (normalized analytics), **GL-028+** (Hermes ingest).

Roadmap: `docs/ai-program/roadmap.md`

## Catalog

| kind | name | symbol |
|------|------|--------|
| Futures | 1-Ounce Gold | 1OZ |
| Futures | 10-Year T-Note | ZN |
| Futures | 100-Ounce Silver | SIC |
| Futures | 2-Year T-Note | ZT |
| Spread | 2-yr T-Note Calendar Spread | ZT CALENDAR SPREAD |
| Futures | 20yr US Treasury Bond | TWE |
| Futures | 3 Year US Treasury Notes | Z3N |
| Futures | 5-Year T-Note | ZF |
| Spread | 5-Year T-Note Calendar Spread | ZF CALENDAR SPREAD |
| Futures | Australian Dollar | 6A |
| Futures | Brazilian Real | 6L |
| Spread | Brent Crude Fin. (Last Day) Calendar Spread | BZ CALENDAR SPREAD |
| Futures | Brent Crude Last Day Financial | BZ |
| Futures | British Pound | 6B |
| Futures | CME Bitcoin | BTC |
| Futures | CME Micro XRP | MXP |
| Futures | CME Solana | GSOL |
| Futures | CME XRP | GXRP |
| Futures | Canadian Dollar | 6C |
| Futures | Copper | HG |
| Spread | Copper Calendar Spread | HG CALENDAR SPREAD |
| Futures | Corn | ZC |
| Spread | Corn Calendar Spread | ZC CALENDAR SPREAD |
| Spread | Crude Calendar Spread | CL CALENDAR SPREAD |
| Futures | Crude Oil | CL |
| Futures | E-Micro Australian Dollar | M6A |
| Futures | E-Micro British Pound | M6B |
| Futures | E-Micro Canadian Dollar | MCD |
| Futures | E-Micro Euro | M6E |
| Futures | E-Micro Gold | MGC |
| Spread | E-Micro Gold Calendar Spread | MGC CALENDAR SPREAD |
| Futures | E-Micro Japanese Yen | MJY |
| Futures | E-Micro Swiss Franc | MSF |
| Futures | E-Mini Copper | QC |
| Futures | E-Mini Crude Oil | QM |
| Futures | E-Mini Dow ($5) | YM |
| Spread | E-Mini Dow ($5) Reverse Calendar Spread | YM REVERSE SPREAD |
| Futures | E-Mini Euro FX | E7 |
| Spread | E-Mini Euro FX Reverse Calendar Spread | E7 REVERSE SPREAD |
| Futures | E-Mini Gold | QO |
| Futures | E-Mini Heating Oil | QH |
| Futures | E-Mini Japanese Yen | J7 |
| Spread | E-Mini MidCap 400 Reverse Spread | EMD REVERSE SPREAD |
| Futures | E-Mini NASDAQ 100 | NQ |
| Spread | E-Mini NASDAQ Reverse Spread | NQ REVERSE SPREAD |
| Futures | E-Mini Natural Gas | QG |
| Futures | E-Mini Russell 2000 | RTY |
| Futures | E-Mini S&P 500 | ES |
| Futures | E-Mini S&P Midcap 400 | EMD |
| Spread | E-mini Crude Oil Calendar Spread | QM CALENDAR SPREAD |
| Spread | E-mini Natural Gas Calendar Spread | QG CALENDAR SPREAD |
| Spread | E-mini Russell 2000 Reverse Calendar Spread | RTY REVERSE SPREAD |
| Spread | E-mini S&P Reverse Calendar Spread | ES REVERSE SPREAD |
| Spread | E-mini Yen Reverse Calendar Spread | J7 REVERSE SPREAD |
| Futures | Ether | ETH |
| Futures | Euro FX | 6E |
| Futures | Eurodollar | GE |
| Spread | Eurodollar Calendar Spread | GE CALENDAR SPREAD |
| Futures | Fed Funds 30 Day (Globex) | ZQ |
| Futures | Feeder Cattle | GF |
| Spread | Feeder Cattle Calendar Spread | GF CALENDAR SPREAD |
| Futures | Gold | GC |
| Spread | Gold Calendar Spread | GC CALENDAR SPREAD |
| Futures | Heating Oil | HO |
| Spread | Heating Oil Calendar Spread | HO CALENDAR SPREAD |
| Futures | Japanese Yen | 6J |
| Spread | KC Wheat Calendar Spread | KE CALENDAR SPREAD |
| Futures | Lean Hogs | HE |
| Spread | Lean Hogs Calendar Spread | HE CALENDAR SPREAD |
| Futures | Live Cattle | LE |
| Spread | Live Cattle Calendar Spread | LE CALENDAR SPREAD |
| Futures | Lumber | LBR |
| Spread | Lumber Calendar Spread | LBS CALENDAR SPREAD |
| Futures | Mexican Peso | 6M |
| Futures | Micro 10-Year Yield | 10Y |
| Futures | Micro 2-Year Yield | 2YY |
| Futures | Micro 30-Year Yield | 30Y |
| Futures | Micro 5-Year Yield | 5YY |
| Futures | Micro Bitcoin | MBT |
| Futures | Micro Copper | MHG |
| Futures | Micro Corn | MZC |
| Futures | Micro Crude Oil | MCL |
| Futures | Micro E-mini Dow $0.50 | MYM |
| Futures | Micro E-mini NASDAQ-100 | MNQ |
| Futures | Micro E-mini Russell 2000 | M2K |
| Futures | Micro E-mini S&P 500 | MES |
| Futures | Micro E-mini S&P MidCap 400 Index | MMC |
| Futures | Micro E-mini S&P SmallCap 600 Index | MSC |
| Futures | Micro Ether | MET |
| Futures | Micro Henry Hub Natural Gas | MNG |
| Futures | Micro Nikkei Stock Average | MNK |
| Futures | Micro Silver | SIL |
| Futures | Micro Solana | MSL |
| Futures | Micro Soybean | MZS |
| Futures | Micro Soybean Meal | MZM |
| Futures | Micro Soybean Oil | MZL |
| Futures | Micro Ultra 10yr Note | MTN |
| Futures | Micro Ultra T-Bond | MWN |
| Futures | Micro Wheat | MZW |
| Futures | Mini Corn | XC |
| Futures | Mini Soybean | XK |
| Futures | Mini Wheat | XW |
| Spread | Mini-Sized Corn Calendar Spread | XC CALENDAR SPREAD |
| Spread | Mini-Sized Soybeans Calendar Spread | XK CALENDAR SPREAD |
| Spread | Mini-Sized Wheat Calendar Spread | XW CALENDAR SPREAD |
| Futures | Natural Gas | NG |
| Spread | Natural Gas Calendar Spread | NG CALENDAR SPREAD |
| Futures | New Zealand Dollar | 6N |
| Futures | Nikkei 225 (USD) | NKD |
| Futures | Oats | ZO |
| Spread | Oats Calendar Spread | ZO CALENDAR SPREAD |
| Futures | Palladium | PA |
| Spread | Palladium Calendar Spread | PA CALENDAR SPREAD |
| Futures | Platinum | PL |
| Futures | RBOB Gasoline | RB |
| Spread | RBOB Gasoline Calendar Spread | RB CALENDAR SPREAD |
| Futures | Random Length Lumber | LBS |
| Futures | Rough Rice | ZR |
| Spread | Rough Rice Calendar Spread | ZR CALENDAR SPREAD |
| Futures | Silver | SI |
| Spread | Silver Calendar Spread | SI CALENDAR SPREAD |
| Spread | Soybean Calendar Spread | ZS CALENDAR SPREAD |
| Futures | Soybean Meal | ZM |
| Spread | Soybean Meal Calendar Spread | ZM CALENDAR SPREAD |
| Futures | Soybean Oil | ZL |
| Spread | Soybean Oil Calendar Spread | ZL CALENDAR SPREAD |
| Futures | Soybeans | ZS |
| Futures | Swiss Franc | 6S |
| Futures | US Treasury Bond | ZB |
| Futures | Ultra 10-Year T-Note | TN |
| Futures | Ultra US Treasury Bond | UB |
| Futures | Wheat | ZW |
| Spread | Wheat Calendar Spread | ZW CALENDAR SPREAD |
| Futures | miNY Silver | QI |

**Counts:** 108 futures · 40 spreads · **148 total** (as pasted; verify against live Tradovate/Apex before production gating).

## Machine-readable copy

Tab-separated duplicate for scripts (`kind`, `name`, `symbol`):

```
kind	name	symbol
Futures	1-Ounce Gold	1OZ
Futures	10-Year T-Note	ZN
Futures	100-Ounce Silver	SIC
Futures	2-Year T-Note	ZT
Spread	2-yr T-Note Calendar Spread	ZT CALENDAR SPREAD
Futures	20yr US Treasury Bond	TWE
Futures	3 Year US Treasury Notes	Z3N
Futures	5-Year T-Note	ZF
Spread	5-Year T-Note Calendar Spread	ZF CALENDAR SPREAD
Futures	Australian Dollar	6A
Futures	Brazilian Real	6L
Spread	Brent Crude Fin. (Last Day) Calendar Spread	BZ CALENDAR SPREAD
Futures	Brent Crude Last Day Financial	BZ
Futures	British Pound	6B
Futures	CME Bitcoin	BTC
Futures	CME Micro XRP	MXP
Futures	CME Solana	GSOL
Futures	CME XRP	GXRP
Futures	Canadian Dollar	6C
Futures	Copper	HG
Spread	Copper Calendar Spread	HG CALENDAR SPREAD
Futures	Corn	ZC
Spread	Corn Calendar Spread	ZC CALENDAR SPREAD
Spread	Crude Calendar Spread	CL CALENDAR SPREAD
Futures	Crude Oil	CL
Futures	E-Micro Australian Dollar	M6A
Futures	E-Micro British Pound	M6B
Futures	E-Micro Canadian Dollar	MCD
Futures	E-Micro Euro	M6E
Futures	E-Micro Gold	MGC
Spread	E-Micro Gold Calendar Spread	MGC CALENDAR SPREAD
Futures	E-Micro Japanese Yen	MJY
Futures	E-Micro Swiss Franc	MSF
Futures	E-Mini Copper	QC
Futures	E-Mini Crude Oil	QM
Futures	E-Mini Dow ($5)	YM
Spread	E-Mini Dow ($5) Reverse Calendar Spread	YM REVERSE SPREAD
Futures	E-Mini Euro FX	E7
Spread	E-Mini Euro FX Reverse Calendar Spread	E7 REVERSE SPREAD
Futures	E-Mini Gold	QO
Futures	E-Mini Heating Oil	QH
Futures	E-Mini Japanese Yen	J7
Spread	E-Mini MidCap 400 Reverse Spread	EMD REVERSE SPREAD
Futures	E-Mini NASDAQ 100	NQ
Spread	E-Mini NASDAQ Reverse Spread	NQ REVERSE SPREAD
Futures	E-Mini Natural Gas	QG
Futures	E-Mini Russell 2000	RTY
Futures	E-Mini S&P 500	ES
Futures	E-Mini S&P Midcap 400	EMD
Spread	E-mini Crude Oil Calendar Spread	QM CALENDAR SPREAD
Spread	E-mini Natural Gas Calendar Spread	QG CALENDAR SPREAD
Spread	E-mini Russell 2000 Reverse Calendar Spread	RTY REVERSE SPREAD
Spread	E-mini S&P Reverse Calendar Spread	ES REVERSE SPREAD
Spread	E-mini Yen Reverse Calendar Spread	J7 REVERSE SPREAD
Futures	Ether	ETH
Futures	Euro FX	6E
Futures	Eurodollar	GE
Spread	Eurodollar Calendar Spread	GE CALENDAR SPREAD
Futures	Fed Funds 30 Day (Globex)	ZQ
Futures	Feeder Cattle	GF
Spread	Feeder Cattle Calendar Spread	GF CALENDAR SPREAD
Futures	Gold	GC
Spread	Gold Calendar Spread	GC CALENDAR SPREAD
Futures	Heating Oil	HO
Spread	Heating Oil Calendar Spread	HO CALENDAR SPREAD
Futures	Japanese Yen	6J
Spread	KC Wheat Calendar Spread	KE CALENDAR SPREAD
Futures	Lean Hogs	HE
Spread	Lean Hogs Calendar Spread	HE CALENDAR SPREAD
Futures	Live Cattle	LE
Spread	Live Cattle Calendar Spread	LE CALENDAR SPREAD
Futures	Lumber	LBR
Spread	Lumber Calendar Spread	LBS CALENDAR SPREAD
Futures	Mexican Peso	6M
Futures	Micro 10-Year Yield	10Y
Futures	Micro 2-Year Yield	2YY
Futures	Micro 30-Year Yield	30Y
Futures	Micro 5-Year Yield	5YY
Futures	Micro Bitcoin	MBT
Futures	Micro Copper	MHG
Futures	Micro Corn	MZC
Futures	Micro Crude Oil	MCL
Futures	Micro E-mini Dow $0.50	MYM
Futures	Micro E-mini NASDAQ-100	MNQ
Futures	Micro E-mini Russell 2000	M2K
Futures	Micro E-mini S&P 500	MES
Futures	Micro E-mini S&P MidCap 400 Index	MMC
Futures	Micro E-mini S&P SmallCap 600 Index	MSC
Futures	Micro Ether	MET
Futures	Micro Henry Hub Natural Gas	MNG
Futures	Micro Nikkei Stock Average	MNK
Futures	Micro Silver	SIL
Futures	Micro Solana	MSL
Futures	Micro Soybean	MZS
Futures	Micro Soybean Meal	MZM
Futures	Micro Soybean Oil	MZL
Futures	Micro Ultra 10yr Note	MTN
Futures	Micro Ultra T-Bond	MWN
Futures	Micro Wheat	MZW
Futures	Mini Corn	XC
Futures	Mini Soybean	XK
Futures	Mini Wheat	XW
Spread	Mini-Sized Corn Calendar Spread	XC CALENDAR SPREAD
Spread	Mini-Sized Soybeans Calendar Spread	XK CALENDAR SPREAD
Spread	Mini-Sized Wheat Calendar Spread	XW CALENDAR SPREAD
Futures	Natural Gas	NG
Spread	Natural Gas Calendar Spread	NG CALENDAR SPREAD
Futures	New Zealand Dollar	6N
Futures	Nikkei 225 (USD)	NKD
Futures	Oats	ZO
Spread	Oats Calendar Spread	ZO CALENDAR SPREAD
Futures	Palladium	PA
Spread	Palladium Calendar Spread	PA CALENDAR SPREAD
Futures	Platinum	PL
Futures	RBOB Gasoline	RB
Spread	RBOB Gasoline Calendar Spread	RB CALENDAR SPREAD
Futures	Random Length Lumber	LBS
Futures	Rough Rice	ZR
Spread	Rough Rice Calendar Spread	ZR CALENDAR SPREAD
Futures	Silver	SI
Spread	Silver Calendar Spread	SI CALENDAR SPREAD
Spread	Soybean Calendar Spread	ZS CALENDAR SPREAD
Futures	Soybean Meal	ZM
Spread	Soybean Meal Calendar Spread	ZM CALENDAR SPREAD
Futures	Soybean Oil	ZL
Spread	Soybean Oil Calendar Spread	ZL CALENDAR SPREAD
Futures	Soybeans	ZS
Futures	Swiss Franc	6S
Futures	US Treasury Bond	ZB
Futures	Ultra 10-Year T-Note	TN
Futures	Ultra US Treasury Bond	UB
Futures	Wheat	ZW
Spread	Wheat Calendar Spread	ZW CALENDAR SPREAD
Futures	miNY Silver	QI
```
