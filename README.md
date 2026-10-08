# Rewloy .NET

English: [below](#english).

**Rewloy API'nin resmî .NET (C#) kütüphanesi.**

> **Durum: önizleme (0.x), 0.2.3'ten beri nuget.org'da; 0.3.0 Rewloy API 1.3.2'yi izler. API kararlı; kütüphane arayüzü 1.0'a kadar değişebilir.**

[Rewloy](https://rewloy.com), işletmelerin dijital sadakat kartlarını
müşterinin telefonuna koyar. Kart türleri damga, puan, VIP, cashback, hediye
kartı, kupon ve indirimdir:
- iPhone'da Apple Cüzdan;
- Android'de Rewloy Cüzdan ve Google Cüzdan;
- her yerde web kartı.

Kasada QR okutulur; bakiye, ödül ve kampanyalar kartın kendisinde güncellenir.
Panelde yapılabilen her şey [Rewloy API v1](https://rewloy.com/gelistiriciler)
ile de yapılabilir (geliştirici belgeleri: **https://rewloy.com/gelistiriciler**);
bu kütüphane onu .NET'ten kullanır. Türk ERP ve kasa
yazılımlarının çoğu .NET olduğu için .NET Framework 4.7.2 ve üstünü de
kapsar:

- **Tam tipli.** API'nin her işlemi, `operationId` adıyla bir `async`
  metottur (`getPass` → `GetPassAsync`). İstek gövdeleri, sorgular ve yanıtlar
  OpenAPI belgesinden ([`openapi.json`](https://app.rewloy.com/v1/openapi.json))
  üretilen sınıflardır; IntelliSense her alanı Türkçe açıklamasıyla gösterir.
  CI belgeyi her gün okur ve değişince yeniden üretir.
- **Bağımlılıksız.** `HttpClient` ve `System.Text.Json`; `net8.0` ve
  `netstandard2.0` (bu sonuncuda `System.Text.Json` paketi gelir).
- **Güvenli tekrar.** Geçici hatalarda ölçülü yeniden deneme; satışta, kasa
  işleminde ve kampanyada `Idempotency-Key`.
- **Ötesi:** `await foreach` ile sayfalama ve canlı akış (SSE), webhook imzası
  doğrulama, kullanımdan kalkma bildirimi, her çağrıda `CancellationToken`.

## Kurulum

Paket [nuget.org'da](https://www.nuget.org/packages/Rewloy):

```sh
dotnet add package Rewloy
```

Visual Studio'da: Paket Yöneticisi Konsolu'nda `Install-Package Rewloy`.

Ya da kaynaktan derleyin (.NET SDK 8 ya da üstü ve git gerekir). İki yol var:

```sh
git clone https://github.com/Rewloy/rewloy-dotnet
# 1) projenize doğrudan başvuru:
dotnet add reference rewloy-dotnet/src/Rewloy/Rewloy.csproj
# 2) ya da yerel bir NuGet kaynağına paketleyin:
dotnet pack rewloy-dotnet/src/Rewloy -c Release -o ./nupkgs -p:PackageVersion=0.0.0-local
dotnet nuget add source ./nupkgs --name rewloy-yerel
dotnet add package Rewloy --version 0.0.0-local   # nuget.org'daki sürümle karışmaz
```

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
// "Şimdi ne yapılabilir?" için `Actions[].Ready` okunur; `RewardReady` yalnız damga ve puanda "ödül hazır"dır.
var odul = kart.Actions.Any(a => (a.Action == "redeem-stamps" || a.Action == "redeem-reward") && a.Ready);
Console.WriteLine($"{kart.Type} {kart.Balance} {odul}");
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
- `BaseUrl` (varsayılan `https://app.rewloy.com`; sonuna `/v1` eklemeniz ya da eklememeniz fark etmez: `https://app.rewloy.com/v1` de olur, kütüphane `/v1`i kendisi ekler);
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

### Başka bir adres (staging)

API'nin başka bir kopyasına (kendi staging ortamınız ya da bir vekil sunucu)
`BaseUrl` ile bağlanılır:

```csharp
using var rewloy = new RewloyClient(new RewloyClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("REWLOY_API_KEY"),
    BaseUrl = "https://rewloy-staging.ornek.com",   // sonuna /v1 yazsanız da olur
});
```

Gerçek müşterilere dokunmadan denemek için adres değiştirmeniz gerekmez:
[test modu](#test-modu) aynı adreste, ayrı bir test ortamıyla çalışır.

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
    new RequestOptions { IdempotencyKey = $"kasa3-z0187-fis{fisNo}" });   // aşağıya bakın
if (sonuc.Duplicate) Console.WriteLine("Bu işlem zaten yazılmış");
```

### Satış: `RecordSaleAsync`

Kasa ya da kendi yazılımınız için en kolay yol `RecordSaleAsync`tir: "bu satış
oldu, sen yaz". Ödenen toplamı (kartın para biriminde, kuruş) gönderirsiniz;
ne yazılacağına kartın türü ve programın kendi kuralı karar verir. Kartın
türünü bilmeniz gerekmez.

```csharp
var kart = await rewloy.GetPassAsync(seri);
// Kartın türüne özgü alanlar; `Balance` yerine bunları okuyun.
if (kart.Stamps != null) Console.WriteLine($"{kart.Stamps.Count} / {kart.Stamps.Max} damga");
if (kart.Points != null) Console.WriteLine($"{kart.Points} puan");
if (kart.Money != null) Console.WriteLine($"{kart.Money.AmountMinor / 100m} {kart.Money.Currency}");
Console.WriteLine($"{kart.ProgramName} {kart.Customer?.Name}");   // Customer: yalnız customers.read yetkisiyle

// Fiş numarası anahtar olamaz: kasa + Z no + fiş no, ya da satışla saklanan bir UUID.
var anahtar = $"kasa3-z0187-fis{fisNo}";
var satis = await rewloy.RecordSaleAsync(
    seri,
    new RecordSaleBody
    {
        LocationId = subeId,
        AmountMinor = 4550,          // 45,50: kartın para biriminde (kart.Currency), kuruş
        Currency = kart.Currency,    // isteğe bağlı güvence: uyuşmazsa 422 CURRENCY_MISMATCH
        Reference = $"fis-{fisNo}",  // fiş numarası buraya yazılır
    },
    new RequestOptions { IdempotencyKey = anahtar });
if (satis.Applied == "none") Console.WriteLine($"Yazılan bir şey yok: {satis.Reason}");
else Console.WriteLine($"{satis.Credited} {satis.Applied} yazıldı, bakiye {satis.Balance}");
// Fişi çizmek için ayrıca okumanız gerekmez: yazımdan sonraki kart `satis.Card`'dadır (yetki yoksa null).
if (satis.Card != null && satis.Card.Actions.Any(a => (a.Action == "redeem-stamps" || a.Action == "redeem-reward") && a.Ready))
    Console.WriteLine("Ödül hazır");
```

`Actions[].Ready`, kartın kendi durumuna göre işlemin şimdi yapılıp
yapılamayacağıdır (damga ödülü hazır mı, puan bir ödüle yetiyor mu, bakiye var
mı, kupon kullanılmamış mı, VIP ziyareti bu pencerede sayılmış mı). `RewardReady`
aynen kalır ama türe göre anlam değiştirir: damga ve puanda "ödül hazır";
cashback ve hediye kartında bakiye sıfırdan büyükse; **VIP'te her zaman
`true`**. Kasa ekranında "Ödül hazır" yazısını yalnız damga ve puanda gösterin.

`GET /v1/passes/{serial}` ayrıca `Actions` (kartın aldığı kasa işlemleri ve
şimdi yapılıp yapılamayacakları) ve `Sale` (bir satışın bu kartta ne
yazacağı) alanlarını verir.

**İade.** `ReverseSaleAsync` bir satışın karta yazdığını geri alır; satışı
yazarken gönderdiğiniz anahtarla (`SaleKey`) ya da `Reference`la bulur:

```csharp
var geri = await rewloy.ReverseSaleAsync(seri, new ReverseSaleBody { SaleKey = anahtar, LocationId = subeId });
Console.WriteLine($"{geri.Reversed} {geri.Applied} geri alındı, bakiye {geri.Balance}");
```

Bir satış bir kez geri alınır (tekrar `Duplicate = true` döner). Kazanılan
kullanılmışsa (ödüle ya da harcamaya gitmişse) `409 SALE_ALREADY_SPENT` gelir ve
hiçbir şey yazılmaz.

**Çevrimdışı kasa kuyruğu: `OccurredAt`.** Bağlantı koptuğunda satışı sonra
yazıyorsanız `OccurredAt` ile satışın gerçekten olduğu anı (`DateTimeOffset`,
saat dilimiyle) gönderin; kartın geçmişinde o anla görünür. Gelecekte olamaz (2
dakikalık saat farkı kabul edilir). `IdempotencyKey` kuyruktaki kayıtla birlikte
saklanır, tekrar gönderilince satış ikinci kez yazılmaz.

```csharp
await rewloy.RecordSaleAsync(
    seri,
    new RecordSaleBody { LocationId = subeId, AmountMinor = 4550, Reference = $"fis-{fisNo}", OccurredAt = DateTimeOffset.Parse("2026-10-05T14:32:10+03:00") },
    new RequestOptions { IdempotencyKey = anahtar });
```

**Kasa işlemini iptal etmek: `ReverseActionAsync`.** `PassActionAsync` ile
yapılan bir harcama, ödül ya da kullanım yanlışlıkla yapıldıysa (`spend`,
`spend-points`, `redeem-stamps`, `redeem-reward`, `use`) `ReverseActionAsync`
tamamını geri verir. İşlemi, yaparken gönderdiğiniz `Idempotency-Key`
(`ActionKey`) ya da işlemin `Reference` değeriyle bulur (`PassActionBody` artık
isteğe bağlı bir `Reference` alır). `ReverseActionAsync` bir `Idempotency-Key`
**istemez**: bir işlem bir kez geri alınır, tekrar `Duplicate = true` döner.

```csharp
await rewloy.PassActionAsync(
    seri,
    new PassActionBody { Action = "spend", LocationId = subeId, AmountMinor = 2500 },
    new RequestOptions { IdempotencyKey = $"kasa3-z0187-iptal{fisNo}" });
var iptal = await rewloy.ReverseActionAsync(seri, new ReverseActionBody { ActionKey = $"kasa3-z0187-iptal{fisNo}", LocationId = subeId });   // ya da Reference = $"fis-{fisNo}"
Console.WriteLine($"{iptal.Undone} {iptal.Restored} geri verildi, bakiye {iptal.Balance}");
```

`PassActionAsync`in yanıtı kart türüne göre iki biçimdedir ve `PassActionData`
tek bir sınıftır: bakiyeli kartlarda `Balance` (damga, puan, VIP, cashback,
hediye kartı), kupon ve indirim kartında `Status`, `Uses` ve `UsesLeft`
(`sonuc.Uses != null` ile ayırın; öteki biçimin alanları `null`dır; her
alanın belgesi hangi biçimde geldiğini söyler). Kazanımlar (`earn-stamps`,
`earn-points`, `visit`) `ReverseActionAsync`le değil `ReverseSaleAsync`le geri
alınır.

**Yazımın yanıtında kartın durumu: `Card`.** `RecordSaleAsync`, `PassActionAsync`,
`ReverseSaleAsync` ve `ReverseActionAsync` yanıtları `Card` taşır: yazımdan sonraki
kart, `GetPassAsync`'in `Customer` hariç aynı alanlarıyla (`ProgramName`,
`Currency`, `Stamps`/`Points`/`Money`, `Actions`…). Yazımla aynı işlemde okunur,
yanıtın `Balance`'ıyla aynı anı söyler. **Tekrarda** (`Duplicate == true`) kartın
**şimdiki** durumudur. Kimliğin kartın programında `passes.read` yetkisi yoksa
(yalnız kasa yetkisi olan bir eklenti anahtarı) `Card` `null`dır.
`RecordSaleData.Reversed == true`, bu anahtarla yazılan satışın sonradan geri
alındığını söyler (yalnız bir tekrarda olabilir; `Credited` ilk isteğin
yazdığıdır, kart onu artık taşımaz): fişi yeniden yazmak için yeni bir anahtar
gönderin.

**Kartın işlemleri: `ListPassOperationsAsync`.** Kartın defterindeki işlemler,
yeniden eskiye, sayfalı (`ListPassOperationsAllAsync(seri)` ile `await foreach`):
bir kasa ekranındaki "son işlemler" listesi ve her birinin İade düğmesi için;
kasanın kendi anahtar günlüğünü tutması gerekmez. Her işlemde `UndoWith` hangi
uç noktanın geri aldığını (`"sale/reverse"` ya da `"actions/reverse"`),
`Reversible` bu kimliğin şimdi geri alıp alamayacağını söyler; bu kimliğin kendi
işlemlerinde `SaleKey` ya da `ActionKey` de gelir.

```csharp
await foreach (var islem in rewloy.ListPassOperationsAllAsync(seri))
{
    if (!islem.Reversible) continue;
    if (islem.UndoWith == "sale/reverse") await rewloy.ReverseSaleAsync(seri, new ReverseSaleBody { SaleKey = islem.SaleKey });
    else await rewloy.ReverseActionAsync(seri, new ReverseActionBody { ActionKey = islem.ActionKey });
}
```

**`OccurredAt` reddedilirse** `400 VALIDATION` gelir ve
`ex.Details[0].reason` nedeni söyler: `in_future`, `too_old` (72 saatten eski),
`before_issue` (kart o anda yoktu: `OccurredAt` olmadan yeniden gönderin),
`invalid`. Tanımadığınız bir `reason`'ı `invalid` gibi ele alın.

### Kazanım kuralları ve fiş satırları (API 1.3.0)

Fişin satırlarını (`Lines`) gönderirseniz program neyin kazandırdığına ürün
gruplarıyla karar verebilir: "kahvenin her adedine 1 damga". Satır
göndermeyen kasa bugünkü gibi çalışır; kuralı olmayan program da öyle.

```csharp
// 1) İşletmenin ürün grubu (bir kez tanımlanır, her program kullanır).
var grup = await rewloy.CreateEarnGroupAsync(new CreateEarnGroupBody
{
    Name = "Kahveler",
    Members = new[] { new CreateEarnGroupBodyMembersItem { Effect = "include", Match = "sku", Value = "KAHVE" } },
});

// 2) Programın kuralları. Revision, okuduğunuz sürümdür (hiç kaydedilmediyse 0); arada
//    başkası kaydettiyse 409 REVISION_CONFLICT gelir: yeniden okuyup değişikliğinizi onun üstüne yapın.
var kurallar = await rewloy.GetEarnRulesAsync(programId);
await rewloy.PutEarnRulesAsync(programId, new PutEarnRulesBody
{
    Revision = (int)kurallar.Revision,
    Rules = new[] { new PutEarnRulesBodyRulesItem { Kind = "stamp.perUnit", GroupId = grup.Id, Stamps = 1 } },
});

// 3) Satırlı satış. Yanıtın Earn alanı satır satır "neden" der.
var fis = await rewloy.RecordSaleAsync(
    seri,
    new RecordSaleBody
    {
        LocationId = subeId,
        AmountMinor = 16000,
        Lines = new[]
        {
            new RecordSaleBodyLinesItem { LineId = "1", Name = "Filtre kahve", Sku = "KAHVE", Quantity = JsonSerializer.SerializeToElement(2), UnitPriceMinor = 6000 },
            new RecordSaleBodyLinesItem { LineId = "2", Name = "Kek", UnitPriceMinor = 4000 },
        },
    },
    new RequestOptions { IdempotencyKey = anahtar });
foreach (var satir in fis.Earn!.Lines) Console.WriteLine($"{satir.LineId}: {satir.Status}, {satir.Earned} {fis.Earn.Unit}");
foreach (var kural in fis.Earn.Rules) Console.WriteLine(kural.Text);   // "Kahve başına 1 damga"

// 4) Satır iadesi: yalnız bir kahve geri alınır; satış satıldığı günün kurallarıyla yeniden yargılanır.
//    Satır iadesi bir Idempotency-Key ister (bir iade, bir anahtar).
var iade = await rewloy.ReverseSaleAsync(
    seri,
    new ReverseSaleBody { SaleKey = anahtar, Lines = new[] { new ReverseSaleBodyLinesItem { LineId = "1", Quantity = JsonSerializer.SerializeToElement(1) } } },
    new RequestOptions { IdempotencyKey = $"{anahtar}-iade-1" });
Console.WriteLine($"{iade.Reversed} geri alındı; kalan satırlar: {iade.LinesLeft!.Count}");
```

Hiçbir şey yazmadan sormak için: `PreviewSaleAsync(seri, …)` bir kartın satışta
ne yazacağını söyler (`RecordSaleAsync`in gövdesi ve yanıtı, `Preview = true`; `Idempotency-Key`
gerekmez), `PreviewEarnAsync(programId, …)` kart olmadan bir fişin ne kazandıracağını
açıklar ve kaydedilmemiş bir kural taslağını (`RuleSet`) deneyebilir. Kasa kapanmadan
"Bu fiş 2 damga kazandırır" demek ya da bir mağazada "76 puan kazanırsınız" yazmak için.
Başka yeni işlemler: ürün grupları (`ListEarnGroupsAsync`, `UpdateEarnGroupAsync`…),
fişlerden görülen kategoriler (`ListSeenLinesAsync`), başlangıç şablonları
(`ListEarnTemplatesAsync`), kural sürümleri (`ListEarnRuleRevisionsAsync`).

### Şube QR'ı, dondurma ve kopya (API 1.3.0)

Her şubenin bir QR'ı vardır; okutan, şubenin sayfasını ve o şubede geçerli
kartları görür.

```csharp
var sube = await rewloy.GetLocationAsync(subeId);
Console.WriteLine($"{sube.Qr.Url} ({sube.Qr.State})");

using var herkes = new RewloyClient(new RewloyClientOptions());                   // kimlik gerekmez
var sayfa = await herkes.PublicBranchAsync(sube.Qr.Code);
Console.WriteLine($"{sayfa.Business.Name} · {sayfa.Branch.Name}: {sayfa.Branch.State}, {sayfa.Items.Count} kart");

RewloyFile png = await rewloy.LocationQrPngAsync(subeId, new LocationQrPngQuery { Size = 1024 });
File.WriteAllBytes("sube-qr.png", png.Content);
RewloyFile afis = await rewloy.LocationQrSheetPdfAsync(subeId);                    // A4 afiş
```

Şube dondurma (`FreezeLocationAsync`, `UnfreezeLocationAsync`,
`UpdateLocationFreezeAsync`, `CancelLocationFreezeAsync`, `ListLocationFreezesAsync`)
yalnız ekip oturumu ve kişinin şifresiyle çalışır; API anahtarıyla
`403 CREDENTIAL_NOT_ALLOWED` gelir. Donuk şubenin kasası `409 LOCATION_FROZEN`
verir, bütün şubeler donukken işletme duraklar (`409 BUSINESS_FROZEN`).
`CopyProgramAsync` bir hediye kartı, kupon ya da indirim kartının başka bir
değerle kopyasını yapar; sadakat kartı `422 NOT_AN_INSTRUMENT` alır.
`ProgramJoinQrAsync(id, new ProgramJoinQrQuery { BranchCode = …, Format = "png" })`
programın katılım QR'ını bir şube için verir.

### `Idempotency-Key`

`RecordSaleAsync`, `PassActionAsync`, `SendCampaignAsync` ve
`RefundShopRedemptionAsync` bir `Idempotency-Key` **ister**: API'nin tanımında
(OpenAPI) bu başlık bu işlemlerde zorunludur, bu yüzden
`RequestOptions.IdempotencyKey` bu metotlarda zorunludur. Verilmezse kütüphane
istek göndermeden `ArgumentException` fırlatır; **sizin yerinize anahtar
üretmez**. Üretilmiş rastgele bir anahtar yalnızca tek çağrının yeniden
denemelerini korurdu: program çöküp yeniden başlarsa yeni bir anahtar çıkar ve
satış ikinci kez yazılabilirdi. Anahtarı kendiniz üretip satışla birlikte
saklayın. Anahtar 8–64 karakterlik görünür ASCII olmalıdır (0x21–0x7E: harf,
rakam ve noktalama; boşluk, Türkçe harf ya da `fiş` gibi ASCII dışı karakter
olmaz); aksi halde kütüphane yine istek göndermeden `ArgumentException`
fırlatır. Başlığın isteğe bağlı olduğu işlemlerde (örneğin `IssuePassAsync`)
anahtar verilmezse kütüphane bir UUID üretir ve aynı çağrının her denemesinde
aynısını gönderir.
- **Anahtar bir kimlik için kalıcı olarak tekildir** (8–64 karakter; defterden
  hiç silinmez). Aynı anahtarla aynı isteğin tekrarı ikinci kez yazmaz ve
  ilk sonucu `Duplicate = true` ile döndürür. Aynı anahtar başka bir gövdeyle
  `422 IDEMPOTENCY_KEY_REUSED` alır.
- **Fiş numarası tek başına anahtar olamaz:** yazarkasa fiş numaraları Z
  raporundan sonra yeniden başlar. Kasa + Z no + fiş no birleşimi
  (`kasa3-z0187-fis0042`) ya da satışla birlikte saklanıp tekrarda yeniden
  gönderilen bir UUID kullanın.
- **Fiş numarası `Reference` alanına** yazılır; müşterinin geçmişinde ve işlem
  dökümünde görünür.

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

Webhook'u panelden ya da API'den ekleyebilirsiniz. `webhooks.manage` yetkili
bir API anahtarı `CreateWebhookAsync`, `ListWebhooksAsync`, `GetWebhookAsync`,
`SetWebhookStatusAsync`, `TestWebhookAsync` ve `ListWebhookDeliveriesAsync`i
çağırabilir; `WebhookEventsAsync` abone olunabilecek olayları söyler. Sır
(`Secret`) yalnız `CreateWebhookAsync` yanıtında gelir, saklayın:

```csharp
var yeni = await rewloy.CreateWebhookAsync(new CreateWebhookBody
{
    Url = "https://ornek.com/rewloy/webhook",
    Events = new[] { "pass.activity", "pass.voided" },
});
var sir = yeni.Secret;
await rewloy.TestWebhookAsync(yeni.Webhook.Id);   // webhook.test olayı gönderir
```

Adres herkese açık bir `https` adresi olmalıdır (test ortamında da);
yerelde bir tünel kullanın.

**Sırrı yenilemek.** Kaybolan ya da sızan bir sır için `RotateWebhookSecretAsync`
webhook'a yeni bir sır verir (yeni `Secret` yalnız o yanıtta döner); webhook'u
silip yeniden eklemek gerekmez. Eski sır 24 saat daha yeninin yanında imzalar:
o sürede `Rewloy-Signature` iki `v1` taşır ve teslimler
`Rewloy-Signature-Rotating: 1` başlığıyla gelir. `Webhook.Verify` her `v1`'i ve
birden çok sırrı dener; yenilemeden önce alıcınızı `new[] { yeni, eski }` ile
güncelleyin. `DeleteWebhookAsync` webhook'u teslim geçmişiyle birlikte kalıcı
siler (`204`).

```csharp
var yeni = (await rewloy.RotateWebhookSecretAsync(webhookId)).Secret;
// yeni sırrı alıcınıza ekleyin, 24 saat sonra eskisini bırakın
var olay = Webhook.Verify(hamGovde, imzaBasligi, new[] { yeni, eskiSir });
```

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
  `pass.extended`, `location.frozen`, `location.unfrozen`, `business.paused`,
  `business.resumed`, `webhook.test`); gövdedeki `type` ile aynı (`olay.Type`).
  `pass.*` olaylarının verisi `olay.PassData`, `location.*` ve `business.*`
  olaylarının verisi (kart ve `customer_id` boştur) `olay.LocationData`dır.
- `Rewloy-Delivery`: teslimin kimliği. Teslim "en az bir kez"dir: çift gelen
  teslimi bununla ayıklayın.

Gövde kişinin iletişim bilgisini taşımaz; kişiyi `customer_id` ile API'den
okuyun. 2xx dışı bir yanıt yaklaşık 45 saat boyunca 8 kez yeniden denenir ve
her deneme yeni bir `t` ile imzalanır. Birden çok sır (webhook değiştirirken)
için `Webhook.Verify(gövde, başlık, new[] { eskiSir, yeniSir })`. Kendi
işleyicinizi test etmek için `Webhook.Sign(gövde, sir)` aynı başlığı üretir.

**Webhook'un durumu.** Webhook nesnesinde (`ListWebhooksAsync`, `GetWebhookAsync`,
`SetWebhookStatusAsync`, `CreateWebhookAsync` ve `RotateWebhookSecretAsync`'ın webhook'u)
iki tarih alanı hep vardır, ikisi de boş olabilir (`DateTimeOffset?`):
- `pausedUntil`: alıcınız art arda iki kez `5xx`, `429` verdi ya da yanıt vermedi;
  açık webhook'un teslimleri bu ana kadar bekler, sonra kendiliğinden yeniden
  denenir (60 saniye). Bekletilmiyorsa ya da webhook kapalıysa boştur.
- `resumableUntil`: webhook'u **kurallar** kapattı ve bekleyen teslimleri
  saklanıyor (kapanıştan 24 saat sonrasına kadar). Bu andan önce
  `SetWebhookStatusAsync(id, new SetWebhookStatusBody { Active = true })` ile
  açarsanız kaldığı yerden devam eder: saklananlar hemen gider, kapalıyken olan
  olaylar da gelir. Açıksa, bir kişi ya da anahtar kapattıysa ya da süre geçtiyse boştur.

```csharp
foreach (var w in await rewloy.ListWebhooksAsync())
{
    if (w.PausedUntil is { } bekletme) Console.WriteLine($"{w.Url}: {bekletme:u} anına kadar bekletiliyor");
    if (w.ResumableUntil is { } saklanan) Console.WriteLine($"{w.Url}: {saklanan:u} öncesinde açın, kaldığı yerden sürer");
}
```

## Hatalar ve yeniden deneme

```csharp
try
{
    await rewloy.PassActionAsync(serial, new PassActionBody { Action = "spend", LocationId = subeId, AmountMinor = 5000 },
        new RequestOptions { IdempotencyKey = $"kasa3-z0187-fis{fisNo}" });
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
- `RateLimitException`: `429`; `RetryAfter` (`TimeSpan?`). Her istisna (bu dahil)
  yanıtın `RateLimit-*` başlıklarını `RateLimit` olarak verir
  (`Limit`, `Remaining`, `Reset`; başlık yoksa `null`);
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
yanit.RateLimit;   // RateLimit-* başlıkları: Limit, Remaining, Reset (yoksa null)
yanit.Mode;        // Rewloy-Mode
yanit.Data;        // kampanya
```

Her metodun bir `…WithResponseAsync` ikizi vardır; `Data`ya ek olarak sayfalı
listede `Meta`, `StatusCode`, `Headers`, `RequestId`, `RateLimit`, `Mode` ve
`Replayed` döner.

`Mode`, yanıtın `Rewloy-Mode` başlığıdır: `live` ya da `test` (`IsTestMode`).
Başlık yoksa `Mode` `null`dır. Canlı akışta aynı bilgi `akis.Mode`dadır.

## Test modu

Gerçek müşterilere dokunmadan denemek için işletmenizin bir **test ortamı**
vardır: ona bağlı ayrı bir işletme (adı "· Test" ile biter); kendi
programları, müşterileri, kartları, anahtarları ve webhook'ları. Panel →
Geliştirici → "Test ortamını aç" ya da `POST /v1/test/environment`. Orada
oluşturulan anahtar `rwk_test_` ile başlar ve aynı adreste, aynı yollarla
çalışır:

```csharp
using var rewloy = new RewloyClient(new RewloyClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("REWLOY_TEST_KEY"),   // rwk_test_…
});
var yanit = await rewloy.GetPassWithResponseAsync(seri);
Console.WriteLine(yanit.IsTestMode);   // True
```

- Test ortamı hiçbir şey göndermez (e-posta, bildirim, SMS); kartlar
  cüzdanlara eklenmez. Gönderilmeyenler `GET /v1/test/messages` ile okunur.
- Webhook'lar teslim edilir ve `Rewloy-Test: 1` başlığıyla `"test": true`
  taşır.
- Gerçek müşteri verisini test ortamına girmeyin.
- `ResetTestEnvironmentAsync` (1.2.0'dan beri) müşterileri, kartları, kodları ve
  kayıtları siler; ortamın kimliği, programları, şubeleri, anahtarları ve
  webhook'ları kalır, entegrasyonunuz aynı anahtarla sürer. Bir anahtar
  sızdıysa `new ResetTestEnvironmentBody { RevokeKeys = true }` anahtarları da
  geçersiz kılar ve webhook'ları kapatır. Yanıt `Deleted` ve `Kept` sayılarını
  verir; `Closed` artık hep `null`dır.
- POS için anahtar: `CreateApiKeyAsync(new CreateApiKeyBody { Kind = "pos", LocationId = subeId, Register = "Kasa 1", Password = sifre })`
  hazır Kasa rolüyle yalnız o şubede çalışan bir anahtar oluşturur; yanıttaki
  `BaseUrl` POS'a yazılacak adrestir.
- `ListAllBatchesAsync` işletmenin bütün hediye kartı, kupon ve indirim
  kodlarını sayfalar (`Status` süzgeci: `open`, `full`, `expired`, `closed` ya da
  `archived`; satırın `State`'i de bunlardan biri: `archived` kodun programı
  arşivde demektir, bağlantısı kart vermez). Arşivdeki bir programa kod
  oluşturmak `409 PROGRAM_ARCHIVED` (`ErrorCode.ProgramArchived`) verir.
- `SendBatchLinkAsync` kodun bağlantısını yalnız kod kart verirken e-postayla
  gönderir: durdurulmuş kod `410 BATCH_CLOSED`, süresi dolmuş `410 BATCH_EXPIRED`,
  kartları bitmiş `410 BATCH_FULL`, programı arşivde olan `409 PROGRAM_ARCHIVED`
  verir ve e-posta gitmez (1.2.0'dan önce son üçünde de giderdi). Kodları
  `ErrorCode` sabitleri (`ErrorCode.BatchFull`…) içindedir.

Ayrıntı: https://rewloy.com/gelistiriciler#test-ortamı

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
- Testler ağa çıkmaz (isteğe bağlı canlı takım hariç, aşağıda). Üretimin belirleyici olduğunu ve işlenmiş çıktının
  güncel olduğunu da sınarlar.
- CI her gün canlı belgeyi okur ve bir değişiklik varsa bir pull request açar.
- Kararlar: [docs/DECISIONS.md](https://github.com/Rewloy/rewloy-dotnet/blob/main/docs/DECISIONS.md).

### Canlı testler

Kütüphaneyi gerçek bir Rewloy **geliştirme (dev) sunucusuna** karşı uçtan uca
sınayan ayrı bir takım vardır (`tests/Rewloy.Tests/Live`, `Category=Live`).
Yayın adayı canlıya geçmeden önce bu sunucuda sınanır. Ortam değişkenleri
yoksa **atlanır** (başarısız sayılmaz), yani `dotnet test` değişmez.

```sh
REWLOY_BASE_URL=https://dev-sunucunuz REWLOY_API_KEY=rwk_test_… \
  dotnet test tests/Rewloy.Tests -f net10.0 --filter Category=Live --logger "console;verbosity=detailed"
```

- `REWLOY_BASE_URL`: dev sunucusunun adresi. `REWLOY_API_KEY`: **yalnız test
  anahtarı** (`rwk_test_…`); başka anahtar reddedilir.
- `REWLOY_SESSION` (isteğe bağlı): test işletmesinin ekip oturumu (`rws_…`).
  Test sıfırlama bir anahtarı kabul etmez (`403 CREDENTIAL_NOT_ALLOWED`); oturum
  verilirse takım sonunda `ResetTestEnvironmentAsync` çağırır ve işletmeyi
  temiz bırakır. Verilmezse reddin kendisini sınar, sildiğini elle siler.
- Başlamadan `GET /v1/meta` okunur: `environment` `dev` değilse (ya da alan
  yoksa) takım **çalışmaz**. Canlıya karşı asla çalışmaz.
- Kapsadığı alanlar: meta ve işletme, rate-limit başlıkları, programlar (damga ve
  hediye kartı), kartlar (ver, getir, kasa görünümü), satış (`RecordSale`),
  `PassAction`, işlem listesi ve geri alma, müşteri arama, sayfalama, kodlar
  (oluştur, listele, `SendBatchLink` ret durumları), webhook'lar (oluştur,
  listele, sırrı yenile, sil), idempotency, hata nesneleri, test sıfırlama ve
  (API 1.3.0) fiş satırlı kazanım kuralları, önizlemeler, satır iadesi,
  `CopyProgramAsync`, şube QR'ı ve anahtarın şube dondurma reddi.
  Sonda alan başına geçen/kalan sayısı yazılır; herhangi bir hatada çıkış kodu
  sıfır değildir.
- Sıfırlama bir işletme için günde en fazla 5 kez çalışır (`429 RATE_LIMITED`).
- Kapsam dışı: bkz. `tests/Rewloy.Tests/Live/TODO.md` (şube dondurma da burada:
  ekip oturumu ve sahibin şifresi ister).

## Belgeler

| | |
|---|---|
| Başlarken | https://rewloy.com/gelistiriciler |
| API referansı | https://rewloy.com/gelistiriciler/api |
| OpenAPI 3.1 | https://app.rewloy.com/v1/openapi.json |
| Hata kodları | https://rewloy.com/gelistiriciler/hatalar |
| API'nin değişiklik günlüğü | https://rewloy.com/gelistiriciler/degisiklikler |
| Bu kütüphanenin değişiklikleri | [CHANGELOG.md](https://github.com/Rewloy/rewloy-dotnet/blob/main/CHANGELOG.md) |

**Sürümler:**
- Kütüphane anlamsal sürümleme ([SemVer](https://semver.org)) kullanır. 1.0'a
  kadar arayüzü değişebilir.
- API'ye alan eklemek geriye uyumludur; kütüphanenin sınıfları her gün
  güncellenir.
- Kalkacak bir uç nokta en az 180 gün önce duyurulur ve bu süre boyunca
  `Deprecation` ve `Sunset` başlıklarını taşır.

## Güvenlik

Bir güvenlik açığı bulursanız
[SECURITY.md](https://github.com/Rewloy/rewloy-dotnet/blob/main/SECURITY.md)
dosyasındaki yoldan özel olarak bildirin. Lütfen herkese açık issue açmayın.

## Lisans

[MIT](https://github.com/Rewloy/rewloy-dotnet/blob/main/LICENSE)

---

## English

Developer docs (in Turkish): **https://rewloy.com/gelistiriciler**.

**The official .NET (C#) library for the Rewloy API.**

> **Status: preview (0.x), on nuget.org since 0.2.3. The API is stable; the
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

From [nuget.org](https://www.nuget.org/packages/Rewloy):

```sh
dotnet add package Rewloy
```

(or `Install-Package Rewloy` in Visual Studio's Package Manager Console).

Or build it from source (.NET SDK 8 or later, and git):

```sh
git clone https://github.com/Rewloy/rewloy-dotnet
dotnet add reference rewloy-dotnet/src/Rewloy/Rewloy.csproj      # a project reference, or:
dotnet pack rewloy-dotnet/src/Rewloy -c Release -o ./nupkgs -p:PackageVersion=0.0.0-local   # a local package
dotnet nuget add source ./nupkgs --name rewloy-local
dotnet add package Rewloy --version 0.0.0-local   # never mixed up with the nuget.org version
```

On .NET Framework use `<PackageReference>` (with `packages.config`, `System.Text.Json`
needs binding redirects) and `<LangVersion>latest</LangVersion>` for `await foreach`.
TLS 1.2 is required: the default from 4.7 up; on older versions set it in `ServicePointManager`.

### Use

```csharp
using var rewloy = new RewloyClient(new RewloyClientOptions { ApiKey = apiKey });   // or StaffSession + Merchant, or HolderSession

var kart = await rewloy.IssuePassAsync(new IssuePassBody { ProgramId = programId, Email = email, KvkkConsent = true });
var sale = await rewloy.RecordSaleAsync(
    kart.Serial,
    new RecordSaleBody { LocationId = locationId, AmountMinor = 4550, Reference = $"receipt-{receiptNo}" },   // amount in the card's currency, minor units
    new RequestOptions { IdempotencyKey = $"till3-z0187-r{receiptNo}" });

// A gift-card spend rung up by mistake? Void it by the key it was sent with:
await rewloy.PassActionAsync(
    kart.Serial,
    new PassActionBody { Action = "spend", LocationId = locationId, AmountMinor = 2500 },
    new RequestOptions { IdempotencyKey = $"till3-z0187-s{receiptNo}" });
var voided = await rewloy.ReverseActionAsync(kart.Serial, new ReverseActionBody { ActionKey = $"till3-z0187-s{receiptNo}" });
Console.WriteLine($"{voided.Undone} {voided.Restored} {voided.Balance}");   // spend 2500 and the balance again
```

- **Till.** `RecordSaleAsync` writes a completed sale to a card (the card type
  and the programme's own rule decide what is written); `GetPassAsync` returns
  the card's structured fields (`ProgramName`, `Currency`, `Stamps`, `Points`,
  `Money`, `Customer`); `ReverseSaleAsync` takes a refunded sale back:
  `await rewloy.ReverseSaleAsync(serial, new ReverseSaleBody { SaleKey = key })`.
  A void is `ReverseActionAsync`: it takes back a `PassActionAsync` that was a
  mistake (`spend`, `spend-points`, `redeem-stamps`, `redeem-reward`, `use`),
  found by the `Idempotency-Key` you sent with it (`ActionKey`) or its
  `Reference`; it needs no `Idempotency-Key` of its own, and a repeat answers
  `Duplicate = true`:
  `await rewloy.ReverseActionAsync(serial, new ReverseActionBody { ActionKey = key })`.
  A till that queues sales while offline sets `RecordSaleBody.OccurredAt` (a
  `DateTimeOffset`, not in the future), so the card's history shows when the
  sale really happened; the queued `IdempotencyKey` makes the resend safe.
  `PassActionBody` takes an optional `Reference` too, and the answer,
  `PassActionData`, is one class for both shapes: the balance-card answer
  (`Balance`) or the coupon / discount-card answer (`Status`, `Uses`,
  `UsesLeft`); the other shape's properties are `null` (`answer.Uses != null`
  tells them apart; each property's documentation says which shape sends it).
- **`Card` on write answers.** `RecordSaleAsync`, `PassActionAsync`,
  `ReverseSaleAsync` and `ReverseActionAsync` answer with `Card`: the card after
  the write, the fields of `GetPassAsync` except `Customer`, read in the same
  transaction (on a replay, `Duplicate == true`, it is the card's **current**
  state). A key without `passes.read` in the card's programme gets
  `Card == null`. `RecordSaleData.Reversed == true` (replays only) says the sale
  written under that key was taken back since: send a new key to write the
  receipt again. For "can I act now" read `Card.Actions[].Ready`; `RewardReady`
  means "reward ready" only for stamp and points cards (always `true` on VIP, any
  balance on cashback and gift cards).
- **Recent operations.** `ListPassOperationsAsync` lists a card's ledger
  operations, newest first and paged (`ListPassOperationsAllAsync` for
  `await foreach`), for a till's "last operations" screen: `UndoWith`
  (`"sale/reverse"` or `"actions/reverse"`), `Reversible` and, for this
  credential's own operations, `SaleKey` / `ActionKey` to pass straight to
  `ReverseSaleAsync` / `ReverseActionAsync`.
- **Earn rules and receipt lines (API 1.3.0).** `RecordSaleBody.Lines` takes the
  receipt's lines (`RecordSaleBodyLinesItem`); with a programme's rules
  (`CreateEarnGroupAsync` for product groups, `PutEarnRulesAsync` for the rule
  set: `Revision` is the revision you read, a newer one is `409 REVISION_CONFLICT`)
  the answer's `Earn` explains the credit per line, per rule and in total.
  `PreviewSaleAsync` (a card) and `PreviewEarnAsync` (a programme, no card; an
  unsaved draft goes in `RuleSet`) answer the same without writing anything.
  `ReverseSaleBody.Lines` refunds some lines of a sale (needs an
  `Idempotency-Key`; the sale is judged again with the rules of its day and only the
  difference is taken back; the answer has `Earn` and `LinesLeft`).
  `PassActionBody.BillMinor` is the whole bill when a cashback programme limits
  its share of it. The Turkish part has a worked example.
- **Branch QR and freeze (API 1.3.0).** `PublicBranchAsync(code)` reads the page a
  branch's QR opens (no credential), `LocationQrSvgAsync` / `LocationQrPngAsync` /
  `LocationQrSheetPdfAsync` / `LocationQrSheetSvgAsync` return the QR and the A4
  sheet as a `RewloyFile`, `GetLocationQrItemsAsync` / `PutLocationQrItemsAsync`
  the branch's card list. A branch can be frozen (`FreezeLocationAsync` and
  friends: a team session and the person's password; a key gets
  `403 CREDENTIAL_NOT_ALLOWED`): its till then refuses `409 LOCATION_FROZEN`, and
  with every branch frozen the business pauses (`409 BUSINESS_FROZEN`).
  `CopyProgramAsync` copies a gift card, coupon or discount card; a loyalty
  card is `422 NOT_AN_INSTRUMENT`.
  `ProgramJoinQrAsync` takes an optional `ProgramJoinQrQuery` (`BranchCode`,
  `Format`); `ProgramJoinQrAsync(id, options)` as written for 0.2.4 still compiles.
- **Rejected `OccurredAt`** is a `400 VALIDATION` whose `Details[0].reason` is
  `in_future`, `too_old`, `before_issue` or `invalid` (treat an unknown reason as
  `invalid`).
- **Idempotency keys.** `RecordSaleAsync`, `PassActionAsync`,
  `SendCampaignAsync` and `RefundShopRedemptionAsync` need an `Idempotency-Key`:
  the API's OpenAPI document marks the header required for them, so
  `RequestOptions.IdempotencyKey` is required and the call throws an
  `ArgumentException` before sending if it is missing. The client never makes
  one up for you (a generated key would not survive a restart of your program).
  The key must be 8–64 printable ASCII characters (0x21–0x7E); a non-ASCII key
  such as `fiş-0042` is refused client-side, with an `ArgumentException`,
  before anything is sent. Where the header is optional (for example
  `IssuePassAsync`) the client still generates a UUID and reuses it on every
  retry of the call. A key is unique **for good per
  credential**: do not use the receipt number alone (fiscal receipt numbers
  restart after the Z report) but register + Z number + receipt number, or a
  UUID stored with the sale. The receipt number goes in `Reference`.
- **Base URL.** `new RewloyClientOptions { ApiKey = key, BaseUrl = "https://staging.example.com" }`
  or `BaseUrl = "https://staging.example.com/v1"`: with or without a trailing
  `/v1` (and trailing slashes), the client appends `/v1/...` itself. Default
  `https://app.rewloy.com`.
- **Test mode.** Open the test environment (panel → Developer, or
  `POST /v1/test/environment`) and use its `rwk_test_` key at the same address:
  a separate test business that sends nothing and never reaches real
  customers. Webhooks are delivered with `Rewloy-Test: 1`.
- **Arguments.** Path parameters (a `Guid` for a uuid), then the body and the
  query the operation takes, then `RequestOptions` (`IdempotencyKey`,
  `Merchant`, `Timeout`, `MaxRetries`, `Headers`) and a `CancellationToken`.
- **Results.** A method gives the answer's `data`: `Page<T>` for paged lists,
  `Task` for 204, a `RewloyFile` for files.
- **The whole answer.** Every method has a `…WithResponseAsync` twin that
  returns `StatusCode`, `Headers`, `RequestId`, `RateLimit` (`Limit`,
  `Remaining`, `Reset` from the `RateLimit-*` headers; `null` when absent),
  `Mode` (the `Rewloy-Mode` header: `live` or `test`; `IsTestMode`) and
  `Replayed` (`Idempotent-Replayed`) besides the data.
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

`RotateWebhookSecretAsync` gives a webhook a new secret (returned only in that
answer); the old one keeps signing for 24 hours, so `Rewloy-Signature` carries
two `v1` values and the delivery has `Rewloy-Signature-Rotating: 1`.
`Webhook.Verify` tries every `v1` and every secret you pass:
`new[] { newSecret, oldSecret }`. `DeleteWebhookAsync` removes a webhook and its
delivery history for good.

A webhook object (`ListWebhooksAsync`, `GetWebhookAsync`, `SetWebhookStatusAsync`, and the
`Webhook` of `CreateWebhookAsync` and `RotateWebhookSecretAsync`) always carries two
fields, each `DateTimeOffset?`, null when it does not apply:

- `pausedUntil`: your receiver failed twice in a row (`5xx`, `429`, a connection
  error or no answer), so the open webhook's deliveries wait until this moment
  and are then retried on their own (60 seconds). Null when it is not paused
  or the webhook is off.
- `resumableUntil`: the **rules** turned the webhook off and its pending
  deliveries are kept (until 24 hours after it closed). Turn it on before this
  moment (`SetWebhookStatusAsync(id, new SetWebhookStatusBody { Active = true })`) and it
  carries on where it stopped: the kept deliveries go at once and the events
  that happened meanwhile arrive too. Null while it is on, when a person or a
  key turned it off, or once the time has passed.

Rewloy 1.3.0 (library 0.3.0) adds the events `pass.extended` (a card's last day
moved later: `PassData.From`, `PassData.To`, `Reason` `merchant` or `branch_frozen`),
`location.frozen`, `location.unfrozen`, `business.paused` and `business.resumed`
(read with `ev.LocationData`; `card` and `customer_id` are null), and
`PassData.Partial` on the adjustment of a line refund. Keep a default branch on
`ev.Type`.

Also in Rewloy 1.2.0 (library 0.2.4): `CreateApiKeyAsync(new CreateApiKeyBody { Kind = "pos", LocationId = …, Register = …, Password = … })`
(a till key bound to one branch); `ResetTestEnvironmentAsync(new ResetTestEnvironmentBody { RevokeKeys = true })`
(keeps the test business, programmes and keys; revokes keys only when asked);
`ListAllBatchesAsync` (every gift-card, coupon and discount code of the
business, with the `archived` state); `409 PROGRAM_ARCHIVED` when creating a
code for an archived programme.
`SendBatchLinkAsync` e-mails a code's link only while the code issues a card:
`410 BATCH_CLOSED` (stopped), `410 BATCH_EXPIRED` (past its date),
`410 BATCH_FULL` (every card given) and `409 PROGRAM_ARCHIVED` (its programme is
archived) refuse it and no mail goes; before 1.2.0 the last three were sent
anyway. The codes are in the `ErrorCode` constants (`ErrorCode.BatchFull`…).

### Errors, retries, deprecations

- **Errors.** Failures throw `RewloyException` with `Status`, `Code` (the
  API's stable code; the constants are in `ErrorCode`), `Title`, `Detail`,
  `Details`, `RequestId`, `RateLimit` and `Body`. Subclasses: `RateLimitException`
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

### Live tests

A separate suite runs the library end to end against a real Rewloy **dev
server** (`tests/Rewloy.Tests/Live`, trait `Category=Live`); a release
candidate is tested there before it goes live. It is **skipped** (not failed)
when the environment is not set, so the ordinary `dotnet test` is unchanged.

```sh
REWLOY_BASE_URL=https://your-dev-server REWLOY_API_KEY=rwk_test_… \
  dotnet test tests/Rewloy.Tests -f net10.0 --filter Category=Live --logger "console;verbosity=detailed"
```

- `REWLOY_BASE_URL`: the dev server. `REWLOY_API_KEY`: a **test key only**
  (`rwk_test_…`); any other key is refused.
- `REWLOY_SESSION` (optional): a team session (`rws_…`) of the test business.
  The test reset does not accept an API key (`403 CREDENTIAL_NOT_ALLOWED`); with
  a session the suite ends with `ResetTestEnvironmentAsync` and leaves the
  business clean. Without it the suite checks that refusal and removes what it
  can by hand.
- It first reads `GET /v1/meta` and **refuses to run** unless `environment` is
  `dev` (a missing field counts as not dev). It never runs against live.
- Areas: meta and business, rate-limit headers, programs (stamp and gift card),
  passes (issue, get, till view), `RecordSale`, `PassAction`, the operations
  list and reversals, customer search, pagination, codes (create, list,
  `SendBatchLink` refusals), webhooks (create, list, rotate secret, delete),
  idempotency, error objects, test reset, and (API 1.3.0) earn rules with
  receipt lines, previews, a line refund, `CopyProgramAsync`, the branch QR and the
  key's refusal to freeze. It prints passed/failed per area and
  exits non-zero on any failure.
- The reset runs at most 5 times a day per business (`429 RATE_LIMITED`).
- Not covered: `tests/Rewloy.Tests/Live/TODO.md` (among them freezing a branch,
  which needs a team session and the owner's password).

### Security and licence

Report vulnerabilities privately, as [SECURITY.md](https://github.com/Rewloy/rewloy-dotnet/blob/main/SECURITY.md) says.
[MIT](https://github.com/Rewloy/rewloy-dotnet/blob/main/LICENSE) licensed.
