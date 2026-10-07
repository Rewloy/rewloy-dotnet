using System;

namespace Rewloy.Tests.Live
{
    /// <summary>
    /// A fact that runs only when the live-test environment is set: <c>REWLOY_BASE_URL</c> and <c>REWLOY_API_KEY</c>.
    /// Without them it is skipped (not failed), so the ordinary <c>dotnet test</c> is unaffected.
    /// </summary>
    public sealed class LiveFactAttribute : FactAttribute
    {
        public LiveFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(LiveEnv.BaseUrl) || string.IsNullOrWhiteSpace(LiveEnv.ApiKey))
                Skip = "Live tests: set REWLOY_BASE_URL and REWLOY_API_KEY (a dev server and an rwk_test_ key); see README, Live tests.";
        }
    }

    /// <summary>The environment the live tests read.</summary>
    public static class LiveEnv
    {
        public static string? BaseUrl => Get("REWLOY_BASE_URL");
        public static string? ApiKey => Get("REWLOY_API_KEY");

        /// <summary>Optional: a team session (<c>rws_…</c>) of the test business, which only the test reset accepts (an API key is refused there).</summary>
        public static string? Session => Get("REWLOY_SESSION");

        private static string? Get(string name)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(v) ? null : v!.Trim();
        }
    }
}
