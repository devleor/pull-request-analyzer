using System.Text.Json;
using System.Text.RegularExpressions;
using PullRequestAnalyzer.Configuration;
using PullRequestAnalyzer.Models;

namespace PullRequestAnalyzer.Services;

public interface IJsonParsingService
{
    AnalysisResult ParseLlmResponse(string rawResponse, PullRequestData pr);
}

public class JsonParsingService : IJsonParsingService
{
    private readonly ILogger<JsonParsingService> _logger;
    private readonly ProcessingSettings _settings;
    private readonly ValidationSettings _validationSettings;

    public JsonParsingService(
        IConfiguration configuration,
        ILogger<JsonParsingService> logger)
    {
        _logger = logger;
        _settings = configuration.GetSection("Analysis:Processing").Get<ProcessingSettings>()
                    ?? new ProcessingSettings();
        _validationSettings = configuration.GetSection("Analysis:Validation").Get<ValidationSettings>()
                              ?? new ValidationSettings();
    }

    public AnalysisResult ParseLlmResponse(string rawResponse, PullRequestData pr)
    {
        try
        {
            var jsonString = ExtractJson(rawResponse);
            jsonString = FixCommonJsonErrors(jsonString);

            var result = DeserializeResult(jsonString);
            EnrichResult(result, pr);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse LLM response");
            throw new InvalidOperationException("Failed to parse analysis response", ex);
        }
    }

    private string ExtractJson(string rawResponse)
    {
        LogDebugInfo("Raw LLM response", rawResponse);

        // Extract JSON from markdown code blocks
        if (rawResponse.Contains("```json"))
        {
            return ExtractFromCodeBlock(rawResponse, "```json", 7);
        }

        if (rawResponse.Contains("```"))
        {
            return ExtractFromCodeBlock(rawResponse, "```", 3);
        }

        return rawResponse;
    }

    private string ExtractFromCodeBlock(string response, string marker, int offset)
    {
        var start = response.IndexOf(marker) + offset;
        var end = response.LastIndexOf("```");

        if (end > start)
        {
            return response.Substring(start, end - start).Trim();
        }

        return response;
    }

    private string FixCommonJsonErrors(string jsonString)
    {
        LogDebugInfo("Cleaned JSON string", jsonString);

        // Fix common LLM error: change_units as object instead of array
        if (IsChangeUnitsObject(jsonString))
        {
            _logger.LogWarning("Fixing malformed JSON: change_units is an object, converting to array");
            jsonString = ConvertChangeUnitsToArray(jsonString);
        }

        return jsonString;
    }

    private bool IsChangeUnitsObject(string json)
    {
        return json.Contains("\"change_units\": {") && !json.Contains("\"change_units\": [");
    }

    private string ConvertChangeUnitsToArray(string jsonString)
    {
        jsonString = Regex.Replace(
            jsonString,
            @"""change_units""\s*:\s*{",
            "\"change_units\": [{");

        var changeUnitsStart = jsonString.IndexOf("\"change_units\": [{");
        if (changeUnitsStart >= 0)
        {
            var position = FindClosingBrace(jsonString, changeUnitsStart);
            if (position > 0)
            {
                jsonString = jsonString.Insert(position, "]");
            }
        }

        return jsonString;
    }

    private int FindClosingBrace(string jsonString, int startIndex)
    {
        var braceCount = 1;
        var i = startIndex + "\"change_units\": [{".Length;

        while (i < jsonString.Length && braceCount > 0)
        {
            if (jsonString[i] == '{') braceCount++;
            else if (jsonString[i] == '}') braceCount--;
            i++;
        }

        return braceCount == 0 ? i : -1;
    }

    private AnalysisResult DeserializeResult(string jsonString)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        return JsonSerializer.Deserialize<AnalysisResult>(jsonString, options)
               ?? throw new InvalidOperationException("Failed to deserialize analysis result");
    }

    private void EnrichResult(AnalysisResult result, PullRequestData pr)
    {
        result.AnalysisTimestamp = DateTime.UtcNow;
        result.PrNumber = pr.Number;
        result.PrTitle = pr.Title;

        AssignUniqueIds(result);
        CalculateConfidenceScore(result);
    }

    private void AssignUniqueIds(AnalysisResult result)
    {
        foreach (var changeUnit in result.ChangeUnits)
        {
            if (string.IsNullOrEmpty(changeUnit.Id))
            {
                changeUnit.Id = Guid.NewGuid().ToString();
            }
        }
    }

    private void CalculateConfidenceScore(AnalysisResult result)
    {
        if (!result.ChangeUnits.Any())
        {
            result.ConfidenceScore = _validationSettings.MinConfidenceScore;
            return;
        }

        var scores = result.ChangeUnits
            .Select(cu => _validationSettings.ConfidenceLevels
                .GetValueOrDefault(cu.ConfidenceLevel?.ToLower() ?? "low",
                                 _validationSettings.MinConfidenceScore))
            .ToList();

        result.ConfidenceScore = scores.Average();
    }

    private void LogDebugInfo(string label, string content)
    {
        if (content.Length > _settings.MaxLogLength)
        {
            _logger.LogDebug("{Label} (first {MaxLength} chars): {Content}",
                label, _settings.MaxLogLength,
                content.Substring(0, _settings.MaxLogLength) + "...");
        }
        else
        {
            _logger.LogDebug("{Label}: {Content}", label, content);
        }
    }
}