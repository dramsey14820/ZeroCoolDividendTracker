# ZeroCool AI Coding Pipeline — Agent Routing & Execution Guide

This repository uses the **ZeroCool AI Coding Pipeline**, a 6-stage hybrid workflow that pairs **Gemini** (cloud reasoning, architecture, and final audit) with **local Ollama models** (fast, zero-cost TDD code generation, local error patching, and Pass-1 diff review).

## Local Runtime & Hardware Profile

- **Ollama Endpoint**: `http://localhost:11434`
- **Hardware Ceiling**: NVIDIA GeForce RTX 4060 (8 GB VRAM), 16 GB system RAM
- **Local Models**:
  - `qwen2.5-coder:7b` — Primary unit/integration test & vertical-slice code generation (`num_ctx: 8192`)
  - `qwen3.5:9b` — Local reasoning, compiler/test error patching, and Pass-1 diff review (`num_ctx: 8192`)

---

## 6-Stage Hybrid Routing Rules

| Stage | Name | Assigned Model | Primary Artifact / Output |
| :--- | :--- | :--- | :--- |
| **1** | Business Requirements Brief | **Gemini (High/Medium)** | Problem statement, constraints, non-goals, user flows |
| **2** | Vertical Slice Technical Mini-Spec | **Gemini (High/Medium)** | `.ai/slices/slice-XX.md` (contracts, schema, Gherkin, subtasks) |
| **3** | Unit & Integration Test Generation | **Ollama `qwen2.5-coder:7b`** | Failing xUnit tests mapped to slice Gherkin scenarios |
| **4** | Vertical Slice Implementation | **Ollama `qwen2.5-coder:7b`** | Production code implementing the slice contracts |
| **5** | Build & Test Error Fixes | **Ollama `qwen3.5:9b`** (max 2 retries) | Green `dotnet build -warnaserror` and `dotnet test` |
| **6** | Two-Tier Quality Gate & Code Review | **CLI + `qwen3.5:9b` + Gemini** | Pass-1 local diff review -> Pass-2 Gemini architectural/security audit |

### Stage Details

1. **Stage 1 — Business Requirements Brief (Gemini High/Medium)**
   - Clarify business outcomes, edge cases, non-functional requirements, and explicit out-of-scope items before writing code.
2. **Stage 2 — Vertical Slice Technical Mini-Spec (Gemini High/Medium)**
   - Break features into small, independent vertical slices using `.ai/slices/slice-template.md`.
   - Save each slice as `.ai/slices/slice-XX.md` with exact target file paths, interfaces/DTOs, SQL schema changes, 3–6 compact Gherkin scenarios, and an implementation checklist.
3. **Stage 3 — Unit & Integration Test Generation (Ollama `qwen2.5-coder:7b`)**
   - Translate the slice's Gherkin scenarios directly into failing xUnit tests (using FluentAssertions and NSubstitute).
4. **Stage 4 — Slice Implementation (Ollama `qwen2.5-coder:7b`)**
   - Implement the production code for the listed target files strictly adhering to `.ai/constitution.md` and `.agents/rules/csharp-standards.md`.
5. **Stage 5 — Build & Test Error Fixes (Ollama `qwen3.5:9b`, Max 2 Local Retries)**
   - Run `dotnet build -warnaserror` and `dotnet test`.
   - On failure, feed **only** the concise compiler/test errors and the affected file(s) to `qwen3.5:9b`.
   - **Hard Escalation Cap**: Maximum **2 local retry attempts**. If the build or tests are still red after attempt 2, stop local retries and escalate to **Gemini (High/Medium)** with the exact error output and minimal diff.
6. **Stage 6 — Two-Tier Quality Gate & Code Review**
   - **Deterministic Gate**: `dotnet build -warnaserror` and `dotnet test` must both pass with zero warnings and zero failures.
   - **Pass-1 Local Diff Review (`qwen3.5:9b`)**: Review `git diff --staged` against `.agents/rules/csharp-standards.md` and the active slice's Gherkin scenarios to catch obvious defects, missing edge cases, or rule violations.
   - **Pass-2 Architectural & Security Audit (Gemini High/Medium)**: Review `git diff --staged` for architectural alignment, security vulnerabilities (SQL injection, auth/authz, secret handling), contract compliance, and cross-slice impact before committing.

---

## Strict Local Context Budget (`num_ctx: 8192`)

To prevent VRAM spillover and context degradation on the RTX 4060 (8 GB VRAM), **every local Ollama session MUST stay strictly within `8192` tokens**:

- **Include ONLY**:
  1. `.ai/constitution.md` (<300 tokens)
  2. The active slice mini-spec (`.ai/slices/slice-XX.md`)
  3. The specific target source/test file(s) currently being created or edited
  4. Concise failing compiler diagnostics or failing test output (when in Stage 5)
- **NEVER Include**:
  - Full-repository dumps or directory-wide concatenations
  - Unrelated slices, historical diffs, or unrelated features
  - Lockfiles, `bin/` / `obj/` outputs, or verbose MSBuild diagnostic logs

---

## Supporting Pipeline Files

- **Project Rulebook**: `.ai/constitution.md`
- **C# / .NET Standards**: `.agents/rules/csharp-standards.md`
- **Slice Execution Workflow**: `.agents/skills/feature-slice/SKILL.md`
- **Slice Spec Template**: `.ai/slices/slice-template.md`

