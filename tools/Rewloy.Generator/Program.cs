using System.Text;
using System.Text.Json;
using Rewloy.Generator;

// dotnet run --project tools/Rewloy.Generator
//     Fetches the live document, keeps it as openapi/openapi.json and writes src/Rewloy/Generated/.
// dotnet run --project tools/Rewloy.Generator -- --file openapi/openapi.json
//     Generates from a saved document, e.g. the committed snapshot (reproducible builds).
// dotnet run --project tools/Rewloy.Generator -- --url <url>
//     Fetches from another address.
// --root <dir> names the repository's root; by default it is found by walking up to Rewloy.sln.
//
// The document is checked (the generator refuses what it does not understand)
// before anything is written; files that did not change are left alone.

const string Live = "https://app.rewloy.com/v1/openapi.json";

try
{
    return await RunAsync(args);
}
catch (Exception ex) when (ex is GeneratorException or HttpRequestException or JsonException or IOException or ArgumentException or TaskCanceledException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static async Task<int> RunAsync(string[] args)
{
    string? Arg(string name)
    {
        var i = Array.IndexOf(args, name);
        if (i < 0) return null;
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"{name} needs a value");
        return args[i + 1];
    }

    var root = Arg("--root") ?? FindRoot();
    var file = Arg("--file");
    var snapshotPath = Path.Combine(root, "openapi", "openapi.json");

    string text;
    if (file is not null) text = await File.ReadAllTextAsync(file, Encoding.UTF8);
    else
    {
        var url = Arg("--url") ?? Live;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "rewloy-dotnet-generator");
        using var res = await http.GetAsync(url);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"GET {url}: HTTP {(int)res.StatusCode}");
        text = await res.Content.ReadAsStringAsync();
    }

    using var document = JsonDocument.Parse(text);
    var files = ApiGenerator.Generate(document.RootElement);
    var changed = new List<string>();
    if (file is null && Put(snapshotPath, JsonSnapshot.Write(document.RootElement))) changed.Add(Path.GetRelativePath(root, snapshotPath));
    foreach (var f in files)
    {
        if (Put(Path.Combine(root, f.Path), f.Content)) changed.Add(f.Path);
    }

    var operations = ApiGenerator.CountOperations(document.RootElement);
    Console.WriteLine($"{operations} operations; {(changed.Count > 0 ? "changed: " + string.Join(", ", changed) : "nothing changed")}");
    return 0;
}

/// <summary>Writes when the content differs; returns whether it did.</summary>
static bool Put(string path, string content)
{
    if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == content) return false;
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    return true;
}

static string FindRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rewloy.sln"))) return dir.FullName;
        }
    }
    throw new ArgumentException("Rewloy.sln not found above the current directory: pass --root <dir>");
}
