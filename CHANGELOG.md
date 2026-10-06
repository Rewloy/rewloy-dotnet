# Değişiklik günlüğü / Changelog

Bu kütüphanenin sürümleri. API'nin kendi değişiklikleri:
https://rewloy.com/gelistiriciler/degisiklikler

This library's releases. The API's own changes are listed at the link above.

## 0.2.4 (2026-10-06)

Rewloy API 1.2.0'ı izler (API sürümü, `info.version`): 260 işlem (0.2.2'de 256),
hiçbiri kaldırılmadı. Kasa yazımlarının yanıtında `card`, kartın işlem listesi,
webhook sırrını yenileme ve silme, POS anahtarları, test ortamını silmeden
sıfırlama, bütün kodların listesi. Webhook nesnesinde `pausedUntil` ve
`resumableUntil`; kod bağlantısı göndermede `BATCH_CLOSED`, `BATCH_EXPIRED`,
`BATCH_FULL` ve `PROGRAM_ARCHIVED` hataları. Ayrıca README'deki `rewardReady`
örneği `actions[].ready` okuyacak şekilde düzeltildi. 0.2.3 yalnız .NET ve
Kotlin'in paket sürümüydü; beş kütüphane 0.2.4'te aynı sürüme gelir.

Follows Rewloy API 1.2.0 (the product version in `info.version`): 260
operations (256 in 0.2.2), none removed. All five client libraries are 0.2.4.
Additive, except that `closed` in the test-reset answer is now always `null`
and `sendBatchLink` now refuses a code that issues no card (see below).

- **New operation `ListPassOperationsAsync`** (`GET /v1/passes/{serial}/operations`,
  paged): a card's ledger operations and coupon / discount-card uses, newest
  first, for a till's "last operations" list. Each carries `kind`, signed
  `delta` and `unit`, `at` (and `occurredAt` for a sale written later),
  `reference`, `source`, `byCaller`, and what undoes it: `undoWith`
  (`sale/reverse` or `actions/reverse`), `reversible` and, when not,
  `reason`; for this credential's own operations `saleKey` / `actionKey` to pass
  straight to the reverse call; `reversedBy`, `reversedAt`, `reverses`.
  Needs `passes.read`.
- **`card` on write answers** (`recordSale`, `passAction`, `reverseSale`,
  `reverseAction`): the card after the write, the fields of `getPass` except
  `customer` (`programName`, `currency`, `stamps` / `points` / `money`,
  `rewardReady`, `actions`, `sale`…), read in the same transaction. On a replay
  (`duplicate: true`) it is the card's current state. It is `null` when the
  credential lacks `passes.read` in the card's programme (a till-only plugin
  key), so the type is nullable. No second `getPass` is needed to draw a receipt.
- **`reversed` on `recordSale` and `passAction` answers**: `true` only on a
  replay of a sale that was taken back since (`credited` is what the first
  request wrote, the card no longer carries it); send a new key to write the
  receipt again.
- **`occurredAt` errors**: a rejected `occurredAt` is a `400 VALIDATION` whose
  `details[0].reason` says which limit: `in_future`, `too_old` (over 72 hours),
  `before_issue` (the card did not exist then: resend without `occurredAt`),
  `invalid`. Treat an unknown reason as `invalid`. (Documented on the error
  details; the field stays optional.)
- **New operations `rotateWebhookSecret`** (`POST /v1/developers/webhooks/{id}/rotate-secret`)
  and **`deleteWebhook`** (`DELETE /v1/developers/webhooks/{id}`, `204`). A
  rotation returns the new `secret` once; the old one keeps signing for 24
  hours, so `Rewloy-Signature` carries two `v1` values and the delivery has
  `Rewloy-Signature-Rotating: 1`. `verifyWebhook` already tried every `v1` and
  several secrets: pass `[new, old]` while you switch. Deleting removes the
  delivery history too.
- **POS keys**: `createApiKey` takes a second body shape, `kind: "pos"` with
  `locationId` and optional `register` (the built-in till role, one branch, named
  "POS · branch · register"), and answers with `baseUrl`; `listApiKeys` and
  `getApiKey` rows carry `pos` (`{ locationId, register } | null`) and
  `requestsToday`, and `listApiKeys` filters with `kind` (`pos` | `standard`).
- **Test environment reset** (`resetTestEnvironment`) keeps the test business: the
  body takes `revokeKeys` (default `false`; `true` also revokes the keys, closes
  the webhooks and cancels open store-link codes), and the answer counts
  `deleted` (`customers`, `cards`, `codes`, `outbox`, `webhookDeliveries`), `kept`
  (`programs`, `keys`, `webhooks`), `created`, `keysRevoked` and
  `walletCardsVoided`; `closed` is now always `null`. New error code
  `TEST_RESET_BUSY` (`409`).
- **New operation `listAllBatches`** (`GET /v1/batches`, paged): every gift-card,
  coupon and discount code of the business, newest first; filters `programId`,
  `type`, `status` and `q`. Each row's `state` (and the `status` filter) takes
  **`archived`**: the code itself is open but its card (programme) is archived,
  so its link issues nothing; `status` on the row stays `open` | `closed`. New error
  code `PROGRAM_ARCHIVED` (`409`) on `createBatch` for an archived programme.
- **Programme rows** (`listPrograms`, `getProgram`, `createProgram`,
  `updateProgram`) carry `programName`, always equal to `name` (the field name
  that `createProgram` takes and `getPass` returns).
- **Webhook state: `pausedUntil` and `resumableUntil`** on every webhook object
  (the rows of `listWebhooks`, and the `webhook` of `createWebhook`, `getWebhook`,
  `setWebhookStatus` and `rotateWebhookSecret`). Both are always present, a
  date-time or `null`. `pausedUntil`: an open webhook is paused (its receiver
  failed twice in a row with a `5xx`, a `429`, a connection error or no answer):
  its deliveries wait until this moment and are retried on their own, 60
  seconds; `null` when it is not paused or the webhook is off.
  `resumableUntil`: the rules turned the webhook off and keep its pending
  deliveries; turned on before this moment (24 hours after it was closed, with
  `setWebhookStatus` `{ "active": true }`) it carries on where it stopped, the
  kept deliveries go at once and the events that happened meanwhile arrive too;
  `null` while it is on, when a person or a key turned it off, or once the time
  has passed.
- **`sendBatchLink` refusals** (`POST /v1/batches/{id}/send`): the link of a code
  is e-mailed only while the code issues a card. A stopped code answers
  `410 BATCH_CLOSED`, one past its date `410 BATCH_EXPIRED`, one whose cards
  are all given `410 BATCH_FULL`, and a code whose programme is archived
  `409 PROGRAM_ARCHIVED` (a new `409` on this operation); no mail goes. Before
  1.2.0 the last three were sent anyway. The error codes were already in the
  library's list of codes; the operation's description now names all four.
- Descriptions only: `earnRate` / `cashbackRate` round down on a sale
  (`floor(amountMinor / 100 × earnRate)`, `floor(amountMinor × cashbackRate / 100)`);
  `currencyLocked` also for an open amount-valued coupon; `actions30` on a key
  now counts reads; `rewardReady` means "reward ready" only on stamp and points
  cards (always `true` on VIP, any balance on cashback and gift cards): read
  `actions[].ready` to know what can be done now; `kvkkConsent` on `issuePass`;
  `me` → `key.abilities` is not the key's permissions (those are `permissions`).
- **Fixed in the README**: the first example read `rewardReady` as "ready to
  redeem". It now reads `actions[].ready` (see `getPass`).
- .NET: `PausedUntil` and `ResumableUntil` are `DateTimeOffset?` on the webhook
  models (`ListWebhooksItem`, `GetWebhookData`, `SetWebhookStatusData` and the
  `Webhook` of `CreateWebhookData` and `RotateWebhookSecretData`); the
  `SendBatchLinkAsync` summary names the four refusals, and the codes are
  `ErrorCode.BatchClosed`, `BatchExpired`, `BatchFull` and `ProgramArchived`.
- .NET: new tests in `tests/Rewloy.Tests/V120Tests.cs` (the four new operations,
  paging, `Kind = "pos"`, `RevokeKeys`, a replayed sale with `Card == null`,
  `ErrorCode.ProgramArchived`, the webhook state fields, the four refusals of
  `SendBatchLinkAsync`); `RewloyException.Details` documents `reason`.
  `ListPassOperationsAllAsync` and `ListAllBatchesAllAsync` page with
  `await foreach`. Method names are the PascalCase operationIds with the
  `Async` suffix (`RotateWebhookSecretAsync`, `DeleteWebhookAsync`,
  `ListAllBatchesAsync`, `ResetTestEnvironmentAsync`).

## 0.2.3 (2026-10-05)

İlk nuget.org sürümü: `dotnet add package Rewloy`. Kod 0.2.2 ile aynı; paket bilgileri nuget.org için
tamamlandı (ikon, sürüm notları, mutlak README bağlantıları, paket doğrulaması) ve sürümler artık bir `v*`
etiketiyle GitHub Actions'tan Trusted Publishing ile yayımlanıyor.

First nuget.org release: `dotnet add package Rewloy`. The code is the same as 0.2.2; the package metadata is
completed for nuget.org (icon, release notes, absolute README links, package validation), and releases are now
published from GitHub Actions on a `v*` tag with Trusted Publishing.

## 0.2.2 (2026-10-05)

Rewloy 1.1.0'a (API sürümü) göre yeniden üretildi: 256 işlem (0.2.1'de 255). Kasa
için `ReverseActionAsync`, `RecordSaleBody.OccurredAt`, `PassActionBody.Reference`;
yanıtlarda `RateLimit-*` başlıkları.

Regenerated from Rewloy 1.1.0 (the product version in `info.version`): 256
operations (255 in 0.2.1).

- **New operation: `ReverseActionAsync`** (`POST /v1/passes/{serial}/actions/reverse`).
  Voids a till action made with `PassActionAsync` (`spend`, `spend-points`,
  `redeem-stamps`, `redeem-reward`, `use`), found by its `ActionKey` (the
  `Idempotency-Key` it was sent with) or its `Reference`. It needs no
  `Idempotency-Key`: an action is voided once and a repeat answers
  `Duplicate = true`. New error codes `ACTION_NOT_FOUND`, `ACTION_AMBIGUOUS`,
  `ACTION_NOT_REVERSIBLE` (constants of `ErrorCode`).
- **`RecordSaleBody.OccurredAt`** (optional `DateTimeOffset`): when the sale really
  happened, for a till that queues sales while offline.
- **`PassActionBody.Reference`** (optional), and **`PassActionAsync` returns a
  typed `PassActionData`** instead of a `JsonElement`. The generator turns a
  union of objects (`oneOf` of different shapes) into one class holding every
  member's properties: the ones not in every member are nullable and their
  documentation says which shape sends them (`Balance` for the balance-card
  answer; `Status`, `Uses`, `UsesLeft` for the coupon / discount-card answer).
  **Source-breaking for callers that read the old `JsonElement`** (also
  `MeAsync`, the only other union of objects in the API: it returns `MeData`,
  with `Kind` telling the staff session from the key).
- **Rate limit headers.** `RewloyResponse.RateLimit` and `RewloyException.RateLimit`
  (including `RateLimitException`) return a `RewloyRateLimit` (`Limit`,
  `Remaining`, `ResetSeconds`, `Reset` as a `TimeSpan`; from `RateLimit-Limit`,
  `RateLimit-Remaining`, `RateLimit-Reset`) or `null` when the answer has none.
  Additive.
- Tests no longer hard-code the version in the User-Agent check or the operation
  count (`> 200`, from the snapshot); the pack step in CI lists whatever
  `Rewloy.*.nupkg` it built.
- Webhook-creation responses may carry `warnings` (a non-live installation whose
  URL production would refuse); the `Idempotency-Key` parameter documents its
  8–64 printable ASCII rule; the API's descriptions no longer contain internal
  `ADR n` references. README: the till example has a void step and a note on
  `OccurredAt` for offline queues.

## 0.2.1 (2026-10-05)

Dışarıdan geliştiricilerin bulduğu üç sorun düzeltildi.

Three problems found by outside developers, fixed.

- **`Idempotency-Key` is checked before sending.** The client now refuses a key
  that is not printable ASCII (0x21-0x7E), 8-64 characters, with an
  `ArgumentException` ("Idempotency-Key yalnız ASCII karakterler içerebilir …"),
  and sends nothing. The API will also answer `400 VALIDATION` for such a key
  in its next release.
- **`BaseUrl` takes the address with or without `/v1`.** The documentation and
  the OpenAPI document show `https://app.rewloy.com/v1`, but the client wanted
  the origin only. Now both work; a trailing `/v1` or `/v1/` and trailing
  slashes are stripped (`client.BaseUrl` is the origin).
- **`RequestOptions.IdempotencyKey` is required where the API requires it.** For
  `RecordSaleAsync`, `PassActionAsync`, `SendCampaignAsync` and
  `RefundShopRedemptionAsync` the OpenAPI document marks the header required,
  but the client made up a random UUID when it was missing, which does not
  survive a restart of your program. Now the call throws an `ArgumentException`
  before sending if the key is missing. Where the header is optional
  (`IssuePassAsync`, …) a UUID is still generated and reused on every retry.
  **Breaking for callers that relied on the generated key** (a small break,
  taken in a patch release because the old behaviour could write a sale twice).

## 0.2.0 (2026-10-05)

İlk etiketli sürüm (GitHub Release; NuGet'e henüz çıkmadı). Rewloy API
1.0.5'e göre yeniden üretildi: 211 yol, 255 işlem (0.1.0 etiketlenmedi). Kasa
için `RecordSaleAsync` ve `ReverseSaleAsync`; README'de yeni bir kasa örneği,
test modu ve `BaseUrl`.

The first tagged release (a GitHub Release; not on NuGet yet). Regenerated from
Rewloy API 1.0.5: 211 paths, 255 operations (237 in the untagged 0.1.0).

- **New operations.**
  - *Till:* `RecordSaleAsync` (`POST /v1/passes/{serial}/sale`: write a
    completed sale to a card; the card type decides what is written) and
    `ReverseSaleAsync` (`POST /v1/passes/{serial}/sale/reverse`: take a
    refunded sale back).
  - *Checkout codes and shop connections:* `QuoteCheckoutCodeAsync`,
    `HoldCheckoutCodeAsync`, `CaptureCheckoutOrderAsync`,
    `ReleaseCheckoutOrderAsync`, `RefundCheckoutOrderAsync`,
    `ListOrderRedemptionsAsync`, `ListShopRedemptionsAsync`,
    `ReleaseShopRedemptionAsync`, `RefundShopRedemptionAsync`,
    `SetShopSettingsAsync`, `SetShopCeilingAsync`,
    `SetShopPluginAbilitiesAsync`, and for the card holder
    `HolderCheckoutCodesAsync`, `MintHolderCheckoutCodeAsync`,
    `CancelHolderCheckoutCodeAsync`.
  - `GetMetaAsync` (`GET /v1/meta`): the API's version.
- **`GetPassAsync`** now also returns `ProgramName`, `Currency`, `Stamps`
  (`Count`, `Max`), `Points`, `Money` (`AmountMinor`, `Currency`), `Customer`
  (with `customers.read`), `Actions` and `Sale`.
- **Webhooks.** `webhooks.manage` API keys manage webhooks
  (`CreateWebhookAsync`, `ListWebhooksAsync`, `GetWebhookAsync`,
  `SetWebhookStatusAsync`, `TestWebhookAsync`, `ListWebhookDeliveriesAsync`,
  `WebhookEventsAsync`); a webhook reports `CreatedByKey`.
- **Other fields.** `IssuePassAsync` returns `Created`; business lists and
  `Me` carry `Currency`; programs carry `Sale`; batches `OnlineValue`; shops
  `Accepts`, `Settings`, `ShopName`, `Unbacked` and the plugin key's
  `Abilities`.
- **Tests.** `ReadmeExamplesTests` runs the till example (sale, structured
  fields, refund).
- **README.**
  - A till example with `RecordSaleAsync`, the structured fields of
    `GetPassAsync` and a refund with `ReverseSaleAsync`.
  - `Idempotency-Key`: a key is unique for good per credential. The
    receipt number alone is not a key (fiscal receipt numbers restart after
    the Z report): use register + Z number + receipt number, or a UUID
    stored with the sale. The receipt number goes in `Reference`.
  - Test mode exists: `rwk_test_` keys and a test business.
  - How to set a custom base URL (staging), and a link to the developer
    docs, https://rewloy.com/gelistiriciler.

## 0.1.0 (etiketlenmedi / never tagged)

İlk önizleme. Rewloy API 1.0.0'a göre üretildi: 195 yol, 237 işlem.

First preview, generated from Rewloy API 1.0.0 (195 paths, 237 operations):

- **Package.** `Rewloy` for `net8.0` and `netstandard2.0` (.NET Framework 4.7.2
  and later, 4.8 above all). The only dependency, on `netstandard2.0` alone, is
  `System.Text.Json`.
- **Client.** `new RewloyClient(new RewloyClientOptions { ApiKey = … })`, or
  `StaffSession` with `Merchant`, or `HolderSession`, with `BaseUrl`,
  `Timeout`, `MaxRetries`, `HttpClient`, `UserAgent` and `Delay`.
- **Methods.** One async method per operation, named by its operationId with
  `Async` after it, with a class for each request body, query and answer.
  `…WithResponseAsync` returns the whole answer (`StatusCode`, `Headers`,
  `RequestId`, `Mode`, `Replayed`).
- **Retries** on network errors, timeouts, 429, 502-504 and Cloudflare's
  520-524, with exponential backoff, jitter and `Retry-After` (up to 60 s).
  Only safe requests are retried.
- **`Idempotency-Key`** for till actions and campaigns: generated when
  omitted, reused across retries; `409 IDEMPOTENCY_IN_PROGRESS` is waited out.
- **Paging** with `…AllAsync`, an `IAsyncEnumerable` over every page's items.
- **Server-sent events** with `LiveFeedAsync` and `HolderCardEventsAsync`
  (`EventStream`, an `IAsyncEnumerable`): reconnection with `Last-Event-ID`,
  an idle check, and the connection closes when the loop ends.
- **Webhooks:** `Webhook.Verify` and `Webhook.Sign`.
- **Errors:** `RewloyException`, `RateLimitException`,
  `RewloyConnectionException` and `RewloyTimeoutException`; `ErrorCode` holds
  every code and its title.
- **Deprecations:** `RewloyClient.Deprecated`, one event per deprecated
  operation, and `[Obsolete]` on the method or field.
- **Test mode:** `RewloyResponse.Mode` (the `Rewloy-Mode` header) and
  `IsTestMode`, and `EventStream.Mode`.
- **Regeneration:** `dotnet run --project tools/Rewloy.Generator`, plus a daily
  workflow that opens a pull request when the live document changes.
