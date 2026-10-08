# ADR-0005 — Import pipeline: PostgreSQL queue, idempotent storage, automatic poker account linking

- Status: Accepted
- Date: 2026-10-09

## Context

MVP-1 imports Winamax hand histories and tournament summaries uploaded from the browser (ADR-0002).
Files are untrusted, can be large (a deep run is a few MB, a zip of a month several hundred), are often
uploaded again (whole folder re-uploaded, file still growing during the tournament) and must never create
duplicates or half-imported data. The user's Winamax pseudonym is not known in advance.

## Decision

**Two steps: accept, then process in the background.**

- `POST /api/import/files` (multipart, `.txt` and `.zip`) validates and expands the upload
  (`UploadExpander`: bare file names only, sizes enforced on bytes actually read, no nested archives,
  zip-bomb guard), stores each file and answers `202 Accepted` with a batch id. The client polls
  `GET /api/import/batches/{id}`.
- A `BackgroundService` (`ImportWorker`) drains the queue. The queue is the `import.imported_files` table:
  claims use `UPDATE … FOR UPDATE SKIP LOCKED`, so several API instances can process in parallel without
  coordination. A claim expires after 10 minutes (crashed worker); a file is retried after an error and
  marked `failed` (`PROCESSING_FAILED`) after 3 attempts.
- Each file is processed in **one transaction**: either everything it contains is stored and the file
  is `completed`, or nothing is and it is retried.

**Idempotency by database constraints, not by checks in code.**

| Level | Natural key (unique) | On conflict |
|---|---|---|
| File | `(user_id, content_sha256)` | not stored again; reported `alreadyImported` — unless it had failed: queued again |
| Poker account | `(user_id, room, screen_name)` | existing account reused |
| Tournament | `(poker_account_id, external_tournament_id)` | hands complete unknown facts; a summary overwrites summary facts |
| Hand | `(poker_account_id, external_hand_id)` | skipped, counted `handsAlreadyPresent` |

A file uploaded while the tournament is running and again once complete therefore ends with every hand
exactly once. Insert-or-get is written in SQL (`ON CONFLICT`); the "get" is a separate statement so that
it sees a row committed concurrently (a CTE's snapshot would not).

**Raw files are kept**, gzip-compressed in PostgreSQL (`content_gzip`): they allow re-importing after a
parser fix and investigating rejections (rejections store line numbers, never line content). Moving them
to object storage is a later, local change behind `IImportStore`.

**Hands storage:** columns for what statistics filter and sort on (time, level, blinds, hero seat, stack
and cards, pot) and the full hand as a versioned JSON document (`details jsonb`, `HandDetailsDocument`),
owned by persistence rather than by the parser's records.

**Poker accounts are created automatically** from the hero pseudonym found in the files ("Dealt to …" on
hands, the player of a summary). They start unconfirmed (`confirmed_at` null): the web app asks the user
to confirm the first time. "This is not me" deletes the account with its tournaments, hands and the files
imported into it (cascading foreign keys), so the right files can be uploaded again.

Hands whose hero or tournament id is missing are rejected (`HERO_NOT_FOUND`, `TOURNAMENT_UNKNOWN`), never
guessed.

## Alternatives considered

| Option | Why not |
|---|---|
| Parse synchronously in the request | Request time grows with upload size; a timeout leaves the client unsure of what was imported. |
| Message broker (RabbitMQ, Azure Service Bus) or Hangfire | Infrastructure or dependency to operate for a queue that PostgreSQL already provides with `SKIP LOCKED` at our volume. |
| Deduplicate in application code (read then insert) | Races between concurrent imports; constraints are the only reliable guard. |
| Raw files on disk / object storage now | Needs a storage decision tied to hosting (not made yet); PostgreSQL keeps backups and transactions in one place. |
| Ask the pseudonym at sign-up | Extra step before any value; error-prone (case, spaces). The files already tell us. |

## Consequences

- Up to ~2 s latency before processing starts (polling). LISTEN/NOTIFY can remove it if it matters.
- Database size grows with raw files (≈ 5–10× compression on hand histories). Retention of raw files
  is to be decided with the opponent-data ADR (pseudonyms of other players are in them and in `details`).
- Two users can each import files with the same pseudonym; accounts are per user. Detecting that is a
  product question for later (it does not leak data between users).
- Hands are inserted one statement per hand; batching (`NpgsqlBatch`/`COPY`) is the first optimization
  if large imports become slow.

## When to revisit

- The desktop agent (Phase 12) uploads continuously: the same pipeline applies, with smaller batches.
- Raw file volume or retention requirements → object storage.
- Processing throughput insufficient with one worker per instance.
