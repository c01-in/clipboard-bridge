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
builder.Services.AddSingleton<PairingHub>();
builder.Services.AddHostedService<DiscoveryService>();
builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
builder.Services.AddSingleton<FcmSender>(); builder.Services.AddSingleton<Broker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Broker>());
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.Equals("/api/clipboard", StringComparison.OrdinalIgnoreCase))
    {
        var bodyLimit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is not null && !bodyLimit.IsReadOnly) bodyLimit.MaxRequestBodySize = Bridge.MaxBytes * 2L;
    }
    await next(context);
});
app.MapGet("/health", () => Results.Json(new { service = "ClipboardBridge", version = 1 }));
app.MapGet("/app/android", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    string apk = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "android", "ClipboardBridge.apk"));
    return File.Exists(apk) ? Results.File(apk, "application/vnd.android.package-archive", "ClipboardBridge.apk") : Results.NotFound();
});
app.MapPost("/api/pairing/{id}/confirm", (string id, Box body, PairingHub hub, HttpContext context) =>
    PairResult(() => hub.ConfirmPhone(id, body, context.Connection.RemoteIpAddress!.MapToIPv4())));
app.MapGet("/api/pairing/{id}/configuration", (string id, PairingHub hub, HttpContext context) =>
    PairResult(() => hub.Configuration(id, context.Connection.RemoteIpAddress!.MapToIPv4(), "http://" + context.Connection.LocalIpAddress!.MapToIPv4() + ":45871")));
app.MapPost("/api/pairing/{id}/complete", async (string id, Box body, Broker broker, HttpContext context, CancellationToken ct) =>
{
    try { return Results.Json(await broker.CompletePairing(id, body, context.Connection.RemoteIpAddress!.MapToIPv4(), ct), Bridge.Json); }
    catch (Exception e) when (e is InvalidDataException or InvalidOperationException or System.Security.Cryptography.CryptographicException or FormatException or System.Text.Json.JsonException or ArgumentException)
    { return Results.Json(new { error = "配对请求无效、未确认或已过期" }, statusCode: 409); }
});
app.MapPost("/api/register", async (Box body, Broker broker, CancellationToken ct) =>
{
    try
    {
        var ack = await broker.Register(body, ct);
        return ack is null ? Results.Ok() : Results.Json(ack, Bridge.Json);
    }
    catch (Exception e) when (e is FormatException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or InvalidDataException or ArgumentException) { return Results.BadRequest(); }
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
app.MapPost("/api/clipboard", async (ClipboardUpload body, Vault vault, Broker broker, CancellationToken ct) =>
{
    try
    {
        var key = vault.Read().Key;
        var (manifest, bytes) = UploadContent.Open(body, key);
        var receipt = await broker.ReceiveFromAndroid(manifest, bytes, ct);
        return Results.Json(new Box(Bridge.Seal(Bridge.Encode(receipt), key)), Bridge.Json);
    }
    catch (Exception e) when (e is FormatException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or InvalidDataException or System.Text.DecoderFallbackException)
    { return Results.BadRequest(); }
});
await app.RunAsync();

static IResult PairResult(Func<Box> action)
{
    try { return Results.Json(action(), Bridge.Json); }
    catch (Exception e) when (e is InvalidDataException or InvalidOperationException or System.Security.Cryptography.CryptographicException or FormatException or System.Text.Json.JsonException or ArgumentException)
    { return Results.Json(new { error = "配对请求无效、未确认或已过期" }, statusCode: 409); }
}
