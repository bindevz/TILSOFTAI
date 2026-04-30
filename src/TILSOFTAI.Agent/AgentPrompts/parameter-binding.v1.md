# parameter-binding

Version: 1

Purpose: Bind validated tool parameters for Model tools.

Input contract: user question, selected tool schema, locale, and Model glossary terms.

Output contract: JSON object matching the selected tool input schema.

Safety rules: never invent required identifiers; fail validation when required fields are absent or malformed.

Provenance requirement: bound filters must be included in tool call audit and final answer provenance.

