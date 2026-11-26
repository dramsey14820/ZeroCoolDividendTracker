---
name: pipeline-guide
description: Interactive stage-by-stage coach that detects the current ZeroCool AI Coding Pipeline stage and guides the user from requirements through vertical slice specs, local Ollama TDD, and two-tier diff review.
---

# ZeroCool Pipeline Interactive Guide

Use this skill whenever the user asks to **start the pipeline**, **guide me through the next stage**, **where am I in the pipeline**, or **run a new app through the pipeline**.

Your job is to act as a clear, step-by-step coach: detect the current state, tell the user exactly which stage and model apply, perform the current stage's work (or hand off to local Ollama), and pause at explicit checkpoints before advancing.

---

## Step 0: Auto-Detect Current Pipeline Stage

Before responding, inspect the repository state:
1. Check if `.ai/requirements.md` exists.
2. List `.ai/slices/slice-*.md` (excluding `slice-template.md`) and read the `Status` line of the latest numbered slice.
3. Check `git status --short` (note any modified or staged files).

Determine the active stage:
- **No `.ai/requirements.md`** -> **Stage 1 (Business Requirements Brief)**
- **`.ai/requirements.md` exists, but no numbered slice (or latest slice is `Done`)** -> **Stage 2 (Vertical Slice Technical Mini-Spec)**
- **Active slice is `Draft`** -> **Stage 2 (Finalize & Approve Slice Spec)**
- **Active slice is `Ready`** -> **Stage 3 (Red Phase: Generate Failing Tests)**
- **Active slice is `In Progress`** -> Check test/build state to guide **Stage 4 (Implement)** or **Stage 5 (Build/Test & Local Repair)**
- **Active slice is `In Review` (or build/tests are green with staged changes)** -> **Stage 6 (Two-Tier Quality Gate & Commit)**

Always open your response with a compact status banner:
> **Pipeline Status**: Stage X — \<Stage Name\> | **Active Slice**: `slice-XX.md` (or None) | **Assigned Model**: \<Gemini / Ollama `qwen2.5-coder:7b` / Ollama `qwen3.5:9b`\>

---

## Interactive Stage Playbook

### Stage 1: Business Requirements Brief *(Gemini High/Medium)*
1. Read `.ai/constitution.md`.
2. Ask the user **4–6 concise questions** (in one turn) to capture:
   - Core problem & target users
   - Top 3–5 primary workflows
   - Platform specifics (Blazor Server Web vs Windows WiX Desktop vs Android)
   - Key data entities & storage needs
   - Explicit out-of-scope items for v1
3. Draft `.ai/requirements.md` from their answers.
4. **Checkpoint**: Ask the user to approve `.ai/requirements.md` before moving to Stage 2.

### Stage 2: Vertical Slice Technical Mini-Spec *(Gemini High/Medium)*
1. Read `.ai/repo-map.md` (if present) instead of scanning the full repository to check existing interfaces, Razor routes, and SQL tables.
2. Propose a numbered vertical-slice roadmap (short bullet list).
   - For a **brand-new application**, always make **`slice-01.md`** the **Walking Skeleton** (`.NET 10+` LTS solution with `*.Shared.UI`, `*.Web`, `*.Maui` with `WindowsPackageType=None`, `*.Tests`, explicit `Program.Main`, base CSS Grid/Flexbox shell, and one passing smoke/bUnit test).
3. Once the roadmap is approved, create `.ai/slices/slice-XX.md` using `.ai/slices/slice-template.md` with exact target files, interfaces/DTOs, SQL schema, 3–6 Gherkin scenarios, subtasks, and `Risk: High | Low`. Set `Status: Ready`.
4. **Checkpoint**: Ask the user to review `slice-XX.md` and confirm they are ready to run Stage 3 locally.

### Stage 3: Unit & Integration Test Generation *(Local Ollama `qwen2.5-coder:7b`)*
1. Update `slice-XX.md` status to `In Progress`.
2. Guide the user (or run the helper) to generate failing tests from the slice's Gherkin scenarios:
   ```powershell
   .\.ai\run-stage.ps1 -Stage Tests -SlicePath .ai\slices\slice-XX.md [-IncludeUiRules]
   ```
3. Apply the generated test files and run `dotnet test` to confirm the tests fail for the expected reason (Red phase).
4. **Checkpoint**: Confirm tests are in place and failing as expected, then advance to Stage 4.

### Stage 4: Vertical Slice Implementation *(Local Ollama `qwen2.5-coder:7b`)*
1. Guide the user (or run the helper) with **only** the slice spec and target files (`num_ctx: 8192`):
   ```powershell
   .\.ai\run-stage.ps1 -Stage Implement -SlicePath .ai\slices\slice-XX.md -TargetFiles <test-and-target-files> [-IncludeUiRules]
   ```
2. Apply the implementation strictly within the slice's target files, adhering to `.agents/rules/csharp-standards.md` (and `blazor-maui-ui.md` for UI slices).
3. Advance immediately to Stage 5.

### Stage 5: Deterministic Build/Test & Local Repair *(Local Ollama `qwen3.5:9b`, Max 2 Retries)*
1. Run the verification helper (runs `dotnet build -warnaserror` + `dotnet test`, and captures concise errors into `.ai/last-error.txt` on failure):
   ```powershell
   .\.ai\verify-slice.ps1
   ```
2. If green, update `slice-XX.md` status to `In Review` and advance to Stage 6.
3. If red:
   - Run the local repair step using the auto-captured `.ai/last-error.txt`:
     ```powershell
     .\.ai\run-stage.ps1 -Stage Fix -SlicePath .ai\slices\slice-XX.md -TargetFiles <failing-files> -ErrorLog .ai\last-error.txt
     ```
   - Track attempts explicitly (**Attempt 1/2**, **Attempt 2/2**).
   - **Hard Escalation Cap**: If still red after 2 local attempts, stop local retries and escalate directly to **Gemini (High/Medium)** with `.ai/last-error.txt` and current `git diff`.

### Stage 6: Two-Tier Quality Gate & Commit *(CLI -> `qwen3.5:9b` -> Gemini)*
1. Check off completed subtasks in `.ai/slices/slice-XX.md` and stage all slice files:
   ```powershell
   git add <slice-files>
   ```
2. **Pass-1 Local Diff Review (`qwen3.5:9b`)**:
   ```powershell
   .\.ai\run-stage.ps1 -Stage Review -SlicePath .ai\slices\slice-XX.md [-IncludeUiRules]
   ```
   Fix any valid findings, re-run `dotnet build -warnaserror` + `dotnet test`, and re-stage.
3. **Pass-2 Architectural & Security Audit *(Gemini High/Medium — Risk-Gated)***:
   - Check the slice's `Risk` field (and whether Stage 5 escalated):
     - **`Risk: High`** *(or any Stage 5 escalation)*: **Required.** Review the filtered `git diff --staged` against `.ai/constitution.md`, `.agents/rules/csharp-standards.md`, `.agents/rules/blazor-maui-ui.md`, and `.ai/slices/slice-XX.md`. Report clear PASS / CHANGES REQUESTED findings.
     - **`Risk: Low`** *(CSS/layout tweaks, copy, or established CRUD with zero Stage 5 escalations)*: Pass-1 local review + green deterministic gate is sufficient (defer Gemini audit to milestone/PR level).
4. **Completion Checkpoint**:
   - Mark `slice-XX.md` as `Status: Done`.
   - Provide the commit & repo-map refresh commands:
     ```powershell
     git commit -m "feat(slice-XX): <summary>"
     .\.ai\update-repo-map.ps1
     ```
   - Recommend starting a **fresh Gemini chat** (or `/compact`) before moving to the next slice to keep frontier token usage minimal.

