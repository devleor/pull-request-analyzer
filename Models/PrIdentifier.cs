namespace PullRequestAnalyzer.Models;

public sealed record PrIdentifier(string Owner, string Repo, int Number)
{
    public override string ToString() => $"{Owner}/{Repo}#{Number}";
    public string CacheKey(string prefix) => $"{prefix}{Owner}/{Repo}/{Number}";
}
