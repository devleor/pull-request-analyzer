using System.Text.Json.Serialization;

namespace PullRequestAnalyzer.Models;

public sealed class PullRequestData
{
    [JsonPropertyName("id")]          public long   Id               { get; set; }
    [JsonPropertyName("number")]      public int    Number           { get; set; }
    [JsonPropertyName("title")]       public string Title            { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description      { get; set; } = string.Empty;
    [JsonPropertyName("state")]       public string State            { get; set; } = string.Empty;
    [JsonPropertyName("author")]      public string Author           { get; set; } = string.Empty;
    [JsonPropertyName("created_at")]  public DateTime CreatedAt      { get; set; }
    [JsonPropertyName("updated_at")]  public DateTime UpdatedAt      { get; set; }
    [JsonPropertyName("merged_at")]   public DateTime? MergedAt      { get; set; }
    [JsonPropertyName("owner")]       public string Owner            { get; set; } = string.Empty;
    [JsonPropertyName("repo")]        public string Repo             { get; set; } = string.Empty;
    [JsonPropertyName("url")]         public string Url              { get; set; } = string.Empty;
    [JsonPropertyName("additions")]   public int    Additions        { get; set; }
    [JsonPropertyName("deletions")]   public int    Deletions        { get; set; }
    [JsonPropertyName("changed_files_count")] public int ChangedFilesCount { get; set; }

    [JsonPropertyName("commits")]
    public List<CommitData> Commits { get; set; } = [];

    [JsonPropertyName("changed_files")]
    public List<ChangedFileData> ChangedFiles { get; set; } = [];

    public PrIdentifier ToIdentifier() => new(Owner, Repo, Number);
}

public sealed class CommitData
{
    [JsonPropertyName("sha")]          public string   Sha         { get; set; } = string.Empty;
    [JsonPropertyName("message")]      public string   Message     { get; set; } = string.Empty;
    [JsonPropertyName("author")]       public string   Author      { get; set; } = string.Empty;
    [JsonPropertyName("author_email")] public string   AuthorEmail { get; set; } = string.Empty;
    [JsonPropertyName("timestamp")]    public DateTime Timestamp   { get; set; }
    [JsonPropertyName("url")]          public string   Url         { get; set; } = string.Empty;
}

public sealed class ChangedFileData
{
    [JsonPropertyName("filename")]          public string Filename         { get; set; } = string.Empty;
    [JsonPropertyName("status")]            public string Status           { get; set; } = string.Empty;
    [JsonPropertyName("additions")]         public int    Additions        { get; set; }
    [JsonPropertyName("deletions")]         public int    Deletions        { get; set; }
    [JsonPropertyName("changes")]           public int    Changes          { get; set; }
    [JsonPropertyName("patch")]             public string Patch            { get; set; } = string.Empty;
    [JsonPropertyName("previous_filename")] public string PreviousFilename { get; set; } = string.Empty;
    [JsonPropertyName("blob_url")]          public string BlobUrl          { get; set; } = string.Empty;
    [JsonPropertyName("raw_url")]           public string RawUrl           { get; set; } = string.Empty;
}
