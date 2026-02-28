using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PullRequestAnalyzer.Models;
using PullRequestAnalyzer.Services;

namespace PullRequestAnalyzer
{
    /// <summary>
    /// Console application to generate PR JSON data from GitHub.
    /// Usage: dotnet run --project . -- <github_token> <owner> <repo> <pr_number> <output_file>
    /// </summary>
    class GeneratePRData
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 5)
            {
                Console.WriteLine("Usage: dotnet run -- <github_token> <owner> <repo> <pr_number> <output_file>");
                Console.WriteLine("Example: dotnet run -- ghp_xxxx mindsdb mindsdb 1234 pr_data.json");
                return;
            }

            string githubToken = args[0];
            string owner = args[1];
            string repo = args[2];
            int prNumber = int.Parse(args[3]);
            string outputFile = args[4];

            try
            {
                Console.WriteLine($"Fetching PR {owner}/{repo}#{prNumber}...");
                var service = new GitHubIngestService(githubToken);
                var prData = await service.FetchPullRequestAsync(owner, repo, prNumber);

                Console.WriteLine($"Successfully fetched PR data. Writing to {outputFile}...");

                var json = JsonConvert.SerializeObject(prData, Formatting.Indented);
                File.WriteAllText(outputFile, json);

                Console.WriteLine($"✓ PR data saved to {outputFile}");
                Console.WriteLine($"  - PR Title: {prData.Title}");
                Console.WriteLine($"  - Commits: {prData.Commits.Count}");
                Console.WriteLine($"  - Changed Files: {prData.ChangedFilesCount}");
                Console.WriteLine($"  - Additions: {prData.Additions}, Deletions: {prData.Deletions}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }
}
