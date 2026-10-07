# ADR-0002 — MVP hand-history import by upload, asynchronous server-side processing

- Status: Accepted
- Date: 2026-10-07

## Context

The master spec describes filesystem watching (FileSystemWatcher, debounce, reconciliation).
A hosted ASP.NET Core backend cannot see the player's disk, and a browser cannot watch a folder
in the background (the File System Access API is Chromium-only, needs a permission prompt per
session and stops when the tab closes). The Desktop companion is Phase 12.

We still need a reliable way to get Winamax files into the platform for MVP-1.

## Decision

1. **MVP input = upload.** The player drags `.txt` files, a folder, or a `.zip` into the web app.
2. **Processing is asynchronous.** The upload endpoint stores the raw files, records one
   `ImportedFile` row per file and returns `202 Accepted` with a batch id. A background worker
   parses, normalizes and persists. The UI polls `GET /api/import/batches/{id}`.
3. **Durable queue = PostgreSQL.** Pending files are claimed with
   `SELECT … FOR UPDATE SKIP LOCKED`. No Hangfire, no message broker. A restart resumes
   unfinished files.
4. **Idempotency is layered:**
   - file level: SHA-256 of content per poker account → skips exact re-uploads (optimization);
   - hand level: unique constraint `(provider, poker_account_id, external_hand_id)` → the
     correctness guarantee, needed because a tournament file grows during play and may be
     uploaded twice with different content;
   - tournament level: unique constraint `(provider, poker_account_id, external_tournament_id)`.
5. **Raw files are kept** (compressed) so that every hand can be re-parsed after a parser fix.
   Normalized data and aggregates must be rebuildable from raw files. Raw files are deleted with
   the account or the poker history (privacy, see ADR to come on opponent data retention).
6. **The parser is a pure library** (`IHandHistoryParser`, no hosting or EF dependency) so that
   the future Desktop agent can reuse it unchanged. Only the input source changes:

```
Upload (MVP)      ─┐
Desktop agent     ─┼─▶ Fingerprint ─▶ Import state ─▶ Parse ─▶ Normalize ─▶ Persist ─▶ Schedule stats
(Phase 12)        ─┘
```

## Untrusted input rules

- Accept only `.txt` and `.zip`; check content, not just the extension.
- Configurable limits: max file size, max files per batch, max uncompressed zip size, max entry
  count, max compression ratio (zip bomb). Nested archives are rejected.
- Zip entries are read as streams, never extracted to disk using their names (path traversal).
- Encoding is validated (expected UTF-8 — to be confirmed on real files); invalid files are
  marked `Failed` with a machine-readable reason, never partially guessed.
- Original client paths are never stored.

## Alternatives considered

| Option | Why not |
|---|---|
| File System Access API | Chromium-only, permission per session, no background work. Possible later as a convenience for Chrome users. |
| Minimal headless desktop agent in MVP | Signing, notarization, distribution and support on macOS and Windows before product value is proven. |
| Synchronous parsing in the HTTP request | Large batches would time out; no retry; no restart safety. |
| Hangfire / broker | Extra dependency and ops for a queue PostgreSQL handles at this scale. |

## Trade-offs

- Manual upload adds friction after every session. Accepted for MVP; the Desktop agent removes it.
- Keeping raw files costs storage and privacy surface, but without them a parser bug means data
  loss. Correctness wins.

## Open questions (blocked on real Winamax samples)

- Are tournament results (prize, finish position, entrants, re-entries, bounty winnings) only in
  summary files? If a summary is missing, these fields are `UNKNOWN`, never estimated.
- File encoding and line endings.
- How the hero is identified, and how a poker account is linked to the user on first import.

## When to revisit

- Desktop agent available (Phase 12): it becomes the default input; upload stays as a fallback.
- Queue throughput becomes a bottleneck (measure first).
