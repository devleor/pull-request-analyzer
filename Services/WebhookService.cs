using System.Text;
using System.Text.Json;

namespace PullRequestAnalyzer.Services;

public sealed class WebhookService
{
    private readonly HttpClient _http;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(IHttpClientFactory httpFactory, ILogger<WebhookService> logger)
    {
        _http   = httpFactory.CreateClient("webhook");
        _logger = logger;
    }

    public async Task SendAsync(string url, object payload)
    {
        try
        {
            var json     = JsonSerializer.Serialize(payload);
            var content  = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Webhook to {Url} returned {Status}", url, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send webhook to {Url}", url);
        }
    }
}
