---
navigation_title: Rollover
---

# Rollover

Rollover creates a new index when the current one reaches certain conditions (age, size, document count). Elastic.Ingest.Elasticsearch supports several rollover approaches.

## Approaches

| Approach | How it works | Serverless? |
|----------|-------------|-------------|
| [Manual alias](manual-alias.md) | Application manages write/read aliases and swaps them after indexing | Yes |
| [Rollover API](rollover-api.md) | Calls `POST {alias}/_rollover` with conditions | Yes |
| [ILM managed](ilm-managed.md) | ILM policy triggers rollover automatically based on conditions | No |
| [Data stream lifecycle](data-stream-lifecycle.md) | Automatic rollover and retention via data stream lifecycle | Yes |

## Choosing an approach

- **Manual alias**: best for batch sync patterns where you control when indices rotate (for example, [catalog data](../../getting-started/catalog-data.md))
- **Rollover API**: best when you want explicit control over rollover timing with conditions
- **ILM managed**: best for self-managed clusters that need multi-phase lifecycle (hot/warm/cold/delete)
- **Data stream lifecycle**: best for serverless and new projects that only need retention-based lifecycle
