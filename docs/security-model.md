# Security Model

The API requires tenant and user context. SQL stored procedures repeat tenant and permission enforcement. Artifacts are stored under tenant/run scoped paths and exposed through metadata endpoints only.

