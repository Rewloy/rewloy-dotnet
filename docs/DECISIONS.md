# Decisions

Choices made while building v0.1 without the owner (4 Oct 2026). The Node
library's decisions (`Rewloy/rewloy-node`, docs/DECISIONS.md, 24 of them)
hold here unless one below replaces it. Each can be revisited; most are a
line to change.

**Reused as they are** (Node's numbers): own emitter (1), sunset dates read
from the platform's sentence (3), generation refuses what it does not
understand (5), English library text with the API's Turkish descriptions and
a bilingual README (8), a credential is left out where an operation does not
take its kind but works without one (13), construction is checked (14), the
retry rules and their numbers (15), timeouts per attempt, for a stream only
until its headers (16), idempotency keys (17: required where the API's
document says so and never made up for those, printable ASCII of 8-64
characters checked before sending, a UUID v4 only where optional), one error hierarchy with the same codes for what
never got an answer (18), streams reconnect by default (21), webhooks accept
any `v1` and any of several secrets with a ±300 s tolerance (22), and the
regeneration workflow (24).

## Generation

1. **The generator is C#, in the repository** (`tools/Rewloy.Generator`, a
   console project), not a Node script.
   - **Who needs what:** a .NET developer who wants to regenerate needs only
     the .NET SDK.
   - **Tests:** they call `ApiGenerator.Generate` in-process.
   - **The snapshot:** `JsonSnapshot` writes the document the way
     `JSON.stringify(doc, null, 2)` does, so `openapi/openapi.json` is byte for
     byte the file the Node and PHP libraries keep (`cmp` agrees). A test pins
     the format, and a live run of the generator reports "nothing changed".
2. **Classes for objects, `JsonElement` for what has no shape: a mix.** The
   document has about 800 inline object schemas (630 classes come out of them),
   2 `oneOf` and 2 `anyOf`.
   - **Objects with properties** become one `sealed class` each, named
     `{Operation}Body`, `…Query`, `…Data` or `…Item`; a nested object is
     `{Parent}{Property}`, with `Item` after it in a list; a clash gets a
     number. Typed classes are what IntelliSense and a compiler can help with.
   - **A union of objects** (`oneOf` of different shapes: passAction's two
     answers, `me`'s staff session or API key) is one class with every
     member's properties (0.2.2). A property not in every member is
     nullable and its documentation names the members that send it; one that
     two members shape differently keeps the whole union a `JsonElement`.
     C# has no sum type that compiles on .NET Framework with C# 7.3 and
     deserializes without a converter; a flat class does both and reads
     like the other models. Before 0.2.2 these were `JsonElement`.
   - **`JsonElement`** is used for any other union (a team grant's
     `locations`, which is `"all"` or a list of ids), for a free-form object
     (the passkey `response`) and for a type the document does not give.
   - **A map** (`additionalProperties` with a schema) is
     `IReadOnlyDictionary<string, T>`; a list is `IReadOnlyList<T>`.
   - **Not `JsonElement` everywhere:** that is what the PHP library does with
     arrays, but C# has no cheap way to read `a["b"]["c"]` with types, and
     the point of a .NET library is the types.
3. **Plain mutable classes, not records.**
   - **The audience:** .NET Framework 4.8 projects, whose default is C# 7.3. A
     record's `init` (C# 9) and `required` (C# 11) properties cannot be used
     from there, or from a Visual Studio 2019 compiler. Plain `get; set;`
     properties compile in every C# version.
   - **It fits ERP code,** which fills a body field by field.
   - **The cost:** no `with` and no value equality; `ToString()` is the
     default, so a secret in an answer is not printed by accident.
   - **Tested:** a C# 7.3 console project compiled against the
     `netstandard2.0` build and ran (the README's examples too).
4. **Fields the API adds are kept.** Every generated class derives from
   `RewloyObject`, whose `AdditionalProperties` (`[JsonExtensionData]`) holds
   what the class has no property for. It is written back when a body is sent,
   so a field added to the API is usable before the next regeneration.
5. **Enums stay strings** (integer enums stay numbers), the values in the
   doc comment. An `enum` type would throw on the day the API adds a value.
   `ErrorCode` is a class of string constants for the same reason.
6. **The mapping of types:**
   - `uuid` is `Guid`, `date-time` is `DateTimeOffset`, `uri` and `email` stay
     `string`;
   - an integer is `int` when the schema bounds it inside `Int32` on both
     sides and `long` otherwise (a `limit`, a count; not an amount);
   - a number is `double`;
   - `anyOf` of strings (a date or a date-time) is `string`;
   - a path parameter that is a uuid is a `Guid` argument.
7. **Required fields are not enforced.** The C# `required` keyword needs C# 11,
   and `System.Text.Json` would throw on an answer that lacks a field
   documented as required. A required field is a non-nullable property and
   says "Required." in its doc; a forgotten one is the API's `VALIDATION`
   answer, with the field named in `Details`. Lists a required answer lacks
   are empty, not null.
8. **`Optional<T>` where `null` means something.** In a request body a
   property whose schema allows `null` (seven fields, on four operations: a
   holder's profile corrections, a segment's note, an automation's
   programme and a passkey sign-in's user handle) is `Optional<T>`: left out (not sent), a value, or
   `Optional<T>.Null` (sent as `null`). A nullable property alone cannot say
   "clear it". It converts from `T`, so `Phone = "…"` reads as it should.
9. **Method names and shapes.**
   - **The name** is the operationId in PascalCase with `Async`. A paged list
     also has `…AllAsync` (`IAsyncEnumerable` over every item); every
     operation except a stream has `…WithResponseAsync` (decision 14).
   - **The arguments:** path parameters, then the body, then the query, then
     `RequestOptions` and a `CancellationToken`. A body or a query is optional
     (`= null`) only when everything after it is; a body whose fields are all
     optional that is left out is sent as `{}`, as Node does.
   - **Refusals:** two operations that make the same method name, a name the
     client has itself, a header parameter other than `Rewloy-Merchant` and
     `Idempotency-Key`, a body that is not JSON, `allOf`, an unknown `$ref`, a
     paged list without a `page` parameter or with a body.
10. **Deprecations are `[Obsolete]`** on the method of a deprecated operation
    and on a deprecated field (five today, all until 5 April 2027), with the
    sunset and the replacement from `x-deprecation`. The compiler warns; it
    does not fail a build unless the project turns warnings into errors.
11. **Two namespaces.** `Rewloy` holds the client and everything hand-written;
    `Rewloy.Models` holds the 630 generated classes, so that
    `using Rewloy;` does not fill IntelliSense with them.
12. **Reflection-based `System.Text.Json`, no source generator** in 0.1. A
    `JsonSerializerContext` for 630 types would be generated too, but the
    library is not annotated for trimming or Native AOT yet. A later version
    can.

## Language and package

13. **Targets `net8.0` and `netstandard2.0`.** Both builds are the same
    source; `#if` is used in four places (the framework's constant-time
    compare, two body reads, the socket handler).
    - **Why `netstandard2.0`:** Turkish desktop ERP and POS software is mostly
      Windows-based, .NET or Delphi (ECOSYSTEM.md §2 in the platform's
      repository), and the .NET part is mostly .NET Framework 4.8. 4.7.2
      and later load `netstandard2.0` libraries without trouble; 4.6.1 to 4.7.1
      are named by Microsoft but known to need workarounds, so the README does
      not promise them. .NET Core 2.x and 3.x load it too.
    - **Why `net8.0`:** current .NET gets a build with no dependency at all
      and a pooled `SocketsHttpHandler`. .NET 8 is supported until 10
      November 2026; .NET 10, the current LTS, loads the `net8.0` build, and a
      `net10.0` target adds nothing yet. Drop `net8.0` or add `net10.0` when
      that changes.
    - **Not `net48` itself:** the `netstandard2.0` build covers it without a
      third copy; there is no Windows machine here to test one.
14. **Results: the data, and a twin for the whole answer.** A method resolves
    to the answer's `data`: a class, `Page<T>` (items and `PageMeta`) for a
    paged list, `RewloyFile` (bytes, content type, file name) for a file,
    `JsonElement` for the OpenAPI document, `Task` for a 204.
    `…WithResponseAsync` returns `RewloyResponse<T>` with the status, the
    headers, `RequestId`, `Mode`, `IsTestMode` and `Replayed` too.
    - **Why a second method:** C# has no cheap way to ask for "this call, with
      its headers" through the first. 237 one-line methods cost almost
      nothing, and the test mode (`Rewloy-Mode`) is read from the raw answer
      there, as Node's `request()` does. There is no `LastResponse` property,
      which would race under concurrent calls.
    - **A stream** has no such twin: its `EventStream` carries `RequestId` and
      `Mode` itself.
15. **Dependencies.** None on `net8.0`. `System.Text.Json` 8.0.6, the latest
    8.0 patch when this was written, on `netstandard2.0`; it brings
    `Microsoft.Bcl.AsyncInterfaces` for `IAsyncEnumerable`. `ILogger` is not
    used for the deprecation notice (decision 22).
16. **C# 12 in the library, C# 8 for callers who iterate.** Library source is
    `LangVersion` 12 with nullable reference types on. A caller needs C# 8 for
    `await foreach` only; nothing else in the public surface needs more than
    C# 7.3 (decision 3).
17. **Versioning.** `<Version>` in `Rewloy.csproj`, `RewloyVersion.Current`
    (for the `User-Agent`) and the latest heading of CHANGELOG.md must agree;
    a test says so. The package is not signed and the assembly is not
    strong-named: nothing asks for it, and a strong name cannot be taken back.
18. **Package.** ID `Rewloy`, MIT, the README and the icon inside, release
    notes that link the CHANGELOG at the version's tag, XML documentation for
    both builds, package validation, a symbol package (`.snupkg`) with
    SourceLink to the public `github.com/Rewloy/rewloy-dotnet`. Published to
    nuget.org by the Release workflow (`release.yml`) on a `v*` tag.

## Client

19. **Options are an object.** `new RewloyClient(new RewloyClientOptions {
    ApiKey = … })`, with `StaffSession` and `Merchant` or `HolderSession`
    instead. A merchant is a `Guid`. Durations are `TimeSpan`s; `Timeout` of
    zero or `Timeout.InfiniteTimeSpan` means none. A wrong prefix, two
    credentials or a merchant without a staff session is an
    `ArgumentException` (Node's `TypeError`), whose message never carries the
    value.
20. **The `HttpClient`.**
    - **Given:** it is used as it is and never disposed (it may come from
      `IHttpClientFactory`). Its own `Timeout` still applies on top of the
      client's.
    - **Not given:** the client makes one with `SocketsHttpHandler` (a
      2-minute connection lifetime, so that DNS changes are seen) or, on
      `netstandard2.0`, `HttpClientHandler`. It follows no redirects: the API
      does not redirect, and a redirect could carry the token elsewhere. It
      accepts gzip and deflate and has no timeout of its own: the client times
      each attempt, so that a timeout is a `RewloyTimeoutException` that can
      be retried. It is disposed with the client.
    - **Per call:** each attempt builds a new `HttpRequestMessage` (one cannot
      be sent twice) from one serialized body.
21. **Exceptions.** Node's hierarchy with .NET's names:
    `RewloyException`, `RateLimitException` (`RetryAfter` is a `TimeSpan?`),
    `RewloyConnectionException` and `RewloyTimeoutException`. The last two
    carry the `Rewloy` prefix because `System.TimeoutException` exists.
    - **Fields:** `Status`, `Code`, `Title`, `Detail`, `Details`
      (`JsonElement?`), `Docs`, `RequestId` (the header first), `Body` (text),
      `Headers` and `Operation`. `Code` is a string; `ErrorCode` has a
      constant and a title for each code.
    - **Cancellation** is never wrapped: a cancelled token is an
      `OperationCanceledException`, as everywhere in .NET.
    - **An `HttpClient` that times out** by its own `Timeout` is a
      `RewloyTimeoutException` too.
    - **A 2xx answer that does not fit its type** is `INVALID_RESPONSE`, with
      the `JsonException` inside.
22. **Deprecations are a static event.** `RewloyClient.Deprecated` is raised
    once per operation per process, as Node's warning is, and the notice also
    goes to `Trace.TraceWarning`. The brief offered `ILogger` or an event: the
    event keeps the library free of `Microsoft.Extensions.Logging`, and one
    line connects it to any logger (the README shows it). A subscriber that
    throws is ignored, so that an answered call is not lost: Node's decision
    19 in other words.
23. **`User-Agent`:** `rewloy-dotnet/0.1.0 dotnet/8.0.31 [suffix]`, or
    `netfx/4.8.9300.0` on .NET Framework. Not the `Rewloy-Client` header, for
    Node's decision 12.
24. **Streams.** `EventStream` is an `IAsyncEnumerable<ServerSentEvent>` and
    an `IAsyncDisposable`, returned by `LiveFeedAsync` and
    `HolderCardEventsAsync` without a `Task` (nothing is sent until the first
    `MoveNextAsync`).
    - **One iteration:** a second `GetAsyncEnumerator` throws.
    - **Ending:** a cancelled token, `break` and `Close()` end it without an
      exception and close the connection (the response is disposed even where
      the platform ignores a token while reading, such as .NET Framework).
    - **State:** `LastEventId`, `RetryDelay`, `RequestId` and `Mode` are on
      the stream, which PHP's generators cannot offer.
    - **On the wire:** `Accept-Encoding: identity` and `Cache-Control:
      no-cache`, as an `EventSource` asks and so that no proxy holds events
      back.
    - **Parser:** `SseParser` is public and takes text in pieces of any
      size; UTF-8 is decoded with a stateful decoder.
25. **Paging.** `…AllAsync` starts at the query's `Page` (else 1), asks for
    the next page while `meta` says there is one, and does not ask past a full
    last page. It never changes the query it was given. The query's `Page` is
    an `int` when the schema bounds it, and the copy for the next page is made
    with `MemberwiseClone`, not reflection.
26. **Webhooks.** `Webhook.Verify(payload, header, secret…)` takes the body as
    `string` or `byte[]` and one secret or several (four overloads), a
    tolerance in seconds and `now` as a `DateTimeOffset`; it returns a
    `WebhookEvent`.
    - **The result:** `Root` (the body as `JsonElement`), `Type`, `Id`,
      `CreatedAt`, `Data` and `PassData`, a typed `PassEventData` for the
      `pass.*` events. `Type` is a string, so a new event type is a `default`
      branch, not an error.
    - **A signed body that is not a JSON object** is refused (`Payload`), as
      PHP does.
    - **An empty secret** is an `ArgumentException`, a missing setting rather
      than a bad delivery.
    - **The constant-time compare** is `CryptographicOperations.FixedTimeEquals`
      where there is one (net6.0 and later) and a loop that does not stop at the
      first difference on `netstandard2.0`.
    - **The vectors** are the two the Node and PHP tests use.
    - **`Sign`** is public for testing a handler.
27. **Query strings** are RFC 3986 (`%20`), booleans `true`/`false`, numbers
    in the invariant culture, a list repeats its key, and a null parameter is
    left out. Path values are escaped as a whole (`/` too).
28. **`Delay` is public,** as Node's `sleep` is, so that tests (and anyone who
    schedules differently) can replace the wait between retries and
    reconnections.

## Tests and CI

29. **Tests.** xUnit 2.9 on a stub `HttpMessageHandler`; no network, no
    server, no credential. 119 of them: construction, requests, retries,
    errors, paging, the SSE parser and the stream (including chunks of one
    byte, a silent connection and cancellation), webhooks, deprecations, the
    models, and the generator (determinism, the committed output being
    current, the snapshot's format, a fixture of the cases the live document
    does not have, and its refusals). They run on `net10.0` and `net8.0`, and
    `-p:RewloyAsset=netstandard2.0` runs the same tests against the other build.
30. **What is not tested:** a real .NET Framework runtime. The
    `netstandard2.0` build is exercised on .NET 10, and a C# 7.3 project
    compiled against it, but its `HttpClient` is .NET Framework's own on that
    platform. CI can add a Windows job with `net48` once someone can try it
    locally.
31. **`ci.yml`:** `actions/checkout@v7` and `actions/setup-dotnet@v6` (the
    current majors, 8.0.x and 10.0.x), on Ubuntu and Windows against both
    builds, and a `pack` job that builds the package and lists it. Warnings are
    errors there (`CI=true`).
32. **`regenerate.yml`** is Node's decision 24, with `dotnet test` as its
    check, at 05:59 UTC (Node's runs at 05:23, PHP's at 05:41).
33. **`release.yml`** runs on a `v*` tag only: it stops unless the tag is
    `v` + `<Version>`, tests both builds, packs, and pushes to nuget.org with
    Trusted Publishing (`NuGet/login@v1`, environment `nuget`, the policy
    creator's username in the repository variable `NUGET_USER`). No API key
    is stored. The setup is in the file's header.
