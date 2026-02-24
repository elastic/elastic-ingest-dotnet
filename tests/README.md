# Integration Tests — User Secrets Setup

Integration test projects use [dotnet user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
to store connection details for Elasticsearch and Kibana. Secrets are stored per-project and
never checked into source control.

## Elastic.Ingest.Elasticsearch.IntegrationTests

```bash
cd tests/Elastic.Ingest.Elasticsearch.IntegrationTests

dotnet user-secrets set "Parameters:ElasticsearchUrl" "https://localhost:9200"
dotnet user-secrets set "Parameters:ElasticsearchApiKey" "<base64-api-key>"
```

When these secrets are **not** set the tests spin up an ephemeral Elasticsearch cluster
automatically. Set them when you want to run against an external (e.g. Cloud) deployment.

## Elastic.AgentBuilder.IntegrationTests

These tests require a running Kibana instance with the Agent Builder feature enabled.

### Option A — Elastic Cloud ID

```bash
cd tests/Elastic.AgentBuilder.IntegrationTests

dotnet user-secrets set "Parameters:CloudId" "<cloud-id>"
dotnet user-secrets set "Parameters:KibanaApiKey" "<base64-api-key>"
```

### Option B — Direct Kibana URL

```bash
cd tests/Elastic.AgentBuilder.IntegrationTests

dotnet user-secrets set "Parameters:KibanaUrl" "https://my-kibana.example.com:5601"
dotnet user-secrets set "Parameters:KibanaApiKey" "<base64-api-key>"
```

### Optional — Kibana Space

```bash
dotnet user-secrets set "Parameters:KibanaSpace" "my-space"
```

## Listing & Clearing Secrets

```bash
# List all secrets for a project
dotnet user-secrets list

# Remove a single secret
dotnet user-secrets remove "Parameters:KibanaApiKey"

# Clear all secrets for a project
dotnet user-secrets clear
```

## Running Tests

```bash
# Unit tests (no secrets needed)
dotnet test --project tests/Elastic.AgentBuilder.Tests

# Integration tests (requires secrets above)
dotnet test --project tests/Elastic.AgentBuilder.IntegrationTests
dotnet test --project tests/Elastic.Ingest.Elasticsearch.IntegrationTests
```
