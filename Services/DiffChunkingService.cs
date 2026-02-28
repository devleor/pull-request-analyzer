using System;
using System.Collections.Generic;
using System.Linq;
using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services
{
    /// <summary>
    /// Service to handle chunking of large diffs to fit within LLM token limits.
    /// </summary>
    public class DiffChunkingService
    {
        private const int MAX_TOKENS_PER_CHUNK = 3000; // Conservative estimate: ~4 chars per token
        private const int CHARS_PER_TOKEN = 4;
        private const int MAX_CHARS_PER_CHUNK = MAX_TOKENS_PER_CHUNK * CHARS_PER_TOKEN;

        /// <summary>
        /// Chunks a list of changed files based on token limits.
        /// Returns a list of file groups that fit within token limits.
        /// </summary>
        public List<List<ChangedFileData>> ChunkFiles(List<ChangedFileData> files)
        {
            var chunks = new List<List<ChangedFileData>>();
            var currentChunk = new List<ChangedFileData>();
            int currentChunkSize = 0;

            foreach (var file in files)
            {
                int fileSize = EstimateFileSize(file);

                // If adding this file would exceed the limit, start a new chunk
                if (currentChunkSize + fileSize > MAX_CHARS_PER_CHUNK && currentChunk.Count > 0)
                {
                    chunks.Add(currentChunk);
                    currentChunk = new List<ChangedFileData>();
                    currentChunkSize = 0;
                }

                currentChunk.Add(file);
                currentChunkSize += fileSize;
            }

            // Add the last chunk if it has content
            if (currentChunk.Count > 0)
            {
                chunks.Add(currentChunk);
            }

            return chunks;
        }

        /// <summary>
        /// Truncates a diff patch to fit within token limits.
        /// </summary>
        public string TruncatePatch(string patch, int maxChars = MAX_CHARS_PER_CHUNK)
        {
            if (string.IsNullOrEmpty(patch))
                return patch;

            if (patch.Length <= maxChars)
                return patch;

            // Keep the first part of the patch and indicate truncation
            var truncated = patch.Substring(0, maxChars - 100);
            truncated += "\n\n... (patch truncated for token limit) ...";
            return truncated;
        }

        /// <summary>
        /// Estimates the size of a file in characters (for token estimation).
        /// </summary>
        private int EstimateFileSize(ChangedFileData file)
        {
            int size = 0;
            size += file.Filename?.Length ?? 0;
            size += file.Status?.Length ?? 0;
            size += file.Patch?.Length ?? 0;
            size += 100; // Overhead for metadata

            return size;
        }

        /// <summary>
        /// Creates a summary of file changes without full diffs.
        /// Useful for large PRs where full diffs would exceed token limits.
        /// </summary>
        public ChangedFileData CreateSummaryFile(ChangedFileData file)
        {
            return new ChangedFileData
            {
                Filename = file.Filename,
                Status = file.Status,
                Additions = file.Additions,
                Deletions = file.Deletions,
                Changes = file.Changes,
                Patch = $"[Diff truncated: +{file.Additions}/-{file.Deletions} changes]",
                PreviousFilename = file.PreviousFilename,
                BlobUrl = file.BlobUrl,
                RawUrl = file.RawUrl
            };
        }

        /// <summary>
        /// Estimates total tokens needed for a PR.
        /// </summary>
        public int EstimateTotalTokens(PullRequestData prData)
        {
            int totalChars = 0;

            // PR metadata
            totalChars += prData.Title?.Length ?? 0;
            totalChars += prData.Description?.Length ?? 0;

            // Commits
            foreach (var commit in prData.Commits)
            {
                totalChars += commit.Message?.Length ?? 0;
            }

            // Files
            foreach (var file in prData.ChangedFiles)
            {
                totalChars += EstimateFileSize(file);
            }

            return (int)Math.Ceiling(totalChars / (double)CHARS_PER_TOKEN);
        }
    }
}
