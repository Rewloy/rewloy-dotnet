# Değişiklik günlüğü / Changelog

Bu kütüphanenin sürümleri. API'nin kendi değişiklikleri:
https://rewloy.com/gelistiriciler/degisiklikler

This library's releases. The API's own changes are listed at the link above.

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
