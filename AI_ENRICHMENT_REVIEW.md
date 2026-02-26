# AI Enrichment — Review & Remediation Plan

Post-review of the initial two commits. Covers bugs, refactoring, dead code,
and integration test gaps — in priority order.

---

## 1. Bug: `CleanupOrphanedAsync` ID mismatch

**Severity:** High — silently marks everything as orphaned and deletes it.

**Problem:**
`CleanupOrphanedAsync` collects URLs from the lookup index, then uses
`_mget` against the target index with `UrlHash(url)` as the `_id`:

```csharp
var mgetDocs = string.Join(",", urls.Select(u => $"{{\"_id\":\"{UrlHash(u)}\"}}"));
```

This assumes the target index uses `SHA256(url)` as the document `_id`.
In practice, `AiDocumentationPage` uses `[Id]` on the `Url` property, so
the actual `_id` is the raw URL string (e.g. `/getting-started`), not
the hash. Every `_mget` returns `found: false`, so all entries are
classified as orphans and deleted.

**Fix:**
Replace the `_mget` approach with a terms query against the match field:

1. Collect URLs from the lookup index in batches (already done).
2. For each batch, run a `_count` or `_search` with a `terms` filter on
   the match field against the target index.
3. Compare returned URLs against the batch to find orphans.

Alternatively, simplify by using a scripted `_delete_by_query` that checks
existence via a terms lookup from the target, but that may be less
transparent. The terms-query approach is safer and testable.

Also replace the scroll-based iteration with `PointInTimeSearch` for
consistency with the rest of the codebase. We don't need `TDocument`
deserialization here — only the match field value — so we can use
`PointInTimeSearch<JsonElement>` or a raw `search_after` loop using the
same pattern as `PointInTimeSearch` (PIT + `search_after`) without scroll
contexts.

---

## 2. Refactor: Extract duplicated staleness clause builder

**Severity:** Medium — code duplication, divergence risk.

**Problem:**
`BuildCandidateQuery` (line 286) and `BackfillAsync` (line 496) both
construct the same `should` clauses:

```
for each field:
  - bool.must_not.exists(field)
  - bool.must_not.term(field_ph, current_hash)
```

If one is updated but the other isn't, candidates and backfill diverge.

**Fix:**
Extract into a private method:

```csharp
private string BuildStalenessShouldClauses()
{
    var clauses = new List<string>();
    foreach (var field in _provider.EnrichmentFields)
    {
        clauses.Add($"{{\"bool\":{{\"must_not\":{{\"exists\":{{\"field\":\"{field}\"}}}}}}}}");
        if (_provider.FieldPromptHashFieldNames.TryGetValue(field, out var phField)
            && _provider.FieldPromptHashes.TryGetValue(field, out var phValue))
            clauses.Add($"{{\"bool\":{{\"must_not\":{{\"term\":{{\"{phField}\":\"{phValue}\"}}}}}}}}");
    }
    return string.Join(",", clauses);
}
```

Both `BuildCandidateQuery` and `BackfillAsync` call this. The full
`bool.should` wrapper with `minimum_should_match:1` can also be shared.

---

## 3. Refactor: Fragile JSON splicing in `BulkUpsertLookupAsync`

**Severity:** Medium — correctness risk on edge cases.

**Problem:**
The bulk upsert builds NDJSON by stripping the leading `{` from
`ParseResponse`'s output via `json.Substring(1, json.Length - 1)` and
splicing it into a larger string. If `ParseResponse` ever returns JSON
with leading whitespace (unlikely but possible after `Trim()` changes)
or the format changes, the splicing breaks silently.

Additionally, the `_update` action line manually embeds the URL hash
without JSON-escaping it. If the hash contains special characters
(it won't for SHA256, but defensive coding matters), it could produce
invalid NDJSON.

**Fix:**
Structure `BulkUpsertLookupAsync` to compose the `doc` object explicitly
using `Utf8JsonWriter`, merging the match field, `created_at`, and the
parsed enrichment fields. This means `ParseResponse` should return a
parsed structure (or `JsonDocument`) rather than a raw JSON string — or
we parse its output here. The simplest change is to parse the JSON string
returned by `ParseResponse` and re-emit it as part of the `Utf8JsonWriter`
flow:

```csharp
using var partialDoc = JsonDocument.Parse(json);
foreach (var prop in partialDoc.RootElement.EnumerateObject())
    prop.WriteTo(writer);
```

This eliminates the substring hack entirely.

---

## 4. Refactor: `EmitBuildPrompt` hardcodes `_in1` as the "body" input

**Severity:** Medium — wrong behavior if inputs are declared in a different order.

**Problem:**
The emitter generates a null-check on `_in1` (the second input) as the
"skip if empty" guard. If someone declares `[AiInput]` on `Body` first
and `Title` second, it checks the wrong field. If there's only one input,
it checks `_in0`, which is correct, but the two-input case is fragile.

**Fix:**
Option A: Check ALL inputs — if any required input is missing/empty, skip.

```csharp
// Emit: if (_in0 is null or "" || _in1 is null or "" || ...) return null;
```

This is the safest approach. Every `[AiInput]` field is required for a
valid prompt. If any is blank, skip the document.

Option B: Add a `Required` property to `[AiInput]` to let users mark
which inputs are optional. Default: required.

Start with Option A (simplest), iterate to Option B if needed.

---

## 5. Dead code: `AiEnrichmentModel.StjConfig`

**Severity:** Low — dead field, no runtime impact.

**Problem:**
`AiEnrichmentModel` stores `StjContextConfig? StjConfig` and the
analyzer passes it through, but nothing in `AiEnrichmentEmitter` reads it.
It was needed for the abandoned `_AiJsonContext` approach.

**Fix:**
Remove the `StjConfig` parameter from `AiEnrichmentModel`, the analyzer's
`Analyze` method signature, and the call site in `MappingSourceGenerator`.

---

## 6. Hardcoded `PipelineName` — no versioning

**Severity:** Low — works today because PUT is idempotent, but can cause
confusion.

**Problem:**
`EnrichPolicyName` includes a fields hash (`ai-enrichment-policy-{hash}`),
but `PipelineName` is a static `"ai-enrichment-pipeline"`. If the pipeline
script changes (fields added/removed), the old pipeline is silently
overwritten, which is fine functionally but could clash if multiple
document types share the same cluster.

**Fix:**
Append the same fields hash to the pipeline name:
`ai-enrichment-pipeline-{hash}`. This makes it unique per field set and
safe for multi-type clusters.

---

## 7. `CallCompletionAsync` — minor allocation optimization

**Severity:** Low — LLM latency dominates.

**Problem:**
Each `CallCompletionAsync` call allocates a `MemoryStream` +
`Utf8JsonWriter` to write `{"input":"..."}`.

**Fix:**
Use `ArrayBufferWriter<byte>` instead of `MemoryStream`. Or, since the
body is always `{"input":"<prompt>"}`, use `JsonEncodedText` + a simple
string concatenation:

```csharp
var encoded = JsonEncodedText.Encode(prompt);
var body = $"{{\"input\":\"{encoded}\"}}";
```

This avoids stream/writer allocation entirely. The `JsonEncodedText` call
handles all JSON escaping correctly.

---

## 8. Integration test gaps

### 8a. Per-field staleness / partial re-enrichment (HIGH)

**What's missing:**
This is the core feature — changing one field's prompt should only
regenerate that field. Not tested at all.

**Test plan:**
1. Initialize enrichment infrastructure.
2. Seed the lookup index with a document that has `ai_summary` + 
   `ai_summary_ph` (with the correct current hash) but no `ai_questions`.
3. Index the corresponding document into the secondary.
4. Run `EnrichAsync`.
5. Verify the candidate query found the document (missing `ai_questions`).
6. Verify `ai_summary` was NOT re-requested (the lookup entry's
   `ai_summary` should be unchanged).

Since LLM calls may fail without an endpoint, this can be partially
tested by verifying the candidate query returns the right count and
the `DetermineStaleFields` logic (by checking the generated provider's
`BuildPrompt` only includes the stale field).

### 8b. `CleanupOrphanedAsync` (HIGH — currently buggy, untested)

**Test plan:**
1. Initialize enrichment.
2. Seed 3 lookup entries: `url=/a`, `url=/b`, `url=/c`.
3. Index only `url=/a` and `url=/b` into the target.
4. Run `CleanupOrphanedAsync(targetIndex)`.
5. Verify lookup has exactly 2 entries (`/a` and `/b`).
6. Verify `/c` was removed.

### 8c. `CleanupOlderThanAsync` with real data (MEDIUM)

**Test plan:**
1. Initialize enrichment.
2. Seed a lookup entry with `created_at` = 60 days ago.
3. Seed a lookup entry with `created_at` = now.
4. Run `CleanupOlderThanAsync(TimeSpan.FromDays(30))`.
5. Verify only the recent entry remains.

### 8d. `MaxEnrichmentsPerRun` throttling (MEDIUM)

**Test plan:**
1. Initialize enrichment with `MaxEnrichmentsPerRun = 2`.
2. Index 5 documents into the secondary.
3. Run `EnrichAsync`.
4. Verify `result.ReachedLimit == true`.
5. Verify `result.Enriched + result.Failed <= 2`.
6. Verify remaining 3 documents are still unenriched.

### 8e. Backfill in isolation (MEDIUM)

**Test plan:**
1. Initialize enrichment.
2. Seed a lookup entry for `url=/test-page` with enrichment data.
3. Re-execute the enrich policy.
4. Index a document with `url=/test-page` into the secondary (no AI
   fields).
5. Run `_update_by_query` with the pipeline.
6. Verify the document now has the AI fields from the lookup.

This tests the enrich policy + pipeline independently from the LLM.

### 8f. Incremental enrichment on second run (MEDIUM)

**Test plan:**
1. Initialize + index 3 docs + enrich (or seed lookup directly).
2. Verify lookup has entries.
3. Delete and recreate the secondary index (simulating rollover).
4. Re-index the same 3 docs.
5. Run the pipeline (via `_update_by_query` or natural ingest).
6. Verify docs have AI fields from lookup without any new LLM calls.

This validates the core rollover-survival scenario.

### 8g. `ParseResponse` edge cases in unit tests (LOW)

**Test plan (unit tests on generated provider):**
1. LLM returns valid JSON but missing one field → only present fields
   parsed.
2. LLM returns JSON with extra fields → extra fields ignored.
3. LLM returns empty object `{}` → returns null.
4. LLM returns nested markdown fences (triple backtick inside content).
5. LLM returns JSON with unicode characters in values.
6. LLM returns array for a string field / string for an array field →
   graceful handling.

---

## Execution order

| # | Item | Type | Files touched |
|---|------|------|---------------|
| 1 | Fix `CleanupOrphanedAsync` ID mismatch | Bug fix | `AiEnrichmentOrchestrator.cs` |
| 2 | Extract staleness clause builder | Refactor | `AiEnrichmentOrchestrator.cs` |
| 3 | Fix `BulkUpsertLookupAsync` JSON splicing | Refactor | `AiEnrichmentOrchestrator.cs` |
| 4 | Fix `EmitBuildPrompt` input null-check | Refactor | `AiEnrichmentEmitter.cs` |
| 5 | Remove dead `StjConfig` from model | Cleanup | `AiEnrichmentModel.cs`, `AiEnrichmentAnalyzer.cs`, `MappingSourceGenerator.cs` |
| 6 | Version `PipelineName` with fields hash | Refactor | `AiEnrichmentEmitter.cs` |
| 7 | Simplify `CallCompletionAsync` body | Refactor | `AiEnrichmentOrchestrator.cs` |
| 8a | Integration test: partial staleness | Test | `AiEnrichmentIntegrationTests.cs` |
| 8b | Integration test: `CleanupOrphanedAsync` | Test | `AiEnrichmentIntegrationTests.cs` |
| 8c | Integration test: `CleanupOlderThanAsync` | Test | `AiEnrichmentIntegrationTests.cs` |
| 8d | Integration test: `MaxEnrichmentsPerRun` | Test | `AiEnrichmentIntegrationTests.cs` |
| 8e | Integration test: backfill in isolation | Test | `AiEnrichmentIntegrationTests.cs` |
| 8f | Integration test: rollover survival | Test | `AiEnrichmentIntegrationTests.cs` |
| 8g | Unit tests: `ParseResponse` edge cases | Test | `AiEnrichmentGeneratorTests.cs` |

Items 1-3 can be done as a single commit (orchestrator refactoring).
Items 4-7 as a second commit (emitter + model cleanup).
Items 8a-8g as a third commit (comprehensive test coverage).
