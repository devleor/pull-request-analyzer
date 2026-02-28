using Newtonsoft.Json;

namespace PullRequestAnalyzer.Services
{
    /// <summary>
    /// Service to send webhook notifications when analysis is completed.
    /// </summary>
    public class WebhookService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<WebhookService> _logger;

        public WebhookService(ILogger<WebhookService> logger)
        {
            _logger = logger;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task SendWebhookAsync(string webhookUrl, object payload)
        {
            try
            {
                _logger.LogInformation($"Sending webhook to {webhookUrl}");

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(webhookUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Webhook sent successfully to {webhookUrl}");
                }
                else
                {
                    _logger.LogWarning($"Webhook failed with status {response.StatusCode} to {webhookUrl}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending webhook to {webhookUrl}");
            }
        }
    }
}
