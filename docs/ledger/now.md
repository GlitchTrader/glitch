# Now — Glitch v0.0.2.x

**Updated:** 2026-07-22

## Release truth

| Edition | Public version | Source | Channel |
|---|---:|---|---|
| Standard | 0.0.2.0 | `58a266ae5fce87f54ef390ef0431b2532f1f82ff` | `/latest` |
| Experimental AI | 0.0.2.2 | `2975b2e4070af118d7e752ca7566aa2353647ccf` | `/latest/ai` |
| Hermes profile | 0.0.2.4 | tag `v0.0.2.4` | `hermes profile update glitch` |

The maintained source lanes are `standard/20` and `ai/22`. Former cleanup branch names are retired. Release commit `d71c647203220273562e85f08c13ae047c0127cf` owns the current explicit catalog.

## AI v0.0.2.2 source truth

- Intent v3 supports stable Glitch leg IDs and per-leg TP/SL amendments while retaining bounded v2 compatibility.
- Independently valid protected legs and same-direction protected additions remain cognitive choices.
- Stops may tighten or fall back. Widening requires fresh authoritative Apex state and total-downside recomputation; unsafe widening changes no order.
- One minute publisher emits paired complete frames and gap-aware five-frame packets.
- Flat decisions use five elapsed minutes; positioned decisions use every new packet; recognized failures retry on the next available packet.
- Locks recover dead owners; intent state is atomic and idempotent; ambiguous restart state is never blindly resubmitted.
- Closing the window hides the retained runtime; safety, packets, reconciliation, and servers continue until AddOn termination.
- Learning batches outcomes, NOTHING, rejection, and missed-opportunity evidence into hourly, 300-minute, and completed-session review.
- AI entries remain MARKET-only. LIMIT requires a complete pending-order lifecycle.

## Shipped verification

- Shared source contracts: 41.
- AI/Hermes contracts: 142.
- Combined source suite: 183.
- NinjaTrader F5 compile: operator-reported green before export.
- Public profile has exactly the minute direct operator and 15-minute learning supervisor jobs.

This does not establish profitability, unattended PA/live readiness, holiday/special-close completeness, or dependency recovery.

## Next evidence

1. Runtime-proof per-leg amendments, distinct additions, safe widening, unsafe zero-mutation rejection, follower mirroring, and final flat/order-free state.
2. Prove hidden-window continuity and crash recovery in bounded Sim.
3. Freeze an attributable performance sample before changing cognition or claiming improvement.
4. Resolve authoritative holiday/special-close and dependency/recovery gaps before unattended promotion.
