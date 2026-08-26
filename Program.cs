// ============================================================
//  PR Review Agent — Step 6: GitHub API Integration
//
//  What's new vs Step 5:
//  • GitHubService fetches a real PR diff via GitHub API
//  • After the review, agent posts a formatted comment to the PR
//  • Agent submits an official GitHub review (approve/block)
//  • If AUTO_MERGE: agent calls the GitHub merge API directly
//  • Falls back to mock mode if GITHUB_TOKEN is not set
//
//  Full pipeline:
//    PR opened → agent fetches diff → ReAct review loop
//    → structured verdict → human gate → GitHub actions
//    → audit log written to disk
//
//  To use with a real repo:
//    export GITHUB_TOKEN="ghp_your_token"
//    export GITHUB_OWNER="your-username"
//    export GITHUB_REPO="your-repo"
//    export GITHUB_PR_NUMBER="1"
// ============================================================

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using PRReviewAgent.Models;
using PRReviewAgent.Plugins;
using PRReviewAgent.Services;
using System.Text.Json;

// ── 1. Configuration from environment variables ──────────────
string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException(
        "OPENAI_API_KEY not set. Run: export OPENAI_API_KEY=\"sk-...\"");

string? githubToken  = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
string  githubOwner  = Environment.GetEnvironmentVariable("GITHUB_OWNER")  ?? "demo-owner";
string  githubRepo   = Environment.GetEnvironmentVariable("GITHUB_REPO")   ?? "demo-repo";
int     prNumber     = int.TryParse(
                           Environment.GetEnvironmentVariable("GITHUB_PR_NUMBER"), out var n)
                       ? n : 42;

// ── 2. Build the Kernel ──────────────────────────────────────
var httpHandler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
    {
        if (errors == System.Net.Security.SslPolicyErrors.None) return true;
        if (errors == System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors && chain != null)
        {
            foreach (var status in chain.ChainStatus)
            {
                if (status.Status != System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.RevocationStatusUnknown
                 && status.Status != System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.OfflineRevocation)
                    return false;
            }
            return true;
        }
        return false;
    }
};
var httpClient   = new HttpClient(httpHandler);
var reviewPlugin = new CodeReviewPlugin(interactiveMode: true);
var githubSvc    = new GitHubService(githubToken);

var kernel = Kernel.CreateBuilder()
    .AddOpenAIChatCompletion(modelId: "gpt-4o", apiKey: apiKey, httpClient: httpClient)
    .Build();

kernel.Plugins.AddFromObject(reviewPlugin, "CodeReview");

// ── 3. Banner ────────────────────────────────────────────────
Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║      PR Review Agent  —  Step 6          ║");
Console.WriteLine("║      GitHub API Integration               ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine($"  Repo     : {githubOwner}/{githubRepo}");
Console.WriteLine($"  PR       : #{prNumber}");
Console.WriteLine($"  GitHub   : {(githubToken is null ? "Mock mode" : "Live mode ✅")}");
Console.WriteLine($"  OpenAI   : gpt-4o");
Console.WriteLine();

// ── 4. Fetch the PR from GitHub (or mock) ────────────────────
Console.WriteLine("── Step 1: Fetch PR ─────────────────────────────────");
var pr = await githubSvc.GetPullRequestAsync(githubOwner, githubRepo, prNumber);
Console.WriteLine($"  ✅ Got PR: \"{pr.Title}\" by {pr.Author}");
Console.WriteLine($"     {pr.SourceBranch} → {pr.TargetBranch}");
Console.WriteLine();

// ── 5. Build the diff prompt ─────────────────────────────────
string userPrompt = $"""
    Please review this pull request:

    PR #{pr.Number}: {pr.Title}
    Author: {pr.Author}
    Branch: {pr.SourceBranch} → {pr.TargetBranch}

    {pr.Diff}
    """;

// ── 6. System prompt ─────────────────────────────────────────
const string systemPrompt = """
    You are a senior C# code reviewer operating in a ReAct loop.

    Your process for EVERY review — follow ALL steps in order:

    STEP 1 — Run ALL diagnostic tools:
             CheckCodeQuality, CheckTestCoverage, CheckBreakingChange, CheckConventions

    STEP 2 — Call AskClarifyingQuestion for the single most important uncertainty.

    STEP 3 — Call SummariseFindings to consolidate all results.

    STEP 4 — Call EstimateRisk with the exact issue counts you found.

    STEP 5 — Call BuildStructuredVerdict as your final step.
             NEVER write a free-text verdict. Always call the tool.
    """;

// ── 7. ReAct Loop ────────────────────────────────────────────
Console.WriteLine("── Step 2: Agent Review Loop ────────────────────────");
Console.WriteLine();

var chatService = kernel.GetRequiredService<IChatCompletionService>();
var history     = new ChatHistory(systemPrompt);
history.AddUserMessage(userPrompt);

#pragma warning disable SKEXP0001
var settings = new OpenAIPromptExecutionSettings
{
    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
    MaxTokens        = 2000
};
#pragma warning restore SKEXP0001

int round = 0;
while (true)
{
    round++;
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"\n  [Round {round}] Agent reasoning...");
    Console.ResetColor();

    var response = await chatService.GetChatMessageContentAsync(
        history, executionSettings: settings, kernel: kernel);

    history.AddAssistantMessage(response.Content ?? string.Empty);

    bool verdictBuilt = reviewPlugin.LastResult is not null;
    bool hasFinalText = !string.IsNullOrWhiteSpace(response.Content)
                     && (response.Content.Contains("APPROVED")
                      || response.Content.Contains("CHANGES_REQUESTED")
                      || response.Content.Contains("BLOCKED"));

    if (verdictBuilt || hasFinalText || round >= 6) break;

    if (string.IsNullOrWhiteSpace(response.Content))
        history.AddUserMessage(
            "Continue. Your final step MUST be BuildStructuredVerdict.");
}

// ── 8. Render structured verdict ─────────────────────────────
Console.WriteLine();
Console.WriteLine("── Step 3: Structured Verdict ───────────────────────");
Console.WriteLine();

ReviewResult result = reviewPlugin.LastResult
    ?? new ReviewResult { Summary = "No structured result produced." };

Console.ForegroundColor = result.Verdict switch
{
    "APPROVED"  => ConsoleColor.Green,
    "BLOCKED"   => ConsoleColor.Red,
    _           => ConsoleColor.Yellow
};
Console.WriteLine($"  Verdict     : {result.Verdict}");
Console.ResetColor();
Console.WriteLine($"  Confidence  : {result.Confidence:P0}");
Console.WriteLine($"  Risk Level  : {result.RiskLevel}");
Console.WriteLine($"  Merge Action: {result.MergeAction}");
Console.WriteLine($"  Summary     : {result.Summary}");

if (result.RequiredChanges.Any())
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("  Required Changes:");
    Console.ResetColor();
    foreach (var i in result.RequiredChanges)
        Console.WriteLine($"    ✗  {i}");
}

if (result.Suggestions.Any())
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("  Suggestions:");
    Console.ResetColor();
    foreach (var s in result.Suggestions)
        Console.WriteLine($"    →  {s}");
}

// ── 9. Post review comment to GitHub ─────────────────────────
Console.WriteLine();
Console.WriteLine("── Step 4: Post to GitHub ───────────────────────────");
Console.WriteLine();
await githubSvc.PostReviewCommentAsync(githubOwner, githubRepo, prNumber, result);
await githubSvc.SubmitReviewAsync(githubOwner, githubRepo, prNumber, result, commitSha: "HEAD");

// ── 10. Human-in-the-Loop gate ───────────────────────────────
Console.WriteLine();
Console.WriteLine("── Step 5: Human-in-the-Loop Gate ──────────────────");
Console.WriteLine();

string finalAction = await HumanGate(result, githubSvc, githubOwner, githubRepo, prNumber);

// ── 11. Audit log ────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("── Step 6: Audit Log ────────────────────────────────");
Console.WriteLine();

var audit = new
{
    timestamp      = DateTime.UtcNow,
    repository     = $"{githubOwner}/{githubRepo}",
    pr             = $"#{prNumber} {pr.Title}",
    author         = pr.Author,
    agentVerdict   = result.Verdict,
    confidence     = result.Confidence,
    riskLevel      = result.RiskLevel,
    mergeAction    = result.MergeAction,
    humanDecision  = finalAction,
    requiredChanges = result.RequiredChanges,
    suggestions    = result.Suggestions,
    summary        = result.Summary
};

string auditJson = JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true });

// Write to file — in production this goes to a database or logging service
string auditPath = Path.Combine(AppContext.BaseDirectory, "audit-log.json");
await File.WriteAllTextAsync(auditPath, auditJson);

Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine(auditJson);
Console.ResetColor();
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"  ✅ Audit log written → {auditPath}");
Console.ResetColor();

// ── Done ─────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine(new string('─', 52));
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"✅ PR Review Agent — all 6 steps complete!");
Console.ResetColor();
Console.WriteLine();
Console.WriteLine("  What this system demonstrates:");
Console.WriteLine("  1. Tool Use          — 8 C# functions the agent calls");
Console.WriteLine("  2. ReAct Loop        — multi-round reasoning");
Console.WriteLine("  3. Planning          — ordered steps, not random calls");
Console.WriteLine("  4. Memory            — chat history across rounds");
Console.WriteLine("  5. Structured Output — typed ReviewResult record");
Console.WriteLine("  6. Human-in-the-Loop — confidence-gated approval");
Console.WriteLine("  7. Observability     — every decision logged");
Console.WriteLine("  8. GitHub Integration— real API calls (or mock)");

// ── Human gate ───────────────────────────────────────────────
static async Task<string> HumanGate(
    ReviewResult result, GitHubService github,
    string owner, string repo, int prNum)
{
    switch (result.MergeAction)
    {
        case "AUTO_MERGE":
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  ✅ Confidence ≥ 95% — AUTO-MERGE triggered.");
            Console.ResetColor();
            await github.MergePullRequestAsync(owner, repo, prNum, result);
            return "AUTO_MERGED";

        case "BLOCK":
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  🚫 PR BLOCKED — critical issues:");
            Console.ResetColor();
            foreach (var issue in result.RequiredChanges)
                Console.WriteLine($"     ✗  {issue}");
            Console.WriteLine();
            string blockAnswer = await PromptYesNo("  Override the block and merge anyway? (yes/no): ");
            if (blockAnswer == "yes")
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  ⚠️  Human override — merging despite agent objection.");
                Console.ResetColor();
                await github.MergePullRequestAsync(owner, repo, prNum, result);
                return "HUMAN_OVERRIDE_MERGE";
            }
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  ✅ Block confirmed. PR stays open.");
            Console.ResetColor();
            return "BLOCKED";

        default: // HUMAN_REVIEW
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  👤 Confidence {result.Confidence:P0} — below 95% threshold.");
            Console.WriteLine("     Agent recommends: CHANGES REQUESTED");
            Console.ResetColor();
            Console.WriteLine();
            string reviewAnswer = await PromptYesNo("  Approve and merge this PR? (yes/no): ");
            if (reviewAnswer == "yes")
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  ✅ Human approved — merging.");
                Console.ResetColor();
                await github.MergePullRequestAsync(owner, repo, prNum, result);
                return "HUMAN_APPROVED_MERGE";
            }
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  📝 Changes requested — PR stays open for fixes.");
            Console.ResetColor();
            return "HUMAN_REQUESTED_CHANGES";
    }
}

static async Task<string> PromptYesNo(string prompt)
{
    while (true)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(prompt);
        Console.ResetColor();
        string? input = Console.ReadLine()?.Trim().ToLower();
        if (input is "yes" or "y") return "yes";
        if (input is "no"  or "n") return "no";
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  Please type 'yes' or 'no'.");
        Console.ResetColor();
    }
}
