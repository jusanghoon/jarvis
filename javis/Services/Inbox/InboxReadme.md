# Inbox (daily)

- Write model: append-only JSONL per day: `events-YYYY-MM-DD.jsonl`
- Index: append-only tab-separated index: `events-YYYY-MM-DD.idx.jsonl`
  - columns: `kind<TAB>startOffset<TAB>byteLen`
- Compaction:
  - On exit / next startup best-effort.
  - Original events are never deleted automatically.
