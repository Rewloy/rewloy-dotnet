# Değişiklik günlüğü / Changelog

Bu kütüphanenin sürümleri. API'nin kendi değişiklikleri:
https://rewloy.com/gelistiriciler/degisiklikler

This library's releases. The API's own changes are listed at the link above.

## 0.1.0 (yayımlanmadı / unreleased)

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
