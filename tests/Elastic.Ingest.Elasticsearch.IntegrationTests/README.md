# Integration Tests

Integration tests for `Elastic.Ingest.Elasticsearch`, powered by [TUnit](https://tunit.dev/) and
[Elastic.TUnit.Elasticsearch.Core](https://www.nuget.org/packages/Elastic.TUnit.Elasticsearch.Core/).

## Running tests

### Ephemeral cluster (default)

By default, running the tests will download, install, and start a local Elasticsearch cluster
automatically. This takes ~60 seconds on first run (subsequent runs reuse the cached download).

```bash
dotnet run --project tests/Elastic.Ingest.Elasticsearch.IntegrationTests
```

### Using a remote cluster (recommended for local development)

To skip the ephemeral bootstrap and run against an already-running cluster, configure
connection details via **dotnet user-secrets**. This dramatically reduces iteration time.

```bash
# Initialize secrets for this project (one-time)
dotnet user-secrets init --project tests/Elastic.Ingest.Elasticsearch.IntegrationTests

# Set your Elasticsearch URL
dotnet user-secrets --project tests/Elastic.Ingest.Elasticsearch.IntegrationTests \
  set Parameters:ElasticsearchUrl https://localhost:9200

# Set your API key (if the cluster requires authentication)
dotnet user-secrets --project tests/Elastic.Ingest.Elasticsearch.IntegrationTests \
  set Parameters:ElasticsearchApiKey YOUR_API_KEY
```

Then run the tests normally — the cluster will detect the secrets and connect to your
remote instance instead of starting an ephemeral one:

```bash
dotnet run --project tests/Elastic.Ingest.Elasticsearch.IntegrationTests
```

### Using environment variables

You can also point at an external cluster via environment variables (no code changes needed):

```bash
TEST_ELASTICSEARCH_URL=https://localhost:9200 \
TEST_ELASTICSEARCH_API_KEY=your-api-key \
dotnet run --project tests/Elastic.Ingest.Elasticsearch.IntegrationTests
```

### Resolution order

The cluster resolves its connection in this order:

1. **`TryUseExternalCluster()`** — reads `Parameters:ElasticsearchUrl` and
   `Parameters:ElasticsearchApiKey` from dotnet user-secrets
2. **Environment variables** — `TEST_ELASTICSEARCH_URL` and `TEST_ELASTICSEARCH_API_KEY`
   (built into `Elastic.TUnit.Elasticsearch.Core`)
3. **Ephemeral startup** — downloads and starts a local Elasticsearch instance

## Clearing secrets

```bash
dotnet user-secrets --project tests/Elastic.Ingest.Elasticsearch.IntegrationTests clear
```

## Test architecture

- **`IngestionCluster`** — extends `ElasticsearchCluster<ElasticsearchConfiguration>` from
  `Elastic.TUnit.Elasticsearch.Core`. Provides a shared `ElasticsearchClient` with debug mode
  and per-test output routing. Supports external cluster resolution via user-secrets.
- **`SecurityCluster`** — extends `IngestionCluster` with `ClusterFeatures.Security` enabled
  and trial mode for testing authentication failure scenarios.
- **`IntegrationTestBase`** — convenience base class exposing `Cluster` and `Client` properties.
- Test classes use `[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, ...)]` to
  share a single cluster instance across all tests that need it.
