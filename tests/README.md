# Integration Tests — User Secrets Setup

All integration test projects share a single
[dotnet user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
store (`UserSecretsId: elastic-ingest-dotnet-tests`), defined in
`tests/Directory.Build.props`. Set your secrets once from **any** test project
directory and every integration test project will read them.

## Setting Secrets

Run these commands from any project under `tests/`:

```bash
cd tests/Elastic.AgentBuilder.IntegrationTests   # or any other test project

# Elasticsearch (for Elastic.Ingest.Elasticsearch.IntegrationTests)
dotnet user-secrets set "Parameters:ElasticsearchUrl" "https://localhost:9200"
dotnet user-secrets set "Parameters:ElasticsearchApiKey" "<base64-api-key>"

# Kibana — Option A: Elastic Cloud ID
dotnet user-secrets set "Parameters:CloudId" "<cloud-id>"
dotnet user-secrets set "Parameters:KibanaApiKey" "<base64-api-key>"

# Kibana — Option B: Direct URL (use instead of CloudId)
dotnet user-secrets set "Parameters:KibanaUrl" "https://my-kibana.example.com:5601"
dotnet user-secrets set "Parameters:KibanaApiKey" "<base64-api-key>"

# Optional
dotnet user-secrets set "Parameters:KibanaSpace" "my-space"
```

## Which Secrets Each Project Reads

| Secret | Ingest.ES | AgentBuilder | Extensions.AI |
|---|:-:|:-:|:-:|
| `Parameters:ElasticsearchUrl` | x | | |
| `Parameters:ElasticsearchApiKey` | x | | |
| `Parameters:CloudId` | | x | x |
| `Parameters:KibanaUrl` | | x | x |
| `Parameters:KibanaApiKey` | | x | x |
| `Parameters:KibanaSpace` | | x | x |

The `Elastic.Extensions.AI.IntegrationTests` automatically create and clean up a
temporary test agent — no agent ID or connector ID secrets are needed.

## Listing & Clearing Secrets

```bash
# List all secrets (run from any test project directory)
dotnet user-secrets list

# Remove a single secret
dotnet user-secrets remove "Parameters:KibanaApiKey"

# Clear all secrets
dotnet user-secrets clear
```

## Running Tests

```bash
# Unit tests (no secrets needed)
dotnet test --project tests/Elastic.AgentBuilder.Tests

# Integration tests (requires secrets above)
dotnet test --project tests/Elastic.AgentBuilder.IntegrationTests
dotnet test --project tests/Elastic.Extensions.AI.IntegrationTests
dotnet test --project tests/Elastic.Ingest.Elasticsearch.IntegrationTests
```
