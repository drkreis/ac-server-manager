using System.IO;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

internal static class AssettoServerDownload
{
    public static async Task<string> Download(string destination, IProgress<string>? progress, CancellationToken cancellation)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var version = typeof(AssettoServerDownload).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ACServerManager/{version}");
        progress?.Report(T("Проверяю официальный релиз AssettoServer…"));
        var uri = new Uri("https://github.com/compujuckel/AssettoServer/releases/latest/download/assetto-server-win-x64.zip");
        var tag = T("Официальный стабильный релиз");
        long? expectedSize = null; string? expectedHash = null;
        using var response = await client.GetAsync("https://api.github.com/repos/compujuckel/AssettoServer/releases/latest", cancellation);
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
        {
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var release = json.RootElement;
            var assets = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == "assetto-server-win-x64.zip").ToArray();
            if (assets.Length != 1) throw new InvalidOperationException(T("В официальном релизе не найден Windows x64 ZIP. Выберите скачанный ZIP вручную."));
            var asset = assets[0]; expectedSize = asset.GetProperty("size").GetInt64();
            uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
            if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/compujuckel/AssettoServer/releases/download/", StringComparison.Ordinal)
                || expectedSize is < 1 or > 512L * 1024 * 1024) throw new InvalidOperationException(T("Некорректный пакет официального релиза AssettoServer."));
            tag = release.GetProperty("tag_name").GetString()!;
            if (asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String && digest.GetString() is { } hash && hash.StartsWith("sha256:", StringComparison.Ordinal)) expectedHash=hash[7..];
        }
        // GitHub's supported direct latest-asset link remains usable when its API is rate limited.
        using var download = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation);
        download.EnsureSuccessStatusCode();
        var size = expectedSize ?? download.Content.Headers.ContentLength;
        if (size is < 1 or > 512L * 1024 * 1024) throw new InvalidDataException(T("Некорректный пакет официального релиза AssettoServer."));
        await using (var input = await download.Content.ReadAsStreamAsync(cancellation))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920]; long received = 0; var lastPercent = -1;
            while (true)
            {
                var count = await input.ReadAsync(buffer, cancellation);
                if (count == 0) break;
                received += count;
                if (received > 512L * 1024 * 1024 || size.HasValue && received > size.Value) throw new InvalidDataException(T("Размер загруженного ZIP не совпадает с релизом."));
                await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
                var percent = size.HasValue ? (int)(received * 100 / size.Value) : (int)(received / (1024 * 1024));
                if (percent != lastPercent) { lastPercent = percent; progress?.Report(size.HasValue ? T("Загружаю AssettoServer {0}: {1}%", tag, percent) : T("Загружаю AssettoServer: {0} МБ", percent)); }
            }
            if (received == 0 || size.HasValue && received != size.Value) throw new InvalidDataException(T("Размер загруженного ZIP не совпадает с релизом."));
        }
        if (expectedHash != null)
        {
            await using var stream = File.OpenRead(destination);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation)).ToLowerInvariant();
            if (actual != expectedHash) throw new InvalidDataException(T("Контрольная сумма ZIP AssettoServer не совпадает."));
        }
        return tag;
    }
}
