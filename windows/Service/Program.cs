using ClipboardBridge;

int dataArgument = Array.IndexOf(args, "--data-dir");
if (dataArgument >= 0)
{
    if (dataArgument + 1 >= args.Length || !Path.IsPathFullyQualified(args[dataArgument + 1]))
        throw new ArgumentException("--data-dir 必须指定绝对路径");
    Environment.SetEnvironmentVariable("CLIPBOARD_BRIDGE_DATA", Path.GetFullPath(args[dataArgument + 1]));
}
int ownerArgument = Array.IndexOf(args, "--initialize-owner");
if (ownerArgument >= 0)
{
    if (ownerArgument + 1 >= args.Length) throw new ArgumentException("缺少用户 SID");
    var owner = new System.Security.Principal.SecurityIdentifier(args[ownerArgument + 1]);
    new Vault().Update(c => c.OwnerSid = owner.Value);
    return;
}
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o => o.ServiceName = "ClipboardBridge");
builder.WebHost.UseUrls("http://0.0.0.0:45871");
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 12 * 1024);
builder.Logging.ClearProviders();
builder.Services.AddSingleton<Vault>();
builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
builder.Services.AddSingleton<FcmSender>(); builder.Services.AddSingleton<Broker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Broker>());
var app = builder.Build();
app.MapGet("/health", () => Results.Json(new { service = "ClipboardBridge", version = 1 }));
app.MapPost("/api/register", async (Box body, Vault vault) =>
{
    try
    {
        var device = Bridge.Decode<Device>(Bridge.Open(body.Data, vault.Read().Key));
        if (device.Token.Length is < 40 or > 4096 || device.DeviceName.Length > 160) return Results.BadRequest();
        vault.Update(c => { c.DeviceToken = device.Token; c.DeviceName = device.DeviceName; }); return Results.Ok();
    }
    catch (Exception e) when (e is FormatException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException) { return Results.BadRequest(); }
});
app.MapGet("/blob/{id}", async (string id, Broker broker, CancellationToken ct) =>
{
    if (id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c))) return Results.NotFound();
    var blob = await broker.Blob(id, ct); return blob is null ? Results.NotFound() : Results.Text(blob, "text/plain");
});
app.MapPost("/api/ack", async (Box body, Vault vault, Broker broker, CancellationToken ct) =>
{
    try { await broker.AcceptReceipt(Bridge.Decode<Receipt>(Bridge.Open(body.Data, vault.Read().Key)), ct); return Results.Ok(); }
    catch (Exception e) when (e is FormatException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException) { return Results.BadRequest(); }
});
await app.RunAsync();
