# Date Time and Localization Instructions

Rules:

- canonical storage and processing in UTC
- centralize date/time conversion and formatting helpers
- do not repeat date parsing logic in many places
- support at least:
  - es
  - en
  - pt
- keep outward-facing messages localization-ready
