using MassTransit;

namespace PullRequestAnalyzer.Services
{
    /// <summary>
    /// Background worker service that runs alongside the API.
    /// Consumes messages from RabbitMQ in the background without blocking the API.
    /// </summary>
    public class BackgroundWorkerService : BackgroundService
    {
        private readonly ILogger<BackgroundWorkerService> _logger;
        private readonly IBusControl _busControl;

        public BackgroundWorkerService(
            ILogger<BackgroundWorkerService> logger,
            IBusControl busControl)
        {
            _logger = logger;
            _busControl = busControl;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Background Worker Service starting...");

            try
            {
                // The MassTransit bus is already configured in Program.cs
                // This service just ensures it stays alive and processes messages
                // The actual consumer (AnalyzePullRequestConsumer) is registered in Program.cs

                _logger.LogInformation("Background Worker Service is now listening for messages...");
                _logger.LogInformation("Consumer: AnalyzePullRequestConsumer");
                _logger.LogInformation("Queue: AnalyzePullRequestCommand");

                // Keep the service running until cancellation is requested
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Background Worker Service is stopping...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Background Worker Service");
                throw;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Background Worker Service stopped");
            await base.StopAsync(cancellationToken);
        }
    }
}
