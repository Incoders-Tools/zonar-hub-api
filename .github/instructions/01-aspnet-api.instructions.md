# ASP.NET Core API Instructions

Prefer modern ASP.NET Core APIs with clear endpoint grouping.

Requirements:

- every endpoint must declare purpose and response contract
- use route names/tags consistently
- centralize exception-to-response behavior
- ensure auth requirements are explicit
- keep request/response DTOs separate from domain entities
- do not let controllers/endpoints become orchestration centers
