using System.Reflection;
using System.Text.Json.Nodes;

namespace SQLTableExporter;

internal static class UpdateChecker
{
    private const string PackageId = "anujshroff.sqltableexporter";
    private const string PackageDisplayId = "AnujShroff.SQLTableExporter";
    private const string IndexUrl = "https://api.nuget.org/v3-flatcontainer/" + PackageId + "/index.json";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    static UpdateChecker()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SQLTableExporter-UpdateCheck");
    }

    public static Task<(Version Current, Version Latest)?> CheckAsync()
    {
        return Task.Run(async () =>
        {
            try
            {
                Version? current = Assembly.GetExecutingAssembly().GetName().Version;
                if (current is null)
                {
                    return ((Version, Version)?)null;
                }

                string json = await HttpClient.GetStringAsync(IndexUrl);
                JsonNode? root = JsonNode.Parse(json);
                JsonArray? versions = root?["versions"]?.AsArray();
                if (versions is null)
                {
                    return null;
                }

                Version? latest = null;
                foreach (JsonNode? node in versions)
                {
                    string? raw = node?.GetValue<string>();
                    if (string.IsNullOrEmpty(raw) || raw.Contains('-'))
                    {
                        continue;
                    }
                    if (Version.TryParse(raw, out Version? v) && (latest is null || v > latest))
                    {
                        latest = v;
                    }
                }

                if (latest is null)
                {
                    return null;
                }

                Version currentN = Normalize(current);
                Version latestN = Normalize(latest);

                return latestN > currentN ? (currentN, latestN) : ((Version, Version)?)null;
            }
            catch
            {
                return null;
            }
        });
    }

    public static async Task PrintBannerIfAvailable(Task<(Version Current, Version Latest)?> checkTask)
    {
        try
        {
            (Version Current, Version Latest)? result = await checkTask;
            if (result is null)
            {
                return;
            }
            Console.WriteLine();
            Console.WriteLine($"A newer version is available: {result.Value.Latest} (you have {result.Value.Current})");
            Console.WriteLine($"Update with: dotnet tool update --global {PackageDisplayId}");
        }
        catch
        {
        }
    }

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build);
}
