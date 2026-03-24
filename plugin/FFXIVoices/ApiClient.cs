using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FFXIVoices;

/// <summary>Handles non-auth API calls to CommsLink (voices, profile, etc.).</summary>
public sealed class ApiClient : IDisposable
{
    private readonly Configuration config;
    private readonly IPluginLog log;

    // Cached data
    public List<VoiceInfo> Voices { get; private set; } = new();
    public bool VoicesLoaded { get; private set; }
    public string? LastError { get; private set; }
    public int Credits { get; private set; } = -1;
    public DateTime? NextFreeCredits { get; private set; }

    public ApiClient(Configuration config, IPluginLog log)
    {
        this.config = config;
        this.log = log;
    }

    private HttpClient CreateClient(bool withAuth = false)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        if (withAuth && !string.IsNullOrEmpty(config.AuthToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.AuthToken);
        }
        return client;
    }

    /// <summary>Fetch available voices from GET /voices.</summary>
    public async Task FetchVoicesAsync()
    {
        try
        {
            using var client = CreateClient();
            var response = await client.GetAsync($"{config.ServerUrl}/voices");
            var body = await response.Content.ReadAsStringAsync();
            log.Information("[CommsLink Voices] Voices response: {0} {1}", (int)response.StatusCode, body);

            if (response.IsSuccessStatusCode)
            {
                var voices = JsonSerializer.Deserialize<List<VoiceInfo>>(body);
                if (voices != null)
                {
                    Voices = voices;
                    VoicesLoaded = true;
                    LastError = null;
                    log.Information("[CommsLink Voices] Loaded {0} voices", Voices.Count);
                }
            }
            else
            {
                LastError = $"Voices: {response.StatusCode}";
                log.Warning("[CommsLink Voices] Failed to fetch voices: {0}", body);
            }
        }
        catch (Exception ex)
        {
            LastError = $"Voices error: {ex.Message}";
            log.Error("[CommsLink Voices] Voices fetch error: {0}", ex.Message);
        }
    }

    /// <summary>Select a voice via PUT /voices/select. Returns error message or null on success.</summary>
    public async Task<string?> SelectVoiceAsync(string voiceId)
    {
        try
        {
            using var client = CreateClient(withAuth: true);
            var payload = JsonSerializer.Serialize(new { voiceId });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var url = $"{config.ServerUrl}/voices/select";
            log.Information("[CommsLink Voices] PUT {0} body={1}", url, payload);

            var response = await client.PutAsync(url, content);
            var body = await response.Content.ReadAsStringAsync();
            log.Information("[CommsLink Voices] Voice select response: {0} {1}", (int)response.StatusCode, body);

            if (response.IsSuccessStatusCode)
            {
                return null; // success
            }

            return $"Failed: {body}";
        }
        catch (Exception ex)
        {
            log.Error("[CommsLink Voices] Voice select error: {0}", ex.Message);
            return ex.Message;
        }
    }

    /// <summary>Fetch current credit balance from GET /me or /credits.</summary>
    public async Task FetchCreditsAsync()
    {
        try
        {
            using var client = CreateClient(withAuth: true);
            var response = await client.GetAsync($"{config.ServerUrl}/me");
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("credit_balance", out var cb))
                    Credits = cb.GetInt32();
                else if (doc.RootElement.TryGetProperty("credits", out var cr))
                    Credits = cr.GetInt32();

                // Parse next free credit date (could be "next_free_credits", "next_free_credit_date", etc.)
                foreach (var name in new[] { "next_free_credits", "next_free_credit_date", "next_credit_refresh" })
                {
                    if (doc.RootElement.TryGetProperty(name, out var nfc) && nfc.ValueKind == JsonValueKind.String)
                    {
                        if (DateTime.TryParse(nfc.GetString(), out var dt))
                            NextFreeCredits = dt;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning("[CommsLink Voices] Credits fetch error: {0}", ex.Message);
        }
    }

    public void Dispose() { }
}

public class VoiceInfo
{
    [JsonPropertyName("voice_id")]
    public string VoiceId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "polly";

    [JsonPropertyName("credit_cost")]
    public int CreditCost { get; set; }
}
