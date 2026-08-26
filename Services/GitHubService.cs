// ============================================================
//  GitHubService — all GitHub API interactions in one place
//
//  Responsibilities:
//  • Fetch a PR diff for the agent to review
//  • Post a structured review comment back to the PR
//  • Submit an approve / request-changes / block review
//  • Trigger a merge when the agent is confident enough
//
//  Uses Octokit — GitHub's official .NET client library.
//  Falls back to mock mode when no GitHub token is set,
//  so the demo works without a real repo.
// ============================================================

using Octokit;
using PRReviewAgent.Models;

namespace PRReviewAgent.Services;

public class GitHubService
{
    private readonly GitHubClient? _client;
    private readonly bool          _mockMode;

    public GitHubService(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            _mockMode = true;
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("  ℹ️  GITHUB_TOKEN not set — running in mock mode.");
            Console.WriteLine("      Set GITHUB_TOKEN to connect to a real repository.");
            Console.ResetColor();
            Console.WriteLine();
            return;
        }

        _client = new GitHubClient(new ProductHeaderValue("PR-Review-Agent"))
        {
            Credentials = new Credentials(token)
        };
    }

    // ── Fetch PR diff ────────────────────────────────────────
    /// <summary>
    /// Returns the diff text for a given PR number.
    /// In mock mode returns a realistic hardcoded diff.
    /// </summary>
    public async Task<PullRequestContext> GetPullRequestAsync(
        string owner, string repo, int prNumber)
    {
        if (_mockMode)
        {
            Console.WriteLine("  📥 [Mock] Fetching PR diff...");
            await Task.Delay(300); // simulate network
            return MockPullRequest(prNumber);
        }

        Console.WriteLine($"  📥 Fetching PR #{prNumber} from {owner}/{repo}...");

        var pr   = await _client!.PullRequest.Get(owner, repo, prNumber);
        var diff = await _client.PullRequest.Files(owner, repo, prNumber);

        // Build a diff string the agent can read
        var diffText = string.Join("\n\n", diff.Select(f =>
            $"File: {f.FileName}  (+{f.Additions} -{f.Deletions})\n" +
            f.Patch));

        return new PullRequestContext(
            Number      : prNumber,
            Title       : pr.Title,
            Author      : pr.User.Login,
            SourceBranch: pr.Head.Ref,
            TargetBranch: pr.Base.Ref,
            Diff        : diffText,
            CommitSha   : pr.Head.Sha   // real commit SHA for review submission
        );
    }

    // ── Post review comment ──────────────────────────────────
    /// <summary>
    /// Posts the agent's structured verdict as a comment on the PR.
    /// </summary>
    public async Task PostReviewCommentAsync(
        string owner, string repo, int prNumber, ReviewResult result)
    {
        string body = BuildCommentBody(result);

        if (_mockMode)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  📤 [Mock] Would post this comment to GitHub:");
            Console.WriteLine(new string('·', 50));
            Console.WriteLine(body);
            Console.WriteLine(new string('·', 50));
            Console.ResetColor();
            await Task.Delay(200);
            return;
        }

        Console.WriteLine($"  📤 Posting review comment to PR #{prNumber}...");
        await _client!.Issue.Comment.Create(owner, repo, prNumber, body);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  ✅ Comment posted.");
        Console.ResetColor();
    }

    // ── Submit GitHub review (approve / request changes) ────
    /// <summary>
    /// Submits an official GitHub PR review — this is what
    /// controls the green tick / red cross on the PR page.
    /// </summary>
    public async Task SubmitReviewAsync(
        string owner, string repo, int prNumber,
        ReviewResult result, string commitSha)
    {
        var reviewType = result.MergeAction switch
        {
            "AUTO_MERGE"   => PullRequestReviewEvent.Approve,
            "BLOCK"        => PullRequestReviewEvent.RequestChanges,
            _              => PullRequestReviewEvent.Comment
        };

        string body = result.MergeAction switch
        {
            "AUTO_MERGE" => $"✅ **Auto-approved by PR Review Agent** (confidence: {result.Confidence:P0})\n\n{result.Summary}",
            "BLOCK"      => $"🚫 **Blocked by PR Review Agent** — critical issues found.\n\n{result.Summary}",
            _            => $"👤 **Human review required** (confidence: {result.Confidence:P0})\n\n{result.Summary}"
        };

        if (_mockMode)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  📤 [Mock] Would submit GitHub review: {reviewType}");
            Console.ResetColor();
            await Task.Delay(200);
            return;
        }

        Console.WriteLine($"  📤 Submitting {reviewType} review to PR #{prNumber}...");

        var review = new PullRequestReviewCreate
        {
            CommitId = commitSha,
            Body     = body,
            Event    = reviewType
        };

        try
        {
            await _client!.PullRequest.Review.Create(owner, repo, prNumber, review);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✅ GitHub review submitted: {reviewType}");
            Console.ResetColor();
        }
        catch (Octokit.ApiValidationException ex)
            when (ex.Message.Contains("approve your own pull request"))
        {
            // GitHub does not allow a PR author to approve their own PR.
            // Fall back to a plain comment review so the run still completes.
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  ⚠️  Cannot approve own PR — falling back to Comment review.");
            Console.ResetColor();

            var commentReview = new PullRequestReviewCreate
            {
                CommitId = commitSha,
                Body     = body,
                Event    = PullRequestReviewEvent.Comment
            };
            await _client!.PullRequest.Review.Create(owner, repo, prNumber, commentReview);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  ✅ Comment review posted (self-review fallback).");
            Console.ResetColor();
        }
    }

    // ── Merge PR ─────────────────────────────────────────────
    /// <summary>
    /// Merges the PR. Only called when MergeAction == AUTO_MERGE
    /// and human has approved (or confidence ≥ 0.95).
    /// </summary>
    public async Task MergePullRequestAsync(
        string owner, string repo, int prNumber, ReviewResult result)
    {
        if (_mockMode)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  🔀 [Mock] Would merge PR #{prNumber} via GitHub API.");
            Console.ResetColor();
            await Task.Delay(200);
            return;
        }

        Console.WriteLine($"  🔀 Merging PR #{prNumber}...");

        var merge = new MergePullRequest
        {
            CommitTitle   = $"Auto-merged by PR Review Agent (confidence: {result.Confidence:P0})",
            CommitMessage = result.Summary,
            MergeMethod   = PullRequestMergeMethod.Squash
        };

        try
        {
            var mergeResult = await _client!.PullRequest.Merge(owner, repo, prNumber, merge);

            if (mergeResult.Merged)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✅ PR #{prNumber} merged successfully.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ❌ Merge failed: {mergeResult.Message}");
                Console.ResetColor();
            }
        }
        catch (Octokit.PullRequestNotMergeableException)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  ⚠️  PR #{prNumber} has merge conflicts — resolve them before merging.");
            Console.WriteLine("      Run: git fetch origin && git merge origin/main, fix conflicts, then push.");
            Console.ResetColor();
        }
    }

    // ── Helpers ──────────────────────────────────────────────

    private static string BuildCommentBody(ReviewResult result)
    {
        var sb = new System.Text.StringBuilder();

        string emoji = result.Verdict switch
        {
            "APPROVED"          => "✅",
            "BLOCKED"           => "🚫",
            _                   => "⚠️"
        };

        sb.AppendLine($"## {emoji} PR Review Agent — {result.Verdict}");
        sb.AppendLine();
        sb.AppendLine($"| | |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| **Risk Level** | {result.RiskLevel} |");
        sb.AppendLine($"| **Confidence** | {result.Confidence:P0} |");
        sb.AppendLine($"| **Action** | {result.MergeAction} |");
        sb.AppendLine();
        sb.AppendLine($"> {result.Summary}");

        if (result.RequiredChanges.Any())
        {
            sb.AppendLine();
            sb.AppendLine("### ❌ Required Changes (blocking)");
            foreach (var issue in result.RequiredChanges)
                sb.AppendLine($"- {issue}");
        }

        if (result.Suggestions.Any())
        {
            sb.AppendLine();
            sb.AppendLine("### 💡 Suggestions (non-blocking)");
            foreach (var s in result.Suggestions)
                sb.AppendLine($"- {s}");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Reviewed by [PR Review Agent](https://github.com) — " +
                      $"powered by Semantic Kernel + GPT-4o*");

        return sb.ToString();
    }

    private static PullRequestContext MockPullRequest(int prNumber) =>
        new(
            Number      : prNumber,
            Title       : "Add invoice total calculation",
            Author      : "john.doe",
            SourceBranch: "feature/invoice-service",
            TargetBranch: "main",
            CommitSha   : "417d776b1ee8d50cd8b56c5c7d5a2f21a4e5b215",
            Diff        : """
                PR Title:  Add invoice total calculation
                PR Author: john.doe
                Branch:    feature/invoice-total → main

                --- a/Services/InvoiceService.cs
                +++ b/Services/InvoiceService.cs
                @@ -10,6 +10,25 @@ public class InvoiceService
                 {
                +    public decimal CalculateTotal(List<LineItem> items)
                +    {
                +        decimal total = 0;
                +        for (int i = 0; i < items.Count; i++)
                +        {
                +            total = total + items[i].Quantity * items[i].UnitPrice;
                +        }
                +        if (total > 10000)
                +        {
                +            total = total - (total * 0.1m);
                +        }
                +        if (total > 20000)
                +        {
                +            total = total - (total * 0.05m);
                +        }
                +        return total;
                +    }
                 }

                No unit tests were added.
                No XML documentation added.
                """
        );
}

/// <summary>Data the agent receives about a pull request.</summary>
public record PullRequestContext(
    int    Number,
    string Title,
    string Author,
    string SourceBranch,
    string TargetBranch,
    string Diff,
    string CommitSha  // actual HEAD SHA — needed for submitting reviews
);
