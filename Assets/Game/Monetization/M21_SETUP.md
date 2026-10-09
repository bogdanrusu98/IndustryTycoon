# M21 production setup

The runtime intentionally ships without invented dashboard identifiers, product IDs,
credentials, receipts, or legal copy. Editor/development builds use deterministic test
ad and store adapters. Player builds degrade to unavailable services until production
providers are installed and configured.

Required external setup:

- Install and configure the chosen rewarded-ad SDK. Map `OfflineDouble`,
  `ProductionBoost`, and `OrderRefresh` to dashboard ad-unit IDs. Ads remain
  non-personalized/disabled until the player grants the required consent.
- Install Unity IAP (or another store adapter), create store products, then map the
  logical keys `small_gems`, `medium_gems`, `large_gems`, and `starter_pack` to real
  store IDs. Use store metadata for localized price strings.
- Validate receipts server-side for production and persist consumable fulfillment
  remotely before confirming purchases. The local ledger prevents duplicate callbacks
  on one installation but is not a fraud boundary.
- Install/configure the chosen analytics provider and map the compact internal events.
  Configure consent, retention, deletion, access, and dashboard schemas externally.
- Supply reviewed Privacy Policy/provider disclosures and connect the Settings button
  to the final URL. The current button is explicitly a placeholder.

Reset Save deletes gameplay state, Gems, consumable balances, reward claim history,
boost/cooldown state, and local pending purchase state. Device privacy/settings remain.
The Starter Pack permanent entitlement marker lives outside gameplay save and must also
be restored from the store; it is never trusted as the sole source of entitlement.

Timers reject local UTC rollback and persist accepted anchors, expiries, cooldowns, and
daily-window usage. They remain client-authoritative: a modified device clock/save or a
reinstalled app cannot be made fraud-resistant without trusted server time and remote
purchase/reward state.
