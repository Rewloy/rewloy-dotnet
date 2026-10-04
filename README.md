# Rewloy .NET

**Rewloy API'nin resmî .NET (C#) kütüphanesi.**

> **Durum: önizleme (0.x): yayımlanmadı; API kararlı, kütüphane arayüzü 1.0'a kadar değişebilir.**

[Rewloy](https://rewloy.com), işletmelerin dijital sadakat kartlarını
müşterinin telefonuna koyar. Kart türleri damga, puan, VIP, cashback, hediye
kartı, kupon ve indirimdir:
- iPhone'da Apple Cüzdan;
- Android'de Rewloy Cüzdan ve Google Cüzdan;
- her yerde web kartı.

Kasada QR okutulur; bakiye, ödül ve kampanyalar kartın kendisinde güncellenir.
Panelde yapılabilen her şey [Rewloy API v1](https://rewloy.com/gelistiriciler)
ile de yapılabilir; bu kütüphane onu .NET'ten kullanır. Türk ERP ve kasa
yazılımlarının çoğu .NET olduğu için .NET Framework 4.7.2 ve üstünü de
kapsar:

- **Tam tipli.** API'nin her işlemi, `operationId` adıyla bir `async`
  metottur (`getPass` → `GetPassAsync`). İstek gövdeleri, sorgular ve yanıtlar
  OpenAPI belgesinden ([`openapi.json`](https://app.rewloy.com/v1/openapi.json))
  üretilen sınıflardır; IntelliSense her alanı Türkçe açıklamasıyla gösterir.
  CI belgeyi her gün okur ve değişince yeniden üretir.
- **Bağımlılıksız.** `HttpClient` ve `System.Text.Json`; `net8.0` ve
  `netstandard2.0` (bu sonuncuda `System.Text.Json` paketi gelir).
- **Güvenli tekrar.** Geçici hatalarda ölçülü yeniden deneme; kasa işleminde
  ve kampanyada `Idempotency-Key`.
- **Ötesi:** `await foreach` ile sayfalama ve canlı akış (SSE), webhook imzası
  doğrulama, kullanımdan kalkma bildirimi, her çağrıda `CancellationToken`.

## Kurulum

NuGet'te yayımlanana kadar kaynaktan kurun (.NET SDK 8 ya da üstü ve git
gerekir). İki yol var:

```sh
git clone https://github.com/Rewloy/rewloy-dotnet
# 1) projenize doğrudan başvuru:
dotnet add reference rewloy-dotnet/src/Rewloy/Rewloy.csproj
# 2) ya da yerel bir NuGet kaynağına paketleyin:
dotnet pack rewloy-dotnet/src/Rewloy -c Release -o ./nupkgs
dotnet nuget add source ./nupkgs --name rewloy-yerel
dotnet add package Rewloy --version 0.1.0
```

Yayımlandığında: `dotnet add package Rewloy`.

**Hangi çerçeve?** Paket iki derleme taşır:
- `net8.0`: güncel .NET için;
- `netstandard2.0`: .NET Framework 4.7.2 ve üstü (4.8 en iyisi), .NET Core 2.x
  ve 3.x için. Eski ERP yığınları için bu var.

.NET Framework projeleri için:
- `<PackageReference>` kullanın. `packages.config` ile `System.Text.Json`'ın
  bağımlılıkları için bağlama yönlendirmesi (binding redirect) gerekir.
- `await foreach` için C# 8 ya da üstü gerekir: `<LangVersion>latest</LangVersion>`.
  Sayfalama ve akış dışındaki her şey C# 7.3'le de çalışır.
- API TLS 1.2 ister. 4.7 ve üstünde işletim sisteminin varsayılanı yeter; daha
  eski sürümlerde
  `ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12` yazın.

## Başlarken

```csharp
using Rewloy;

using var rewloy = new RewloyClient(new RewloyClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("REWLOY_API_KEY"),
});

var kart = await rewloy.GetPassAsync("ABCD-EFGH-JKLM");
Console.WriteLine($"{kart.Type} {kart.Balance} {kart.RewardReady}");
```

Her işlem, adı `operationId` olan bir metottur
([API referansı](https://rewloy.com/gelistiriciler/api)). Argümanları sırayla:
- adresteki parametreler (`{serial}`, `{id}`…; uuid ise `Guid`);
- varsa JSON gövde (`…Body` sınıfı) ve sorgu (`…Query` sınıfı);
- `RequestOptions`: `IdempotencyKey`, `Merchant` (`Rewloy-Merchant` başlığı),
  `Timeout`, `MaxRetries`, `Headers`; akışlarda ayrıca `Reconnect` ve
  `IdleTimeout`;
- `CancellationToken`.

Metot yanıttaki `data`yı döndürür. Sayfalı listelerde `Page<T>` (`Data` ve
`Meta`), gövdesiz yanıtta (`204`) `Task`, dosyada (QR, harita, CSV, `.pkpass`)
bir `RewloyFile` (`Content`, `ContentType`, `FileName`) döner.

Sınıflar `Rewloy.Models` ad alanındadır (`using Rewloy.Models;`). Düz
`get`/`set` özelliklerdir; zorunlu alanlar açıklamasında yazar, eksik bir alanı
API `VALIDATION` hatasıyla bildirir. API yeni alan eklerse, kütüphane yeniden
üretilmeden önce bile `AdditionalProperties` sözlüğünde durur ve oradan
gönderilebilir. Alanı boş bırakmakla `null` göndermeyi ayıran birkaç yerde
(örneğin `CorrectHolderProfileBody`) alanlar `Optional<T>`dir:
`Phone = "5321234567"` yazar, `Phone = Optional<string>.Null` alanı siler,
yazılmayan alan gönderilmez.

### Kimlik

| İstemci | Ne için |
|---|---|
| `new RewloyClientOptions { ApiKey = "rwk_…" }` | API anahtarı: kasa, e-ticaret, kendi sisteminiz |
| `new RewloyClientOptions { StaffSession = "rws_…", Merchant = isletmeId }` | ekip oturumu: bir kişinin işletme uygulaması |
| `new RewloyClientOptions { HolderSession = "rwh_…" }` | kart sahibi oturumu: Rewloy Cüzdan gibi müşteri uygulamaları |
| `new RewloyClient()` | kimlik istemeyen uç noktalar: giriş, katılım, kod |

`Merchant`, ekip oturumu birden fazla işletmede koltuk taşıyorsa hangi işletme
için çalıştığını söyler. Her çağrıda `RequestOptions.Merchant` ile
değiştirilebilir. Oturumlar kimliksiz bir istemciyle açılır:

```csharp
using var anonim = new RewloyClient();
var giris = await anonim.LoginAsync(new LoginBody { Email = email, Password = parola });

using var ekip = new RewloyClient(new RewloyClientOptions { StaffSession = giris.Token, Merchant = isletmeId });
if (giris.MfaRequired) await ekip.ProveMfaAsync(new ProveMfaBody { Code = "123456" });
```

Bir işlem istemcinin kimlik türünü kabul etmiyor ama kimliksiz de çalışıyorsa
(örneğin `LoginAsync`), istemci onu kimliksiz çağırır. API, işlemin kabul
etmediği bir kimliği reddeder (`CREDENTIAL_NOT_ALLOWED`). Kimlik hiçbir hata
iletisine ve `ToString()` çıktısına girmez.

Diğer seçenekler (`RewloyClientOptions`):
- `BaseUrl` (varsayılan `https://app.rewloy.com`);
- `Timeout` (60 sn, her deneme için);
- `MaxRetries` (2);
- `HttpClient`: kendi `HttpClient`iniz (aşağıda);
- `UserAgent`: gönderilen `User-Agent`a eklenir, örneğin `"KasaPOS/4.2"`;
- `Delay`: yeniden denemeler arasındaki bekleme (testler için).

**Bir kez oluşturun, paylaşın.** `RewloyClient` iş parçacıkları arasında
güvenlidir ve program boyunca yaşamak içindir. Kendi `HttpClient`inizi
verirseniz (`IHttpClientFactory`'den, vekil sunucuyla, test işleyicisiyle)
istemci onu atmaz; vermezseniz kendisi yönlendirme izlemeyen bir tane yapar ve
`Dispose` ile atar. Verdiğiniz `HttpClient`in kendi `Timeout`u (varsayılan 100
sn) ayrıca geçerlidir.

```csharp
// ASP.NET Core: services.AddHttpClient("rewloy");
var rewloy = new RewloyClient(new RewloyClientOptions
{
    ApiKey = yapilandirma["Rewloy:ApiKey"],
    HttpClient = httpClientFactory.CreateClient("rewloy"),
});
```

Kendi kodunuzu sınamak için aynı kapı kullanılır: `HttpClient`e kendi
`HttpMessageHandler`ınızı verin, ağa çıkmadan sahte yanıtlar döndürün
(bu deponun testleri böyle yazılmıştır).

## Kart vermek ve kasada işlem

```csharp
var kart = await rewloy.IssuePassAsync(new IssuePassBody
{
    ProgramId = programId,
    Email = "ayse@ornek.com",
    FirstName = "Ayşe",
    KvkkConsent = true,
});

var sonuc = await rewloy.PassActionAsync(
    kart.Serial,
    new PassActionBody { Action = "earn-stamps", LocationId = subeId, Count = 1 },
    new RequestOptions { IdempotencyKey = $"fis-{fisNo}" });
if (sonuc.Duplicate) Console.WriteLine("Bu fiş zaten işlenmiş");
```

`PassActionAsync` ve `SendCampaignAsync` bir `Idempotency-Key` ister. Verilmezse
kütüphane bir UUID üretir ve aynı çağrının her denemesinde aynısını gönderir.
Kasada fiş numarası gibi kendi anahtarınızı vermek daha iyidir: program çöküp
yeniden başlasa bile aynı fiş ikinci kez işlenmez, aynı anahtarla tekrar ilk
sonucu `Duplicate = true` ile döndürür.

## Sayfalama

```csharp
await foreach (var musteri in rewloy.ListCustomersAllAsync(new ListCustomersQuery { Consent = "yes", Limit = 200 }))
{
    Console.WriteLine($"{musteri.DisplayName} {musteri.Email}");
}
```

`…AllAsync` sayfalı her listeyi öğe öğe dolaşır ve son sayfada durur
(`CancellationToken` ve `break` de durdurur). Tek bir sayfa için metodun
kendisi yeter: `var sayfa = await rewloy.ListCustomersAsync(new ListCustomersQuery { Page = 2 });`
(`sayfa.Data`, `sayfa.Meta.Total`).

## Canlı akış

```csharp
using var cts = new CancellationTokenSource();
await foreach (var olay in rewloy.LiveFeedAsync(cancellationToken: cts.Token))
{
    if (olay.Event != "event") continue;
    var veri = olay.Json();
    Console.WriteLine($"{veri.GetProperty("kind")} {veri.GetProperty("delta")}");
}
```

`LiveFeedAsync` (işletmenin tezgâh akışı) ve `HolderCardEventsAsync` (kart
sahibinin kartındaki değişiklik) sunucu olayları (`text/event-stream`)
yayınlar. Dönen `EventStream` bir `IAsyncEnumerable<ServerSentEvent>`tır; her
olay `Event`, `Data` ve `Id` taşır, `Json()` `Data`yı ayrıştırır.

- **Yeniden bağlanma.** Bağlantı koparsa akış kendiliğinden yeniden bağlanır:
  sunucunun `retry:` süresi kadar bekler, bir olay `id` taşıdıysa
  `Last-Event-ID` gönderir. `RequestOptions.Reconnect = false` bunu kapatır.
- **Sessiz bağlantı.** API 25 saniyede bir `: hb` gönderir; 60 saniye hiç veri
  gelmezse bağlantı kopmuş sayılır (`RequestOptions.IdleTimeout`).
- **Durdurmak:** `CancellationToken`, döngüden `break` ya da `akis.Close()`.
  Üçü de akışı hatasız bitirir ve bağlantıyı kapatır.
- **Bitiren hatalar.** Yeniden bağlanmanın düzeltemeyeceği bir hata (`401`,
  `403`, `404`) akışı `RewloyException` ile bitirir.
- Akış bir kez dolaşılır. `akis.RequestId` ve `akis.Mode` bağlantının başlıklarını verir.

## Webhook doğrulama

Rewloy her teslimi imzalar:

```
Rewloy-Signature: t=<unix saniye>,v1=<hex HMAC-SHA256(sır, "<t>.<ham gövde>")>
```

`Webhook.Verify` imzayı **ham gövdeyle** ve webhook oluşturulurken bir kez
gösterilen sırla (`whsec_…`) doğrular:
- karşılaştırmayı sabit sürede yapar;
- `t` şimdiden 300 saniyeden (`toleranceSeconds`) uzaksa reddeder;
- gövdeyi ayrıştırılmış olarak döndürür (`WebhookEvent`).

Tutmazsa `WebhookSignatureException` atar (`Reason` nedenini söyler): 400 ile
yanıtlayın ve hiçbir işlem yapmayın. Gövde mutlaka ham olmalıdır: bir modele
ayrıştırılıp yeniden yazılan JSON imzayı tutturmaz.

ASP.NET Core:

```csharp
app.MapPost("/rewloy/webhook", async (HttpRequest istek) =>
{
    using var ham = new MemoryStream();
    await istek.Body.CopyToAsync(ham);
    WebhookEvent olay;
    try
    {
        olay = Webhook.Verify(ham.ToArray(), istek.Headers["Rewloy-Signature"], sir);
    }
    catch (WebhookSignatureException)
    {
        return Results.BadRequest();
    }
    // Rewloy-Delivery bir teslimin her denemesinde aynıdır: işlediyseniz atlayın.
    if (olay.Type == "pass.activity" && olay.PassData is { } veri)
    {
        Console.WriteLine($"{veri.Card} {veri.Kind} {veri.Delta}");
    }
    return Results.Ok();
});
```

.NET Framework'te (ASP.NET Web API ya da `HttpListener`) aynı iş: ham gövdeyi
`byte[]` ya da `string` olarak okuyun ve `Webhook.Verify`'a verin.

Başlıklar:
- `Rewloy-Event`: olay türü (`pass.issued`, `pass.activity`, `pass.voided`,
  `webhook.test`); gövdedeki `type` ile aynı (`olay.Type`).
- `Rewloy-Delivery`: teslimin kimliği. Teslim "en az bir kez"dir: çift gelen
  teslimi bununla ayıklayın.

Gövde kişinin iletişim bilgisini taşımaz; kişiyi `customer_id` ile API'den
okuyun. 2xx dışı bir yanıt yaklaşık 45 saat boyunca 8 kez yeniden denenir ve
her deneme yeni bir `t` ile imzalanır. Birden çok sır (webhook değiştirirken)
için `Webhook.Verify(gövde, başlık, new[] { eskiSir, yeniSir })`. Kendi
işleyicinizi test etmek için `Webhook.Sign(gövde, sir)` aynı başlığı üretir.

## Hatalar ve yeniden deneme

```csharp
try
{
    await rewloy.PassActionAsync(serial, new PassActionBody { Action = "spend", LocationId = subeId, AmountMinor = 5000 },
        new RequestOptions { IdempotencyKey = $"fis-{fisNo}" });
}
catch (RateLimitException ex)
{
    Console.WriteLine($"{ex.RetryAfter} sonra yeniden deneyin");
}
catch (RewloyException ex) when (ex.Code == ErrorCode.InsufficientBalance)
{
    Console.WriteLine(ex.Detail);
}
```

`RewloyException` şunları taşır:
- `Status`: HTTP durumu;
- `Code`: API'nin sabit kodu ([hata kodları](https://rewloy.com/gelistiriciler/hatalar);
  hepsi `ErrorCode` sabitidir); kodunuz buna göre davranmalı;
- `Title`: kodun katalogdaki başlığı;
- `Detail`: API'nin açıklaması (Türkçe, değişebilir);
- `Details`: varsa ayrıntı (`JsonElement?`); doğrulama hatasında
  `[{ field, rule, message }]`;
- `RequestId`: `x-request-id`; destek talebinde bunu verin;
- `Body`, `Headers`, `Docs` ve `Operation`.

Alt sınıflar:
- `RateLimitException`: `429`; `RetryAfter` (`TimeSpan?`);
- `RewloyConnectionException`: yanıt gelmedi (`Status` 0, `Code`
  `CONNECTION_ERROR`);
- `RewloyTimeoutException`: zaman aşımı (`TIMEOUT`); bir önceki sınıfın alt
  sınıfıdır.

Rewloy'un olmayan bir hata gövdesi (örneğin bir vekil sunucunun 502 sayfası)
`HTTP_502` gibi bir kodla gelir. Bir `CancellationToken` iptali hiçbir zaman
`RewloyException` olmaz: her zamanki `OperationCanceledException` gelir.

**Yeniden deneme.** Şunlar en çok `MaxRetries` kez (varsayılan 2) yeniden
denenir: bağlantı hatası, zaman aşımı, `429`, `502`, `503`, `504` ve
Cloudflare'in `520`–`524` hataları.
- **Bekleme:** üstel ve rastgele (0,5 sn, 1 sn, 2 sn… en çok 8 sn); yanıt
  `Retry-After` taşıyorsa o kadar. `Retry-After` 60 saniyeden uzunsa
  beklenmez, hata size gelir.
- **Yalnız tekrarı güvenli istekler:** `GET`, `PUT`, `DELETE` ve
  `Idempotency-Key` taşıyan `POST`. İlk istek hâlâ işlenirken gelen
  `409 IDEMPOTENCY_IN_PROGRESS` de beklenip yeniden denenir. Diğer `POST` ve
  `PATCH` istekleri hiç tekrar edilmez.
- **Süre:** her deneme `Timeout` (varsayılan 60 sn) içinde bitmelidir.

## Kullanımdan kalkma

Kalkacak bir uç nokta en az 180 gün önceden duyurulur. O süre boyunca her
yanıtı `Deprecation`, `Sunset` ve `Link` başlıklarını taşır.

- **Bildirim.** Kütüphane her işlem için süreç başına bir kez
  `RewloyClient.Deprecated` olayını yükseltir ve `Trace.TraceWarning` yazar.
  Bildirim işlemi, `Sunset` tarihini ve değişiklik günlüğündeki kaydı söyler.
- **Derleyici.** O metot (ya da alan) `[Obsolete]` olarak işaretlenir:
  derleyici uyarı verir. Bugün bunun örneği, 5 Nisan 2027'de kalkacak olan
  `email` ve `verifiedPhones` alanlarıdır (yerlerine `identifiers`).
- **Yönetmek.** Bağımlılık eklememek için `ILogger` doğrudan kullanılmaz; olayı
  kendi kayıtlarınıza bağlayın:

```csharp
RewloyClient.Deprecated += (_, e) => logger.LogWarning("{Message}", e.Message);
```

## Yanıtın tamamı ve test modu

```csharp
var yanit = await rewloy.SendCampaignWithResponseAsync(
    new SendCampaignBody { Body = "Bu hafta kahveler 2 damga!" },
    new RequestOptions { IdempotencyKey = "kampanya-2026-10-03" });
yanit.StatusCode;  // 201
yanit.Replayed;    // true: aynı anahtarın ilk yanıtı yeniden döndü (Idempotent-Replayed)
yanit.RequestId;   // x-request-id
yanit.Mode;        // Rewloy-Mode
yanit.Data;        // kampanya
```

Her metodun bir `…WithResponseAsync` ikizi vardır; `Data`ya ek olarak sayfalı
listede `Meta`, `StatusCode`, `Headers`, `RequestId`, `Mode` ve `Replayed`
döner.

`Mode`, yanıtın `Rewloy-Mode` başlığıdır. Platformda test modu: gerçek mesaj
göndermeyen, gerçek kart vermeyen test anahtarları (`rwk_test_…`). Onlarla
yapılan her çağrının yanıtı `Rewloy-Mode: test` taşır; `IsTestMode` bunu
söyler. Başlık yoksa `Mode` `null`dır. Canlı akışta aynı bilgi
`akis.Mode`dadır.

İşlem tablosu da dışa açıktır: `RewloyOperations.PassAction` →
`Method`, `Path`, `Credentials`, `AcceptsMerchant`, `Idempotency`, `IsPaged`…

## Geliştirme

.NET SDK 10 gerekir (testler `net10.0` ve `net8.0`'da çalışır; kütüphane
`net8.0` ve `netstandard2.0` derler).

```sh
dotnet test                                                    # ağsız: API sahte bir HttpMessageHandler
dotnet test -p:RewloyAsset=netstandard2.0                      # aynı testler, netstandard2.0 derlemesiyle
dotnet run --project tools/Rewloy.Generator                    # canlı belgeden: openapi/openapi.json ve src/Rewloy/Generated/
dotnet run --project tools/Rewloy.Generator -- --file openapi/openapi.json   # kayıtlı belgeden
dotnet pack src/Rewloy -c Release                              # NuGet paketi (yayımlamaz)
```

- `src/Rewloy/Generated/` elle düzenlenmez; üreteç `tools/Rewloy.Generator`dır
  (C#, yalnız .NET SDK ister).
- Testler ağa çıkmaz. Üretimin belirleyici olduğunu ve işlenmiş çıktının
  güncel olduğunu da sınarlar.
- CI her gün canlı belgeyi okur ve bir değişiklik varsa bir pull request açar.
- Kararlar: [docs/DECISIONS.md](docs/DECISIONS.md).

## Belgeler

| | |
|---|---|
| Başlarken | https://rewloy.com/gelistiriciler |
| API referansı | https://rewloy.com/gelistiriciler/api |
| OpenAPI 3.1 | https://app.rewloy.com/v1/openapi.json |
| Hata kodları | https://rewloy.com/gelistiriciler/hatalar |
| API'nin değişiklik günlüğü | https://rewloy.com/gelistiriciler/degisiklikler |
| Bu kütüphanenin değişiklikleri | [CHANGELOG.md](CHANGELOG.md) |

**Sürümler:**
- Kütüphane anlamsal sürümleme ([SemVer](https://semver.org)) kullanır. 1.0'a
  kadar arayüzü değişebilir.
- API'ye alan eklemek geriye uyumludur; kütüphanenin sınıfları her gün
  güncellenir.
- Kalkacak bir uç nokta en az 180 gün önce duyurulur ve bu süre boyunca
  `Deprecation` ve `Sunset` başlıklarını taşır.

## Güvenlik

Bir güvenlik açığı bulursanız [SECURITY.md](SECURITY.md) dosyasındaki yoldan
özel olarak bildirin. Lütfen herkese açık issue açmayın.

## Lisans

[MIT](LICENSE)

---

## English

**The official .NET (C#) library for the Rewloy API.**

> **Status: preview (0.x), not published yet. The API is stable; the
> library's interface may change until 1.0.**

The documentation of the API itself is in Turkish (links above). In short:

- Every operation of the API is an `async` method named by its `operationId`
  (`getPass` becomes `GetPassAsync`), with generated classes for request
  bodies, queries and answers, from the OpenAPI document that CI reads daily
  and regenerates from.
- No dependencies beyond the BCL: `HttpClient` and `System.Text.Json` (a
  package on `netstandard2.0`).
- Targets `net8.0` and `netstandard2.0`: .NET Framework 4.7.2 and later (4.8
  above all), where most Turkish ERP and POS software runs, and .NET Core 2.x
  and 3.x.
- Safe retries, `Idempotency-Key` handling, `await foreach` paging and
  server-sent events, webhook signature verification, deprecation events, and
  a `CancellationToken` on every call.

### Install

Until it is on NuGet, install it from source (.NET SDK 8 or later, and git):

```sh
git clone https://github.com/Rewloy/rewloy-dotnet
dotnet add reference rewloy-dotnet/src/Rewloy/Rewloy.csproj      # a project reference, or:
dotnet pack rewloy-dotnet/src/Rewloy -c Release -o ./nupkgs      # a package in a local source
dotnet nuget add source ./nupkgs --name rewloy-local
dotnet add package Rewloy --version 0.1.0
```

On .NET Framework use `<PackageReference>` (with `packages.config`, `System.Text.Json`
needs binding redirects) and `<LangVersion>latest</LangVersion>` for `await foreach`.
TLS 1.2 is required: the default from 4.7 up; on older versions set it in `ServicePointManager`.

### Use

```csharp
using var rewloy = new RewloyClient(new RewloyClientOptions { ApiKey = apiKey });   // or StaffSession + Merchant, or HolderSession

var kart = await rewloy.IssuePassAsync(new IssuePassBody { ProgramId = programId, Email = email, KvkkConsent = true });
var result = await rewloy.PassActionAsync(
    kart.Serial,
    new PassActionBody { Action = "earn-stamps", LocationId = locationId },
    new RequestOptions { IdempotencyKey = $"receipt-{receiptNo}" });   // generated when omitted, reused across retries
```

- **Arguments.** Path parameters (a `Guid` for a uuid), then the body and the
  query the operation takes, then `RequestOptions` (`IdempotencyKey`,
  `Merchant`, `Timeout`, `MaxRetries`, `Headers`) and a `CancellationToken`.
- **Results.** A method gives the answer's `data`: `Page<T>` for paged lists,
  `Task` for 204, a `RewloyFile` for files.
- **The whole answer.** Every method has a `…WithResponseAsync` twin that
  returns `StatusCode`, `Headers`, `RequestId`, `Mode` (the `Rewloy-Mode`
  header, `IsTestMode` for test keys) and `Replayed` (`Idempotent-Replayed`)
  besides the data.
- **Models.** The classes are plain get/set properties in `Rewloy.Models`, so
  they compile in any C# version. Fields the API adds before the next
  regeneration are kept in `AdditionalProperties`. `Optional<T>` tells a
  field left out from an explicit `null` where the API needs both.
- **Pagination.** `rewloy.ListCustomersAllAsync(query)` is an
  `IAsyncEnumerable` over the items of every page.
- **Streams.** `rewloy.LiveFeedAsync(cancellationToken: ct)` (or
  `HolderCardEventsAsync`) is an `IAsyncEnumerable<ServerSentEvent>`
  (`Event`, `Data`, `Id`). It reconnects with `Last-Event-ID` unless
  `RequestOptions.Reconnect` is `false`, and ends quietly on cancellation,
  `break` or `Close()`.
- **One client.** `RewloyClient` is thread-safe: make one and share it. Pass
  your own `HttpClient` (from `IHttpClientFactory`, or with a stub
  `HttpMessageHandler` to test your code) and it is not disposed.

### Webhooks

Verify the **raw** body with the secret shown when the webhook was created:

```csharp
var ev = Webhook.Verify(rawBody, request.Headers["Rewloy-Signature"], secret);   // string or byte[]
```

- **Check.** `Rewloy-Signature: t=<unix seconds>,v1=<hex HMAC-SHA256(secret,
  "<t>.<raw body>")>` is compared in constant time, and `t` must be within
  300 seconds.
- **Refusal.** On failure it throws `WebhookSignatureException` (`Reason` says
  why): answer 400.
- **Headers.** `Rewloy-Event` is the event type (`ev.Type`). `Rewloy-Delivery`
  is the same on every retry of a delivery: deduplicate on it. Delivery is at
  least once. `Webhook.Sign` makes the header for testing your own handler.

### Errors, retries, deprecations

- **Errors.** Failures throw `RewloyException` with `Status`, `Code` (the
  API's stable code; the constants are in `ErrorCode`), `Title`, `Detail`,
  `Details`, `RequestId` and `Body`. Subclasses: `RateLimitException`
  (`RetryAfter`), `RewloyConnectionException` and `RewloyTimeoutException`.
  A cancelled `CancellationToken` is an ordinary `OperationCanceledException`.
- **What is retried.** Network errors, timeouts, 429, 502-504 and
  Cloudflare's 520-524, up to `MaxRetries` (default 2), with exponential
  backoff and jitter, honouring `Retry-After` (up to 60 seconds).
- **Only when safe.** Only GET, PUT, DELETE, and POST with an
  `Idempotency-Key`, are retried.
- **Deprecations.** A deprecated operation's answers carry `Deprecation`,
  `Sunset` and `Link`. The client raises `RewloyClient.Deprecated` once per
  operation per process (hook it to your logger) and the generated method or
  field is `[Obsolete]`.

### Develop

```sh
dotnet test                                    # no network: the API is a stub HttpMessageHandler
dotnet test -p:RewloyAsset=netstandard2.0      # the same tests on the netstandard2.0 build
dotnet run --project tools/Rewloy.Generator    # regenerate from the live document
```

### Security and licence

Report vulnerabilities privately, as [SECURITY.md](SECURITY.md) says.
[MIT](LICENSE) licensed.
