using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestFilterMod.Web.Services;

/// <summary>
/// Проверяет последнюю версию мода через GitHub Releases API.
/// </summary>
public static class VersionChecker
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/cccpvs/QuestFilterMod/releases/latest";
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    static VersionChecker()
    {
        Http.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("QuestFilterMod-SPT");
    }

    /// <summary>
    /// Проверяет, есть ли новая версия. Возвращает (hasUpdate, latestVersion, tagName).
    /// </summary>
    public static async Task<(bool HasUpdate, string LatestVersion, string TagName, string Url, string Error)> CheckAsync(
        string currentVersion, CancellationToken ct = default)
    {
        try
        {
            var release = await Http.GetFromJsonAsync<GithubRelease>(
                ReleasesApiUrl,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                ct);

            if (release == null)
                return (false, null, null, null, "No response from GitHub");

            var latestVersion = release.TagName?.TrimStart('v') ?? release.Name?.TrimStart('v');
            if (string.IsNullOrWhiteSpace(latestVersion))
                return (false, null, null, null, "Could not parse version from release");

            // Парсим версии для сравнения
            if (!TryParseVersion(currentVersion, out var current) || !TryParseVersion(latestVersion, out var latest))
                return (false, latestVersion, release.TagName, release.HtmlUrl, "Version parse error");

            var hasUpdate = latest > current;

            return (hasUpdate, latestVersion, release.TagName, release.HtmlUrl, null);
        }
        catch (TaskCanceledException)
        {
            return (false, null, null, null, "Timeout");
        }
        catch (Exception ex)
        {
            return (false, null, null, null, ex.Message);
        }
    }

    private static bool TryParseVersion(string version, out Version v)
    {
        try
        {
            // Убираем всё кроме цифр и точек
            var clean = new string(version.Where(c => char.IsDigit(c) || c == '.').ToArray());
            v = Version.Parse(clean);
            return true;
        }
        catch
        {
            v = null!;
            return false;
        }
    }

    private class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; }
    }
}
