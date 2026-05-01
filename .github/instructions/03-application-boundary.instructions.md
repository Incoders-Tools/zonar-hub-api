# Application Boundary Instructions

At the application boundary:

- map transport contracts to application commands/queries
- validate requests before business orchestration
- separate business rule validation from transport validation
- return stable error contracts and machine-readable error codes
- keep localized messages separable from error codes
