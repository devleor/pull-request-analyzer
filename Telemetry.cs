using System.Diagnostics;

namespace PullRequestAnalyzer;

public static class Telemetry
{
    public const string ServiceName    = "pull-request-analyzer";
    public const string ServiceVersion = "1.0.0";

    public static readonly ActivitySource Source = new(ServiceName, ServiceVersion);
}
