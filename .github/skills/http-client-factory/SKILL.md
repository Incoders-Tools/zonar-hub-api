# Skill: HTTP Client Factory

Use this skill for all outbound HTTP calls.

Rules:

- use `IHttpClientFactory`
- centralize timeout/retry policy
- avoid per-call raw `HttpClient` creation
- register clients in DI modules
- keep clients named or typed deliberately
