## Operations (Fossilize + audit-check)

### Fossilize trigger (ArchiveStore)
- Buffers `state == Active` entries in memory.
- Fossilize runs when either threshold is hit:
  - `FossilMaxItems = 80` OR
  - `_activeCharSum >= FossilMaxChars (60_000)`
- Emits append-only records:
  - `state=Fossil` with `meta.kind="fossilize"` (+ `summaryEngine`, `summaryMs`)
  - optional `meta.kind="state.transition"`

### Model tagging
- All archive events include `meta.model` (auto-injected in `AuditLogger.WriteArchive` unless already set).

### Audit log location
- `%LocalAppData%\Jarvis\logs\audit-YYYY-MM-DD.jsonl`

### Run audit-check
- Basic:
  - `dotnet run --project javis/Tools/AuditCheck/AuditCheck.csproj -- "<path-to-jsonl>"`
- Custom thresholds:
  - `... --warn-custom-avg 5000 --warn-custom-max 20000`
- Parse error samples:
  - `... --parse-error-samples 10` (more) / `... --parse-error-samples 0` (disable)

### Exit codes (cause ID, severity precedence: 3 > 2 > 4 > 5)
- `0` OK
- `2` schema completeness (missing eventId/tsUnixMs)
- `3` JSONL parse errors
- `4` chat.send.start without end
- `5` custom summary latency high
