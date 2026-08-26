// ============================================================
//  ReviewResult — the structured output of the PR Review Agent
//
//  This typed record replaces the raw text verdict from Step 3.
//  Benefits:
//  • Deserializable — reliable, no string parsing
//  • Drives logic   — MergeAction tells the system what to do
//  • Serializable   — ready to POST to GitHub/GitLab API
//  • Testable       — you can write unit tests against it
// ============================================================

using System.Text.Json.Serialization;

namespace PRReviewAgent.Models;

/// <summary>
/// The final structured verdict produced by the PR Review Agent.
/// </summary>
public record ReviewResult
{
    /// <summary>APPROVED | CHANGES_REQUESTED | BLOCKED</summary>
    [JsonPropertyName("verdict")]
    public string Verdict { get; init; } = "CHANGES_REQUESTED";

    /// <summary>Confidence score 0.0 → 1.0. Above 0.95 = eligible for auto-merge.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>LOW | MEDIUM | HIGH</summary>
    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; init; } = "MEDIUM";

    /// <summary>Blocking issues — must be fixed before merge.</summary>
    [JsonPropertyName("requiredChanges")]
    public List<string> RequiredChanges { get; init; } = [];

    /// <summary>Non-blocking suggestions — nice to have.</summary>
    [JsonPropertyName("suggestions")]
    public List<string> Suggestions { get; init; } = [];

    /// <summary>AUTO_MERGE | HUMAN_REVIEW | BLOCK</summary>
    [JsonPropertyName("mergeAction")]
    public string MergeAction { get; init; } = "HUMAN_REVIEW";

    /// <summary>One-line summary the agent posts back to the PR as a comment.</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;
}
