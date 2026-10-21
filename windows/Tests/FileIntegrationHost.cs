using System.Net;
using System.Security.Cryptography;
using System.Text;
using ClipboardBridge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

/// <summary>Loopback-only interoperability fixture. Never included in the installed service.</summary>
static class FileIntegrationHost
{
    sealed class OfflinePush : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
    }
    public static async Task Run(string root)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root)) throw new IOException("Use a new, absolute test directory.");
        Directory.CreateDirectory(root);
        string key = Convert.ToBase64String(new byte[32]), generation = new string('a', 32);
        var vault = new Vault(Path.Combine(root, "vault"));
        vault.Update(c => { c.Key = key; c.PhoneGeneration = generation; c.Subscription = WebPushChecks.Subscription(); });
        using var http = new HttpClient(new OfflinePush());
        using var transfers = new FileTransfers(vault, new WebPushSender(http));
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:45879"); builder.Services.AddSingleton(transfers);
        await using var app = builder.Build(); FileTransfers.Map(app);
        var savedBatches = new List<object>();
        app.MapGet("/test/config", () => new { key, generation, endpoints = new[] { "http://10.0.2.2:45879" } });
        app.MapGet("/test/saved", () => savedBatches.ToArray());
        app.MapPost("/test/save", async () =>
        {
            var response = await transfers.Local(new("files-incoming"), default);
            if (response.Value is null) return Results.NotFound();
            var incoming = Bridge.Decode<FileInboxBatch>(Encoding.UTF8.GetBytes(response.Value));
            string destination = Path.Combine(root, "Downloads");
            string[] paths = FileInbox.Save(incoming, destination, Path.Combine(root, "receipts"));
            savedBatches.Add(paths.Select(p => new { name = Path.GetFileName(p), size = new FileInfo(p).Length, sha256 = FileProtocol.HashFile(p) }).ToArray());
            await transfers.Local(new("files-incoming-ack", Value: incoming.Id), default);
            return Results.Ok();
        });
        app.MapPost("/test/offer", async () =>
        {
            string sources = Path.Combine(root, "sources"); Directory.CreateDirectory(sources);
            string first = Path.Combine(sources, "跨端大文件.bin"), second = Path.Combine(sources, "empty.txt");
            if (!File.Exists(first))
            {
                byte[] chunk = RandomNumberGenerator.GetBytes(FileProtocol.ChunkBytes);
                using var output = File.Create(first); for (int i = 0; i < 69; i++) output.Write(chunk);
                File.WriteAllBytes(second, []);
            }
            string[] paths = [first, second];
            var response = await transfers.Send(FileTransfers.OpenFiles(paths), default);
            if (!response.Ok) return Results.BadRequest(response.Message);
            string id = response.Value!;
            var request = new FileRequest("offer", id, FileProtocol.NewId(), DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(), generation);
            var offer = Bridge.Decode<FileResponse>(Bridge.Open((await transfers.Request(new(Bridge.Seal(Bridge.Encode(request), key)), default)).Data, key));
            var batch = offer.Batch!;
            var wake = new ClipManifest(batch.Id, batch.Sequence, FileProtocol.Mime, batch.ExpiresAt, "", 0, null, null, generation);
            return Results.Json(new { box = Bridge.Seal(Bridge.Encode(wake), key), id, files = paths.Select(p => new { name = Path.GetFileName(p), size = new FileInfo(p).Length, sha256 = FileProtocol.HashFile(p) }) });
        });
        app.MapGet("/test/status", async () => Results.Text((await transfers.Local(new("files-status"), default)).Value!, "application/json"));
        await app.StartAsync(); Console.WriteLine("File interoperability host ready on loopback:45879 (isolated public test key).");
        await app.WaitForShutdownAsync();
    }
}
