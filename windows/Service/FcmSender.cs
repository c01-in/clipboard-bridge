using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClipboardBridge;

public sealed class FcmSender(HttpClient http)
{
    private string? accessToken, identity;
    private DateTimeOffset expires;
    private static string Url64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public async Task Send(Configuration config, string envelope, CancellationToken ct)
    {
        if (config.ServiceAccountJson is null) throw new InvalidOperationException("请先导入 Firebase 发送凭据");
        if (config.DeviceToken is null) throw new InvalidOperationException("请先配对 Android 手机");
        using var account = JsonDocument.Parse(config.ServiceAccountJson);
        string project = account.RootElement.GetProperty("project_id").GetString()!, email = account.RootElement.GetProperty("client_email").GetString()!;
        if (accessToken is null || identity != email || expires <= DateTimeOffset.UtcNow.AddMinutes(2))
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = Url64(Bridge.Encode(new { alg = "RS256", typ = "JWT" }));
            var claims = Url64(Bridge.Encode(new { iss = email, scope = "https://www.googleapis.com/auth/firebase.messaging", aud = "https://oauth2.googleapis.com/token", iat = now, exp = now + 3600 }));
            string unsigned = header + "." + claims;
            using var rsa = RSA.Create(); rsa.ImportFromPem(account.RootElement.GetProperty("private_key").GetString()!);
            string jwt = unsigned + "." + Url64(rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            using var result = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer", ["assertion"] = jwt }), ct);
            if (!result.IsSuccessStatusCode) throw new InvalidOperationException($"Google 身份认证失败（{(int)result.StatusCode}），请检查发送凭据");
            using var token = JsonDocument.Parse(await result.Content.ReadAsStringAsync(ct));
            accessToken = token.RootElement.GetProperty("access_token").GetString(); identity = email;
            expires = DateTimeOffset.UtcNow.AddSeconds(token.RootElement.GetProperty("expires_in").GetInt32());
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(project)}/messages:send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new ByteArrayContent(Bridge.Encode(new { message = new { token = config.DeviceToken, data = new { v = "1", box = envelope }, android = new { priority = "HIGH", ttl = "600s", collapse_key = "clipboard-bridge" } } }));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            string status = "";
            try { using var e = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); status = e.RootElement.GetProperty("error").GetProperty("status").GetString() ?? ""; } catch (JsonException) { }
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) accessToken = null;
            throw new InvalidOperationException($"FCM 返回 {(int)response.StatusCode} {status}，未确认手机收到");
        }
    }
}
