using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using ClipboardBridge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

static class DownloadChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), "clipboard-download-check-" + Guid.NewGuid().ToString("N") + ".apk");
        byte[] bytes = RandomNumberGenerator.GetBytes(128 * 1024);
        await File.WriteAllBytesAsync(path, bytes);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        AndroidDownload.Map(app, () => path);
        try
        {
            await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) };
            using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/app/android"));
            check(head.StatusCode == HttpStatusCode.OK && head.Content.Headers.ContentLength == bytes.Length && (await head.Content.ReadAsByteArrayAsync()).Length == 0,
                "APK HEAD preflight returns the complete size with no body");
            string etag = head.Headers.ETag!.Tag;
            check(etag == '"' + Convert.ToHexString(SHA256.HashData(bytes)) + '"' && head.Headers.AcceptRanges.Contains("bytes"),
                "APK download advertises byte ranges and a content-derived validator");
            using var full = await client.GetAsync("/app/android");
            check((await full.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes), "Actual HTTP APK response completes with identical bytes");
            using var partial = new HttpRequestMessage(HttpMethod.Get, "/app/android");
            partial.Headers.Range = new RangeHeaderValue(37_000, null);
            partial.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(etag));
            using var resumed = await client.SendAsync(partial);
            check(resumed.StatusCode == HttpStatusCode.PartialContent && resumed.Content.Headers.ContentRange?.From == 37_000 &&
                (await resumed.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes[37_000..]), "Interrupted APK downloads resume at the requested byte offset");
            partial.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue("\"different-build\""));
            using var stale = new HttpRequestMessage(HttpMethod.Get, "/app/android");
            stale.Headers.Range = partial.Headers.Range;
            stale.Headers.IfRange = partial.Headers.IfRange;
            using var changed = await client.SendAsync(stale);
            check(changed.StatusCode == HttpStatusCode.OK && (await changed.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes),
                "A stale APK validator restarts the full download instead of mixing builds");
            using var invalid = new HttpRequestMessage(HttpMethod.Get, "/app/android");
            invalid.Headers.Range = new RangeHeaderValue(bytes.Length, null);
            using var rejected = await client.SendAsync(invalid);
            check(rejected.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable, "An invalid APK resume offset returns HTTP 416");
        }
        finally { await app.StopAsync(); File.Delete(path); }
    }
}
