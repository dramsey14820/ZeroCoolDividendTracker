---
name: feature-slice
description: Step-by-step workflow to implement a single vertical slice from .ai/slices/slice-XX.md using the ZeroCool hybrid Ollama + Gemini TDD pipeline.
---

# Feature Slice Execution Workflow

Follow this exact 5-step workflow to implement **one** vertical slice at a time. Never implement multiple slices in a single pass.

## Prerequisites

- An approved vertical slice mini-spec exists at `.ai/slices/slice-XX.md`.
- Local Ollama server is running at `http://localhost:11434` with `qwen2.5-coder:7b` and `qwen3.5:9b`.
- **Context Guardrail**: Keep all local Ollama prompts strictly under `8192` tokens (`num_ctx: 8192`). Pass only `.ai/constitution.md`, the active `slice-XX.md`, target file(s), and failing output.

---

## Step 1: Load Minimal Context & Verify Scope

1. Read `.ai/constitution.md` and `.agents/rules/csharp-standards.md`.
2. Read the active slice file: `.ai/slices/slice-XX.md`.
3. Confirm the target files, interfaces/contracts, SQL schema, and Gherkin scenarios are unambiguous.
   - If contracts or acceptance criteria are missing or contradictory, **stop and escalate to Gemini (Stage 2)** to refine the slice spec before writing code.

---

## Step 2: Generate Failing Tests from Gherkin (Red Phase)

- **Model**: Local Ollama `qwen2.5-coder:7b` (`num_ctx: 8192`).
- Generate unit and/or integration tests covering **every** `Scenario` (`Given / When / Then`) in `.ai/slices/slice-XX.md`.
- Follow `.agents/rules/csharp-standards.md`:
  - Use `xUnit` (`[Fact]` / `[Theory]`), `FluentAssertions`, and `NSubstitute`.
  - Follow Arrange / Act / Assert with descriptive test names mapped to the Gherkin scenarios.
- Run `dotnet test` to confirm the new tests compile (or fail strictly due to unimplemented slice code) and fail for the expected reason.

---

## Step 3: Implement the Vertical Slice (Green Phase)

- **Model**: Local Ollama `qwen2.5-coder:7b` (`num_ctx: 8192`).
- Implement the target files listed in `.ai/slices/slice-XX.md` against the defined DTOs, interfaces, and SQL schema.
- Enforce all constraints:
  - Modern C# (.NET 10+ LTS), nullable reference types enabled, file-scoped namespaces.
  - Explicit `Program` class with `static void Main` (no top-level statements).
  - Parameterized SQL / safe EF Core queries, `CancellationToken` propagation, no unrequested refactoring outside the slice's target files.

---

## Step 4: Deterministic Build/Test Gate & Local Repair Loop

1. Run the deterministic verification commands:
   ```powershell
   dotnet build -warnaserror
   dotnet test
   ```
2. If either command fails:
   - **Model**: Local Ollama `qwen3.5:9b` (`num_ctx: 8192`).
   - Provide **only** the concise compiler error/warning or failing test stack trace plus the affected file(s) and slice contract.
   - Apply the targeted fix and re-run `dotnet build -warnaserror` and `dotnet test`.
3. **Retry Cap (Max 2 Local Attempts)**:
   - Track local repair attempts (`Attempt 1/2`, `Attempt 2/2`).
   - If the build or tests remain red after **2 local attempts**, **STOP** local retries immediately and escalate to **Gemini (High/Medium)** with:
     - The active slice ID
     - The exact compiler/test error output
     - A summary of the 2 attempted local fixes and current `git diff`

---

## Step 5: Two-Tier Diff Review & Handoff

1. Update the Implementation Subtasks checklist in `.ai/slices/slice-XX.md` to mark completed items.
2. Stage all slice changes:
   ```powershell
   git status
   git add <slice-target-files>
   git diff --staged
   ```
3. **Pass-1 Local Diff Review (`qwen3.5:9b`)**:
   - Feed `git diff --staged` + `.ai/slices/slice-XX.md` (within 8192 tokens) to `qwen3.5:9b`.
   - Check for:
     - Missed Gherkin acceptance criteria or edge cases
     - C# standard violations (top-level statements, missing `CancellationToken`, nullability suppressions, swallowed exceptions)
     - Obvious logic bugs or unparameterized SQL
   - If Pass-1 finds issues, fix them, re-run `dotnet build -warnaserror` and `dotnet test`, and re-stage.
4. **Pass-2 Gemini Architectural & Security Audit**:
   - Present the clean `git diff --staged`, the deterministic build/test pass summary, and any Pass-1 review notes to **Gemini (High/Medium)** for final architectural and security sign-off before committing.

