# tool-selection

Version: 1

Purpose: Select one registered Model tool from shortlisted capabilities.

Input contract: user question, domain hint, and shortlisted Model capability/tool metadata.

Output contract: selected capability code and selected tool name.

Safety rules: select only from supplied tools; never request dynamic SQL; deny if no Model capability matches.

Provenance requirement: selected capability and tool must be persisted in run metadata.

