// ============================================================
//  CodeReviewPlugin — tools the agent can call
//
//  Step 3 additions:
//  • SummariseFindings  — agent calls this to consolidate what
//                         it has learned before deciding next step
//  • AskClarifyingQuestion — agent calls this when it needs more
//                            context (sets up human-in-the-loop)
//  • EstimateRisk       — agent calls this to score overall risk
// ============================================================

using Microsoft.SemanticKernel;
using PRReviewAgent.Models;
using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PRReviewAgent.Plugins;

public class CodeReviewPlugin
{
    private readonly List<string> _findings = [];
    private readonly bool _interactiveMode;

    /// <param name="interactiveMode">
    /// true  = AskClarifyingQuestion pauses for real keyboard input (Step 5+)
    /// false = simulates a developer response (Step 2–4)
    /// </param>
    public CodeReviewPlugin(bool interactiveMode = false)
    {
        _interactiveMode = interactiveMode;
    }

    // ── Tool 1: Code Quality ─────────────────────────────────
    [KernelFunction("CheckCodeQuality")]
    [Description("Analyses a PR diff for code quality issues: long methods, magic numbers, " +
                 "poor naming, missing null checks, logic errors, and non-idiomatic patterns.")]
    public string CheckCodeQuality(
        [Description("The full PR diff text to analyse")] string diff)
    {
        Console.WriteLine("  🔧 [Tool Called] CheckCodeQuality");

        // Only analyse lines being ADDED (starting with +)
        // Ignore removed lines (-) and context lines — they are not the new code
        var addedLines = diff.Split('\n')
            .Where(l => l.StartsWith("+") && !l.StartsWith("+++"))
            .Select(l => l[1..]) // strip the leading +
            .ToList();

        string addedCode = string.Join("\n", addedLines);
        int addedCount   = addedLines.Count;

        var issues = new List<string>();

        if (addedCount > 30)
            issues.Add($"Large addition ({addedCount} lines) — consider splitting into smaller methods");

        // Magic numbers — only flag if no named constants are defined
        bool hasMagicNumbers = Regex.IsMatch(addedCode, @"\b(10000|20000|10_000|20_000)\b");
        bool hasConstants     = addedCode.Contains("const ") || addedCode.Contains("private const");
        if (hasMagicNumbers && !hasConstants)
            issues.Add("Magic numbers detected — extract to named constants");

        // Null guard — only flag if there's a List parameter and no null check
        bool hasListParam  = addedCode.Contains("List<");
        bool hasNullCheck  = addedCode.Contains("ArgumentNullException") ||
                             addedCode.Contains("ThrowIfNull") ||
                             addedCode.Contains("?? throw") ||
                             addedCode.Contains("is null");
        if (hasListParam && !hasNullCheck)
            issues.Add("No null guard on List parameter — add ArgumentNullException check");

        // Stacked if-blocks for discount (the original bug)
        int ifDiscountCount = Regex.Matches(addedCode, @"if\s*\(total\s*>").Count;
        bool hasSwitchExpr  = addedCode.Contains("switch");
        if (ifDiscountCount > 1 && !hasSwitchExpr)
            issues.Add($"Stacked if-blocks for discount logic ({ifDiscountCount}) — use switch expression or else-if");

        // For-loop when LINQ available
        bool hasForLoop = addedCode.Contains("for (int i") || addedCode.Contains("for(int i");
        bool hasLinq    = addedCode.Contains(".Sum(") || addedCode.Contains(".Select(") || addedCode.Contains(".Where(");
        if (hasForLoop && !hasLinq)
            issues.Add("Manual for-loop — prefer LINQ: items.Sum(x => x.Quantity * x.UnitPrice)");

        var result = issues.Count > 0
            ? $"CODE QUALITY — {issues.Count} issue(s):\n" + string.Join("\n", issues.Select(i => $"  • {i}"))
            : $"CODE QUALITY — ✅ No issues found in {addedCount} added lines.";

        _findings.Add(result);
        return result;
    }

    // ── Tool 2: Test Coverage ────────────────────────────────
    [KernelFunction("CheckTestCoverage")]
    [Description("Checks whether the PR diff includes unit tests for new or changed code.")]
    public string CheckTestCoverage(
        [Description("The full PR diff text to analyse")] string diff)
    {
        Console.WriteLine("  🔧 [Tool Called] CheckTestCoverage");

        // Only look at added lines
        string addedCode = string.Join("\n", diff.Split('\n')
            .Where(l => l.StartsWith("+") && !l.StartsWith("+++"))
            .Select(l => l[1..]));

        bool hasTests = addedCode.Contains("[Fact]")
                     || addedCode.Contains("[Test]")
                     || addedCode.Contains("[Theory]")
                     || addedCode.Contains("Assert.")
                     || addedCode.Contains("void Should")
                     || diff.Contains("Tests.csproj")   // test project added
                     || diff.Contains("Test.cs")
                     || diff.Contains("Tests.cs");

        // Also check if a test file path appears in the diff header
        bool testFileInDiff = diff.Split('\n')
            .Any(l => (l.StartsWith("+++") || l.StartsWith("File:"))
                   && (l.Contains("Test") || l.Contains("test") || l.Contains("Spec")));

        var result = (hasTests || testFileInDiff)
            ? "TEST COVERAGE — ✅ Tests detected in this PR."
            : "TEST COVERAGE — ❌ No tests added. Required: happy path, empty list, " +
              "negative values, discount threshold boundaries.";

        _findings.Add(result);
        return result;
    }

    // ── Tool 3: Breaking Change Detection ───────────────────
    [KernelFunction("CheckBreakingChange")]
    [Description("Detects breaking changes: modified public APIs, removed members, changed signatures.")]
    public string CheckBreakingChange(
        [Description("The full PR diff text to analyse")] string diff)
    {
        Console.WriteLine("  🔧 [Tool Called] CheckBreakingChange");

        var warnings = new List<string>();

        var removedPublic = diff.Split('\n')
            .Where(l => l.StartsWith("-") && l.Contains("public "))
            .ToList();

        if (removedPublic.Any())
            warnings.Add($"{removedPublic.Count} public member(s) modified — verify interface compatibility");

        if (diff.Contains("public decimal CalculateTotal"))
            warnings.Add("New public method added — confirm it matches any existing interface contract");

        var result = warnings.Count > 0
            ? $"BREAKING CHANGES — {warnings.Count} warning(s):\n" + string.Join("\n", warnings.Select(w => $"  ⚠️  {w}"))
            : "BREAKING CHANGES — ✅ No breaking changes detected.";

        _findings.Add(result);
        return result;
    }

    // ── Tool 4: Conventions Check ────────────────────────────
    [KernelFunction("CheckConventions")]
    [Description("Checks C# naming conventions, XML documentation, and formatting standards.")]
    public string CheckConventions(
        [Description("The full PR diff text to analyse")] string diff)
    {
        Console.WriteLine("  🔧 [Tool Called] CheckConventions");

        string addedCode = string.Join("\n", diff.Split('\n')
            .Where(l => l.StartsWith("+") && !l.StartsWith("+++"))
            .Select(l => l[1..]));

        var issues = new List<string>();

        bool hasPublicMethod = addedCode.Contains("public ") &&
                               (addedCode.Contains("(") && addedCode.Contains(")"));
        bool hasXmlDocs      = addedCode.Contains("/// <summary>") ||
                               addedCode.Contains("///<summary>");

        if (hasPublicMethod && !hasXmlDocs)
            issues.Add("Missing XML documentation on public members");

        var result = issues.Count > 0
            ? $"CONVENTIONS — {issues.Count} issue(s):\n" + string.Join("\n", issues.Select(i => $"  • {i}"))
            : "CONVENTIONS — ✅ Conventions look good.";

        _findings.Add(result);
        return result;
    }

    // ── Tool 5: Risk Estimator (NEW in Step 3) ───────────────
    [KernelFunction("EstimateRisk")]
    [Description("Calculates an overall risk score and confidence level based on all findings " +
                 "gathered so far. Call this after all other checks are complete.")]
    public string EstimateRisk(
        [Description("Number of critical issues found")] int criticalCount,
        [Description("Number of warning-level issues found")] int warningCount,
        [Description("Whether unit tests are missing (true/false)")] bool testsMissing)
    {
        Console.WriteLine("  🔧 [Tool Called] EstimateRisk");

        // Simple scoring model — easy to explain in an interview
        double riskScore = (criticalCount * 0.3) + (warningCount * 0.1) + (testsMissing ? 0.3 : 0.0);
        double confidence = Math.Max(0.0, 1.0 - riskScore);

        string riskLevel = riskScore switch
        {
            > 0.7  => "🔴 HIGH",
            > 0.4  => "🟡 MEDIUM",
            > 0.1  => "🟠 LOW-MEDIUM",
            _      => "🟢 LOW"
        };

        string action = confidence > 0.95 && !testsMissing
            ? "AUTO-MERGE eligible"
            : "HUMAN REVIEW required";

        return $"RISK ASSESSMENT:\n" +
               $"  Risk Level  : {riskLevel}\n" +
               $"  Risk Score  : {riskScore:F2}\n" +
               $"  Confidence  : {confidence:P0}\n" +
               $"  Decision    : {action}\n" +
               $"  Reasoning   : {criticalCount} critical + {warningCount} warnings" +
               $"{(testsMissing ? " + missing tests" : "")}";
    }

    // ── Tool 6: Clarifying Question ──────────────────────────
    [KernelFunction("AskClarifyingQuestion")]
    [Description("Use this when you need more context from the developer before making " +
                 "a final decision. The question will be shown to the human reviewer.")]
    public string AskClarifyingQuestion(
        [Description("The specific question to ask the developer")] string question)
    {
        Console.WriteLine("  🔧 [Tool Called] AskClarifyingQuestion");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  ❓ Agent needs clarification:");
        Console.WriteLine($"     \"{question}\"");
        Console.ResetColor();
        Console.WriteLine();

        string response;

        if (_interactiveMode)
        {
            // Step 5+: pause and wait for the real developer's answer
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("  Your answer: ");
            Console.ResetColor();
            response = Console.ReadLine() ?? "No answer provided.";
        }
        else
        {
            // Step 2–4: simulate a developer response for demo purposes
            response = question.ToLower() switch
            {
                var q when q.Contains("test")      => "Tests for this method will be added in a follow-up PR.",
                var q when q.Contains("discount")  => "Intention: 10% for orders over 10k, additional 5% for orders over 20k (cumulative).",
                var q when q.Contains("interface") => "There is no IInvoiceService interface yet — this is a new class.",
                _                                  => "I'll address this in a follow-up PR."
            };
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  💬 Simulated developer response: \"{response}\"");
            Console.ResetColor();
        }

        Console.WriteLine();
        return $"CLARIFICATION RECEIVED: {response}";
    }

    // ── Tool 7: Summarise Findings (Step 3) ─────────────────
    [KernelFunction("SummariseFindings")]
    [Description("Returns all findings accumulated so far in this review session. " +
                 "Call this to consolidate before making a final decision.")]
    public string SummariseFindings()
    {
        Console.WriteLine("  🔧 [Tool Called] SummariseFindings");

        if (_findings.Count == 0)
            return "No findings recorded yet — run the check tools first.";

        return $"ACCUMULATED FINDINGS ({_findings.Count} checks run):\n" +
               string.Join("\n\n", _findings.Select((f, i) => $"[{i + 1}] {f}"));
    }

    // ── Tool 8: Build Structured Verdict (NEW in Step 4) ────
    [KernelFunction("BuildStructuredVerdict")]
    [Description("Call this as your LAST step. Converts all findings into a typed " +
                 "ReviewResult JSON object that drives automated merge decisions.")]
    public string BuildStructuredVerdict(
        [Description("APPROVED, CHANGES_REQUESTED, or BLOCKED")] string verdict,
        [Description("Confidence score between 0.0 and 1.0")] double confidence,
        [Description("LOW, MEDIUM, or HIGH")] string riskLevel,
        [Description("Comma-separated list of required (blocking) changes")] string requiredChanges,
        [Description("Comma-separated list of optional suggestions")] string suggestions,
        [Description("One sentence summary to post as a PR comment")] string summary)
    {
        Console.WriteLine("  🔧 [Tool Called] BuildStructuredVerdict");

        // Determine merge action purely from data — no LLM opinion needed here
        string mergeAction = verdict switch
        {
            "APPROVED" when confidence >= 0.95 => "AUTO_MERGE",
            "APPROVED"                          => "HUMAN_REVIEW",
            "BLOCKED"                           => "BLOCK",
            _                                   => "HUMAN_REVIEW"
        };

        var result = new ReviewResult
        {
            Verdict         = verdict,
            Confidence      = confidence,
            RiskLevel       = riskLevel,
            RequiredChanges = requiredChanges
                                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .ToList(),
            Suggestions     = suggestions
                                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .ToList(),
            MergeAction     = mergeAction,
            Summary         = summary
        };

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  📦 Structured ReviewResult:");
        Console.WriteLine(json);
        Console.ResetColor();
        Console.WriteLine();

        // Stored so Program.cs can retrieve it after the loop ends
        LastResult = result;
        return json;
    }

    /// <summary>The last ReviewResult built — retrieved by Program.cs after the loop.</summary>
    public ReviewResult? LastResult { get; private set; }
}
