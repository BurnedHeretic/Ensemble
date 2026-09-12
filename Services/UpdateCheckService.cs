using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Ensemble.Services
{
    internal sealed class UpdateCheckResult
    {
        public bool ReleaseAvailable
        {
            get;
            init;
        }

        public bool UpdateAvailable
        {
            get;
            init;
        }

        public string CurrentVersion
        {
            get;
            init;
        } =
            string.Empty;

        public string LatestVersion
        {
            get;
            init;
        } =
            string.Empty;

        public string ReleaseUrl
        {
            get;
            init;
        } =
            string.Empty;

        public string Message
        {
            get;
            init;
        } =
            string.Empty;
    }

    internal static class UpdateCheckService
    {
        public const string RepositoryUrl =
            "https://github.com/BurnedHeretic/Ensemble";

        private const string LatestReleaseApi =
            "https://api.github.com/repos/BurnedHeretic/Ensemble/releases/latest";

        public static async Task<UpdateCheckResult> CheckAsync()
        {
            string current =
                Assembly.GetExecutingAssembly()
                    .GetName()
                    .Version?
                    .ToString()
                ??
                "0.0.0.0";

            using HttpClient client =
                new HttpClient();

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Ensemble-Halo-Wars-Map-Editor");

            using HttpResponseMessage response =
                await client.GetAsync(
                    LatestReleaseApi);

            if (response.StatusCode ==
                HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult
                {
                    ReleaseAvailable =
                        false,

                    CurrentVersion =
                        current,

                    Message =
                        "No public GitHub release has been published yet."
                };
            }

            response.EnsureSuccessStatusCode();

            using JsonDocument document =
                JsonDocument.Parse(
                    await response.Content
                        .ReadAsStringAsync());

            JsonElement root =
                document.RootElement;

            string tag =
                root.TryGetProperty(
                    "tag_name",
                    out JsonElement tagElement)
                    ? tagElement.GetString()
                      ??
                      string.Empty
                    : string.Empty;

            string releaseUrl =
                root.TryGetProperty(
                    "html_url",
                    out JsonElement urlElement)
                    ? urlElement.GetString()
                      ??
                      string.Empty
                    : string.Empty;

            Version? currentVersion =
                ParseVersion(
                    current);

            Version? latestVersion =
                ParseVersion(
                    tag);

            bool update =
                currentVersion !=
                    null
                &&
                latestVersion !=
                    null
                &&
                latestVersion >
                    currentVersion;

            return new UpdateCheckResult
                {
                    ReleaseAvailable =
                        true,

                    UpdateAvailable =
                        update,

                    CurrentVersion =
                        current,

                    LatestVersion =
                        tag,

                    ReleaseUrl =
                        releaseUrl,

                    Message =
                        update
                            ? "A newer Ensemble release is available."
                            : "You are running the latest published Ensemble release."
                };
        }

        private static Version? ParseVersion(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return null;
            }

            string cleaned =
                value.Trim();

            if (cleaned.StartsWith(
                    "v",
                    StringComparison.OrdinalIgnoreCase))
            {
                cleaned =
                    cleaned[1..];
            }

            int dash =
                cleaned.IndexOf(
                    '-');

            if (dash >=
                0)
            {
                cleaned =
                    cleaned[..dash];
            }

            return Version.TryParse(
                cleaned,
                out Version? version)
                    ? version
                    : null;
        }
    }
}
