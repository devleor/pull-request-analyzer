using MassTransit;
using PullRequestAnalyzer.Messages;
using PullRequestAnalyzer.Services;

namespace PullRequestAnalyzer.Consumers
{
    /// <summary>
    /// MassTransit consumer that processes pull request analysis commands.
    /// </summary>
    public class AnalyzePullRequestConsumer : IConsumer<AnalyzePullRequestCommand>
    {
        private readonly LLMAnalysisService _analysisService;
        private readonly ILogger<AnalyzePullRequestConsumer> _logger;
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly JobStatusService _jobStatusService;
        private readonly WebhookService _webhookService;

        public AnalyzePullRequestConsumer(
            LLMAnalysisService analysisService,
            ILogger<AnalyzePullRequestConsumer> logger,
            IPublishEndpoint publishEndpoint,
            JobStatusService jobStatusService,
            WebhookService webhookService)
        {
            _analysisService = analysisService;
            _logger = logger;
            _publishEndpoint = publishEndpoint;
            _jobStatusService = jobStatusService;
            _webhookService = webhookService;
        }

        public async Task Consume(ConsumeContext<AnalyzePullRequestCommand> context)
        {
            var command = context.Message;
            _logger.LogInformation($"[Job {command.JobId}] Starting analysis for PR #{command.PullRequestData.Number}");

            try
            {
                // Update job status to "processing"
                await _jobStatusService.UpdateJobStatusAsync(command.JobId, "processing");

                // Perform the analysis
                var analysisResult = await _analysisService.AnalyzePullRequestAsync(command.PullRequestData);

                // Update job status to "completed"
                await _jobStatusService.UpdateJobStatusAsync(command.JobId, "completed", analysisResult);

                // Publish success event
                var successEvent = new PullRequestAnalyzedEvent
                {
                    JobId = command.JobId,
                    PrNumber = command.PullRequestData.Number,
                    AnalysisResult = analysisResult
                };

                await _publishEndpoint.Publish(successEvent);
                _logger.LogInformation($"[Job {command.JobId}] Analysis completed successfully");

                // Send webhook notification if provided
                if (!string.IsNullOrEmpty(command.WebhookUrl))
                {
                    await _webhookService.SendWebhookAsync(command.WebhookUrl, successEvent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[Job {command.JobId}] Analysis failed");

                // Update job status to "failed"
                await _jobStatusService.UpdateJobStatusAsync(command.JobId, "failed", errorMessage: ex.Message);

                // Publish failure event
                var failureEvent = new PullRequestAnalysisFailedEvent
                {
                    JobId = command.JobId,
                    PrNumber = command.PullRequestData.Number,
                    ErrorMessage = ex.Message
                };

                await _publishEndpoint.Publish(failureEvent);

                // Send webhook notification if provided
                if (!string.IsNullOrEmpty(command.WebhookUrl))
                {
                    await _webhookService.SendWebhookAsync(command.WebhookUrl, failureEvent);
                }

                throw;
            }
        }
    }
}
