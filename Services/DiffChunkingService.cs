using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services;

public sealed class DiffChunkingService
{
    private const int CharsPerToken     = 4;
    private const int MaxTokensPerChunk = 3000;
    private const int MaxCharsPerChunk  = MaxTokensPerChunk * CharsPerToken;

    public List<List<ChangedFileData>> ChunkFiles(List<ChangedFileData> files)
    {
        var chunks       = new List<List<ChangedFileData>>();
        var currentChunk = new List<ChangedFileData>();
        var currentSize  = 0;

        foreach (var file in files)
        {
            var fileSize = EstimateSize(file);

            if (currentSize + fileSize > MaxCharsPerChunk && currentChunk.Count > 0)
            {
                chunks.Add(currentChunk);
                currentChunk = [];
                currentSize  = 0;
            }

            currentChunk.Add(file);
            currentSize += fileSize;
        }

        if (currentChunk.Count > 0)
            chunks.Add(currentChunk);

        return chunks;
    }

    public string TruncatePatch(string patch, int maxChars = MaxCharsPerChunk)
    {
        if (string.IsNullOrEmpty(patch) || patch.Length <= maxChars)
            return patch;

        return patch[..(maxChars - 50)] + "\n... (truncated)";
    }

    public int EstimateTotalTokens(PullRequestData pr)
    {
        var totalChars =
            (pr.Title?.Length ?? 0) +
            (pr.Description?.Length ?? 0) +
            pr.Commits.Sum(c => c.Message?.Length ?? 0) +
            pr.ChangedFiles.Sum(EstimateSize);

        return (int)Math.Ceiling(totalChars / (double)CharsPerToken);
    }

    private static int EstimateSize(ChangedFileData file) =>
        (file.Filename?.Length ?? 0) +
        (file.Status?.Length   ?? 0) +
        (file.Patch?.Length    ?? 0) +
        100;
}
