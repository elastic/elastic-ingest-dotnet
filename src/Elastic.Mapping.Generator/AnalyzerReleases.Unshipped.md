### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ELASTIC001 | Elastic.Mapping | Error | Only one [AiEnrichment<T>] is allowed per entity type across all mapping contexts.
EMAP001 | Elastic.Mapping | Error | AddField used on an object/nested parent field; use AddProperty instead.
EMAP002 | Elastic.Mapping | Error | AddProperty used on a leaf parent field; use AddField instead.
