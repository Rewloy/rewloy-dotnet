# Live suite: not covered yet

The suite follows library 0.3.0 / API 1.3.2. Receipt lines with an earn rule set,
`PreviewEarnAsync` / `PreviewSaleAsync`, the earn explanation, a line refund, the
branch QR (public page and the downloads), `CopyProgramAsync` and the typed
`Environment` of `GET /v1/meta` are covered. Not covered, and why:

- **Branch freeze** (`FreezeLocationAsync`, `UnfreezeLocationAsync`, the freeze list
  and edits) and so `LOCATION_FROZEN` / `BUSINESS_FROZEN` on a sale or a preview: a
  freeze needs a team session **and the owner's password** (a key is
  `CREDENTIAL_NOT_ALLOWED`, which the suite does check); the suite never holds a
  password. A freeze also counts against the branch's four-per-year limit.
- Branch QR writes: `PutLocationQrItemsAsync`, `AddQrItemsAsync`, session-reuse
  multi-join (`JoinHolderBranchAsync`, needs a holder session), code cards
  single-entry (`ProofRequired`), `UpdateBatchAsync`, `ExtendProgramCardsAsync`.
- Earn rules beyond a stamp rule: points / cashback / VIP rules and caps,
  `CreateEarnRuleAsync` / `UpdateEarnRuleAsync` single-rule edits, ignoring seen lines,
  a receipt from a shop (source-bound group members).
- `SendBatchLink` refusals `BATCH_EXPIRED` and `BATCH_FULL` (need a code that has
  expired or run out of cards: a public claim and a past `ValidUntil`) and
  `PROGRAM_ARCHIVED` on send (on 1.2.2 and later archiving closes the program's open
  codes, so the server answers `BATCH_CLOSED` first; the suite accepts either).
- Webhook delivery itself (`TestWebhookAsync`, deliveries, signature verification
  against a received delivery, the new `pass.extended` / `location.*` events): needs a
  public https receiver.
- Live event stream (SSE), holder (Cuzdan) sessions, team/roles, campaigns,
  automations, segments, shops/checkout cards, POS keys, locations management.
- The netstandard2.0 asset (the suite runs on the net8.0 library build).
