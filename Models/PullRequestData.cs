using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace PullRequestAnalyzer.Models
{
    /// <summary>
    /// Represents a normalized GitHub Pull Request with all metadata, commits, and file changes.
    /// </summary>
    public class PullRequestData
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("number")]
        public int Number { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonProperty("merged_at")]
        public DateTime? MergedAt { get; set; }

        [JsonProperty("owner")]
        public string Owner { get; set; }

        [JsonProperty("repo")]
        public string Repo { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("commits")]
        public List<CommitData> Commits { get; set; } = new List<CommitData>();

        [JsonProperty("changed_files")]
        public List<ChangedFileData> ChangedFiles { get; set; } = new List<ChangedFileData>();

        [JsonProperty("additions")]
        public int Additions { get; set; }

        [JsonProperty("deletions")]
        public int Deletions { get; set; }

        [JsonProperty("changed_files_count")]
        public int ChangedFilesCount { get; set; }
    }

    /// <summary>
    /// Represents a commit within a pull request.
    /// </summary>
    public class CommitData
    {
        [JsonProperty("sha")]
        public string Sha { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("author_email")]
        public string AuthorEmail { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }
    }

    /// <summary>
    /// Represents a file changed in a pull request.
    /// </summary>
    public class ChangedFileData
    {
        [JsonProperty("filename")]
        public string Filename { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; } // "added", "modified", "deleted", "renamed"

        [JsonProperty("additions")]
        public int Additions { get; set; }

        [JsonProperty("deletions")]
        public int Deletions { get; set; }

        [JsonProperty("changes")]
        public int Changes { get; set; }

        [JsonProperty("patch")]
        public string Patch { get; set; }

        [JsonProperty("previous_filename")]
        public string PreviousFilename { get; set; }

        [JsonProperty("blob_url")]
        public string BlobUrl { get; set; }

        [JsonProperty("raw_url")]
        public string RawUrl { get; set; }
    }
}
