# External Integrations Instructions

All external providers must be encapsulated.

Rules:

- use IHttpClientFactory
- centralize resilience and timeout policy
- define provider contracts separately from internal contracts
- use adapters/mappers/ACLs
- never leak provider DTOs into domain logic
