using System.Net.Http.Headers;

namespace ClipboardBridge;

public sealed class WebPushSender(HttpClient http)
{
    public async Task Send(Configuration config, string envelope, CancellationToken ct)
    {
        if (config.Subscription is null) throw new InvalidOperationException(UiText.Get("push_not_registered"));
        if (config.PhonePaused) throw new InvalidOperationException(UiText.Get("phone_paused"));
        WebPush.Validate(config.Subscription);
        using var request = new HttpRequestMessage(HttpMethod.Post, config.Subscription.Endpoint);
        request.Headers.TryAddWithoutValidation("Authorization", WebPush.Authorization(config.VapidPrivateKey));
        request.Headers.TryAddWithoutValidation("TTL", "600"); request.Headers.TryAddWithoutValidation("Urgency", "high");
        request.Content = new ByteArrayContent(WebPush.Encrypt(Bridge.Encode(new { v = "1", box = envelope }), config.Subscription));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(UiText.Get("webpush_error", (int)response.StatusCode));
    }
}
