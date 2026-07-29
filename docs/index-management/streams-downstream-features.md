---
navigation_title: Downstream Streams features
---

# Downstream Streams features

After documents are ingested, Kibana Streams can process and enrich them. These features are **not configured in Elastic.Ingest.Elasticsearch** — they live in Kibana / the Streams API. Good document shape still matters so those features work well.

## Streamlang

[Streamlang](https://www.elastic.co/docs/solutions/observability/streams/streamlang) is a YAML DSL for processors and partition conditions. Streams converts it to ingest pipelines or ES|QL.

**Library implication:** Send consistently typed fields. Prefer stable ECS/OTel names (`message`, `log.level`, `service.name`) so Streamlang `where` conditions and grok/dissect processors match without fighting dynamic mapping surprises.

Wired streams: field naming depends on the endpoint — see [ECS and OTel endpoints](ecs-and-otel-endpoints.md). Classic `[DataStream]` targets: your bootstrapped mappings define the schema Streamlang sees.

## Partitioning (child streams)

Wired streams can route documents into child streams based on partition conditions (often written in Streamlang). Routing keys are typically high-cardinality identifiers such as `service.name` or dataset attributes.

**Library implication:** Populate identity fields on every event. Do not rely on this library to create child streams — enable and configure partitioning in Kibana after data is flowing.

## Significant events

Significant events surface notable conditions in a stream (including query knowledge indicators). They are managed via the Streams UI / API (`significant_events`).

**Library implication:** Keep `@timestamp` accurate and include enough structured context (`service.*`, `error.*`, `event.*`) that ES|QL-based detections can match. The library does not emit significant-event definitions.

## Knowledge indicators

[Knowledge indicators](https://www.elastic.co/docs/solutions/observability/streams/knowledge-indicators) automatically extract structured facts (services, infrastructure, dependencies, schemas) and optional ES|QL query suggestions from log samples.

**Library implication:**

- Prefer readable `message` (or OTel `body.text`) text — extraction samples raw log content
- Include `service.name` / resource attributes so entities merge correctly
- Continuous extraction runs in Kibana with a Generative AI connector — not from the ingest channel

## Summary

| Concern | Owned by |
|---------|----------|
| Bulk ingest, batching, retries | This library |
| Classic template bootstrap | This library (`[DataStream]`) |
| Wired template / lifecycle | Elasticsearch |
| Streamlang, partitions, significant events, KIs | Kibana Streams |
| Field naming at wired ingest | Endpoint (`logs.ecs` / `logs.otel`) |

## Related

- [Streams overview](streams.md)
- [Wired streams](wired-streams.md)
- [Get data into Streams](https://www.elastic.co/docs/solutions/observability/streams/get-data-in) (Elastic docs)
