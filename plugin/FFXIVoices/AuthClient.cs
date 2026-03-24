using Dalamud.Plugin.Services;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FFXIVoices;

public sealed class AuthClient : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly Configuration config;
    private readonly IPluginLog log;

    public bool IsLoggedIn => !string.IsNullOrEmpty(config.AuthToken);
    public string? Token => config.AuthToken;

    public AuthClient(Configuration config, IPluginLog log)
    {
        this.config = config;
        this.log = log;
        this.httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<AuthResult> RegisterAsync(string username, string password, string? contentId, string? charName, string? gender = null)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { username, password, contentId, charName, gender });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync($"{config.ServerUrl}/register", content);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<AuthResponse>(body);
                if (result?.Token != null)
                {
                    config.AuthToken = result.Token;
                    config.Username = username;
                    return new AuthResult(true, "Registered successfully");
                }
            }

            return new AuthResult(false, ParseError(body) ?? "Registration failed");
        }
        catch (Exception ex)
        {
            log.Error("[CommsLink Voices] Register error: {0}", ex.Message);
            return new AuthResult(false, ex.Message);
        }
    }

    public async Task<AuthResult> LoginAsync(string username, string password, string? contentId, string? charName, string? gender = null)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { username, password, contentId, charName, gender });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync($"{config.ServerUrl}/login", content);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<AuthResponse>(body);
                if (result?.Token != null)
                {
                    config.AuthToken = result.Token;
                    config.Username = username;
                    return new AuthResult(true, "Logged in successfully");
                }
            }

            return new AuthResult(false, ParseError(body) ?? "Login failed");
        }
        catch (Exception ex)
        {
            log.Error("[CommsLink Voices] Login error: {0}", ex.Message);
            return new AuthResult(false, ex.Message);
        }
    }

    public HttpClient GetAuthedClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        if (!string.IsNullOrEmpty(config.AuthToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.AuthToken);
        }
        return client;
    }

    public void Logout()
    {
        config.AuthToken = null;
    }

    /// <summary>Parse error from CommsLink response (supports both {error} and Hapi {message} formats).</summary>
    private static string? ParseError(string body)
    {
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var errProp))
                return errProp.GetString();
            if (doc.RootElement.TryGetProperty("message", out var msgProp))
                return msgProp.GetString();
        }
        catch { }
        return null;
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}

public record AuthResult(bool Success, string Message);

public class AuthResponse
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}
