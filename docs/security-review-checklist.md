# Security Review Checklist

- Tenant header required.
- User header required.
- SQL tenant filter required.
- SQL permission function required.
- No dynamic SQL in tool procedures.
- No secrets in committed configuration.
- Artifact paths scoped by tenant and run.
- LLM receives sanitized context only.

