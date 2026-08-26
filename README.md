# PR Review Agent

An autonomous Pull Request review agent built in C# using **Semantic Kernel** and **GPT-4o**.

## What it does

- Fetches a PR diff from GitHub
- Runs 8 static analysis tools automatically (code quality, test coverage, breaking changes, conventions)
- Asks the developer a clarifying question when uncertain
- Produces a typed `ReviewResult` with confidence score
- Gates merge decisions behind human approval
- Posts a formatted review comment back to GitHub
- Writes a full audit log

## Agentic AI Concepts Demonstrated

| Concept | Implementation |
|---|---|
| Tool Use | 8 `[KernelFunction]` C# methods |
| ReAct Loop | Multi-round reason → act → observe |
| Planning | Ordered 5-step review process |
| Memory | Chat history across rounds |
| Structured Output | Typed `ReviewResult` record |
| Human-in-the-Loop | Confidence-gated approval prompt |
| Observability | Every tool call logged in real time |
| GitHub Integration | Octokit — fetch PR, post comment, merge |

## Setup

```bash
export OPENAI_API_KEY="sk-..."
export GITHUB_TOKEN="ghp_..."
export GITHUB_OWNER="your-username"
export GITHUB_REPO="your-repo"
export GITHUB_PR_NUMBER="1"
dotnet run
```

## Architecture

```
GitHub PR → Fetch Diff → ReAct Loop (8 tools) → ReviewResult → Human Gate → GitHub Actions → Audit Log
```

Built with: .NET 10 · Semantic Kernel 1.30 · GPT-4o · Octokit
