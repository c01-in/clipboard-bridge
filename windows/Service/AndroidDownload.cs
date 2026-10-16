using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;

namespace ClipboardBridge;

public static class AndroidDownload
{
    public static void Map(IEndpointRouteBuilder endpoints, Func<string> apkPath)
    {
        endpoints.MapMethods("/app/android", ["GET", "HEAD"], (HttpContext context) =>
        {
            string path = apkPath();
            if (!File.Exists(path)) return Results.NotFound();
            // A strong validator prevents a resumed download from combining two different APK builds.
            using var input = File.OpenRead(path);
            var etag = new EntityTagHeaderValue('"' + Convert.ToHexString(SHA256.HashData(input)) + '"');
            context.Response.Headers.CacheControl = "private, no-cache";
            return Results.File(path, "application/vnd.android.package-archive", "ClipboardBridge.apk",
                lastModified: File.GetLastWriteTimeUtc(path), entityTag: etag, enableRangeProcessing: true);
        });
    }
}
