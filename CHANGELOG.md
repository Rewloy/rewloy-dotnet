# Değişiklik günlüğü / Changelog

Bu kütüphanenin sürümleri. API'nin kendi değişiklikleri:
https://rewloy.com/gelistiriciler/degisiklikler

This library's releases. The API's own changes are listed at the link above.

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
