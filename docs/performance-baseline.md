# Performance Baseline

MVP local deterministic baseline:

- Capability retrieval: in-process keyword ranking.
- Tool runtime: bounded to configured `Agent:MaxRowsPerTool`.
- AI timeout: configured by `Ai:OpenAICompatible:RequestTimeoutSeconds`.
- Artifact write: filesystem SHA-256 checksum per artifact.

