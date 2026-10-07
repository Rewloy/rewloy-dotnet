# Live suite: not covered yet (for the 0.3.0 regeneration)

The suite follows library 0.2.4 / API 1.2.x. The API 1.3.0 additions below have no
method in 0.2.4, so they are not exercised; add them when the library is regenerated:

- Earn rules (groups, one line-item schema) and `recordSale` with **receipt lines**
  (0.2.4's `RecordSaleBody` has no lines; today the suite covers a sale with and
  without `Reference` and with and without a branch).
- Branch QR: one QR per branch, curated/seasonal programmes, session-reuse multi-join,
  code cards single-entry, branch freeze.
- The typed `Environment` field of `GET /v1/meta` (0.2.4 has it only in
  `AdditionalProperties`; the guard reads it by reflection first, then from there).
- `SendBatchLink` refusals `BATCH_EXPIRED` and `BATCH_FULL` (need a code that has
  expired or run out of cards: a public claim and a past `ValidUntil`) and
  `PROGRAM_ARCHIVED` on send (on 1.2.2 archiving closes the program's open codes, so the
  server answers `BATCH_CLOSED` first; the suite accepts either).
- Webhook delivery itself (`TestWebhookAsync`, deliveries, signature verification
  against a received delivery): needs a public https receiver.
- Live event stream (SSE), holder (Cuzdan) sessions, team/roles, campaigns,
  automations, segments, shops/checkout cards, POS keys, locations management.
- The netstandard2.0 asset (the suite runs on the net8.0 library build).
