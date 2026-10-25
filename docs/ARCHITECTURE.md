# ShepherdAPI Architecture — Alpha Version

Status: Approved architecture, implemented through GitHub Issues #1–#6 (see §8). Where this document and an issue ever disagree, the issue as implemented wins; this document is kept in step with them.
Scope: Alpha version only. This document does not describe a target end-state; it describes what Alpha is authorized to build.

## 1. Purpose

Good Shepherd is an experimental system for measuring agentic software development performance. ShepherdAPI is its durable API boundary.

The Alpha version supports rapid, accurate human recording and retrieval of experiment scores, notes, and testing evidence metadata. It serves one client (a separate effort, out of scope here) in this version, but the API is designed so it is not unnecessarily hard to add further clients — including automated agents — later, without building any distributed-system or scaling infrastructure now.

ShepherdAPI is a private, low-volume system for Alpha. The architecture optimizes for correctness, development speed, maintainability, traceability, and straightforward evolution — not throughput or scale.

## 2. System Boundary

- ShepherdAPI is the sole system boundary. Clients never access the `Experiments` database directly.
- The existing `Experiments` database ([database/Create_Experiments_Database_Version_10.sql](../database/Create_Experiments_Database_Version_10.sql), the current V10 baseline) is authoritative. Its DDL is not redesigned. The only schema changes beyond the original design are explicit and authorized: `ExperimentVersion.LockedUTC` (§5.3), the `ScoreObservationSnapshot` table (§5.7), the snapshot `CHECK` constraints (§5.7), which were added through [database/upgrade/](../database/upgrade/) and are folded into V10, and the removal of `ScoreRule.RowState` in v9 (§5.5, Issue #7).
- All new persistent operations pass through ShepherdAPI.
- `Execution.ExecutionID` is `BIGINT`.
- UTC is used for all persisted timestamps.
- ShepherdAPI exposes an OpenAPI contract with Swagger/interactive documentation enabled for Alpha development and manual testing. The contract is committed as [contracts/openapi.json](../contracts/openapi.json) and a test fails when the generated contract drifts from it. Clients (ShepherdWeb and future clients) generate typed clients from that contract rather than duplicating API models by hand.

## 3. Out of Scope (Alpha)

Authentication/authorization, database permissions (deferred, see §7), community submissions/challenges, multi-tenancy, a public-facing API, billing, analytics, automated agent scoring, agent orchestration, dashboards, cloud artifact storage, production deployment mechanisms, ShepherdWeb's validation and presentation of API errors, and mutation of completed historical experiment data (see §5 for what "completed" means).

## 4. Data Model

The data model is the existing `Experiments` database DDL plus the additive changes listed in §2. ShepherdAPI does not introduce parallel or duplicate persistence — it is a thin, explicit boundary over this schema, with its integrity rules enforced in T-SQL objects that live in the repository (§5.8, §6.1).

### 4.1 Entity overview

```
ExperimentVersion
 ├─ Iteration
 │   └─ Execution
 │       ├─ Artifact
 │       └─ ScoreObservation
 │           └─ ScoreObservationEvidence ──> Artifact
 ├─ Parameter
 ├─ ScoreRule ──> Phase, ScoreCategory
 └─ ScoreObservationSnapshot   (written only by finalization, §5.7)

Reference/lookup (global, not owned by any ExperimentVersion):
 Agent, ArtifactType, Phase, Rater, ScoreCategory, Status
```

`Rater.AgentID` optionally links a rater to an `Agent`, but Alpha only exercises human raters — automated agent scoring is out of scope (§3).

### 4.2 Excluded table

`ETLSourceAudit` is historical migration lineage that was mistakenly included in the DDL. It is retained in the database but ShepherdAPI exposes no endpoints for it and does not treat it as part of the domain model.

### 4.3 Legacy evidence fields

`Execution.ArtifactURI` and `ScoreObservation.EvidenceURI` are free-text legacy columns that predate the `Artifact`-based evidence model (§4.4). ShepherdAPI treats them as **read-only**: they may be returned by read endpoints for historical context, but no new ShepherdAPI operation ever writes to them. All new evidence goes through `Artifact` + `ScoreObservationEvidence`.

### 4.4 Evidence model

Evidence files are externally owned — ShepherdAPI never stores or transports file bytes. It records provenance and integrity metadata only, via the `Artifact` table, using SHA-256 as the evidence identity mechanism (enforced today by the DDL's hex-format and exact-length `CHECK` constraints on `Artifact.SHA256`).

Recording flow:
1. `POST` a new `Artifact` — registers evidence metadata (SHA-256, `OriginalURI`/`StoredURI`, size, MIME type) against a specific `Execution`.
2. `POST` a link via `ScoreObservationEvidence` — associates an existing `Artifact` with one or more `ScoreObservation`s (many-to-many), with optional notes on the link itself.

`Artifact` rows are immutable from creation (insert-only) — SHA-256 is the identity guarantee itself, so nothing about an `Artifact` row is ever corrected after it's written. A mistaken registration is superseded by registering a corrected `Artifact`, not edited.

## 5. Lifecycle and Immutability Model

This is the central Alpha invariant: historical experiment data must not be fabricated or mutated, while still allowing the normal human workflow of recording and correcting scores, notes, and evidence as an evaluation proceeds.

An `ExperimentVersion` moves through three states with respect to its data:

| State | Definition | What may change |
|---|---|---|
| **Design** | No `Execution` exists | The version's design (descriptive fields, Iterations, Parameters, ScoreRules) may be edited freely; Executions may be created. |
| **Live** | At least one `Execution` exists and `LockedUTC IS NULL` | The design is frozen (§5.4). Executions, observations, artifacts and evidence continue to be recorded and corrected (§5.1, §5.6). |
| **Final** | `LockedUTC IS NOT NULL` | Everything is frozen (§5.3) except `StatusID`. |

The transition Design → Live is the creation of the first `Execution`; it is permanent, because an `Execution` can never be deleted (§5.1). The transition Live → Final is finalization (§5.7). Both transitions are serialized against concurrent writers by the parent-version lock (§5.8).

All of these rules are enforced in the database — in controlled-write stored procedures and in guard triggers (§5.8) — not only in the API, so they hold for every writer that reaches the database. The API pre-checks only where a clean error or a 404 is needed.

### 5.1 `Execution` — creation, `EndUTC`, and deletion

**Creation guard.** An `Execution` can be created only through `dbo.usp_CreateExecution`, atomically with its checks and under the parent-version lock: the version must be unlocked (`version-locked` otherwise), and the Execution's phase must have a usable configuration — the version's ScoreRules for that phase must exist and total exactly `100.00` (`invalid-rule-configuration` otherwise, with the phase's rules described in the 409 body). `ScoreRule.Active` is ignored (§5.5). Nothing about the Execution's `StatusID` is checked.

**`EndUTC` — "the run finished".** Set by the machine/run process. Structural signal, backed by the existing `endutc_is_null_or_greaterequal_startutc` check constraint — no curation required. Once `EndUTC` is set, the execution's own run-facts become immutable through ShepherdAPI: `ModelUsed`, `InterfaceUsed`, `InterventionCount`, `OutcomeNotes`, `EndUTC` itself. The run-facts update is a controlled-write procedure (`usp_UpdateExecutionRunFacts`) that takes the parent-version lock, then checks the lock (`version-locked`) and `EndUTC` (`execution-run-facts-frozen`, in that order) against the state it reads under the lock, so two racing updates cannot both set `EndUTC`. The rule is independent of the `ExperimentVersion` lock (§5.3). It does *not* stop artifacts, observations or evidence from being recorded for that Execution while the version is not final. Finalization requires every Execution to have ended, unless its end time is unknown and the lock request explicitly accepts that with an explanation (V11, §5.7). Such an Execution's `EndUTC` stays NULL for good, and its `FinalizationException` row says why.

**Immutable from creation, regardless of `EndUTC`:** `Execution.IterationID`, `AgentID`, `PhaseID`, `AttemptNumber`, `StartUTC` (a database guard rejects any change, `identity-change-rejected`). These identify *which* execution this is; changing them after creation would fabricate a different historical record under the same ID rather than correct one.

**Never deleted.** A database trigger rejects every delete of an `Execution`, whatever the state of its version (`execution-delete-rejected`). Because the first Execution permanently freezes the design (§5.4), deleting it must not be a way back.

`Execution.StatusID` carries no architectural meaning in Alpha. It remains a required column (per the unmodified DDL) but no enforcement logic keys off its value.

### 5.2 `Complete` (status value) — "the human thinks the work is finished"

Informational only. Not an immutability boundary. Recording, correction, and late evaluation input may continue after an `ExperimentVersion` or its executions are marked `Complete`. Only `LockedUTC` (§5.3) freezes the record.

### 5.3 `ExperimentVersion.LockedUTC` — "the experimental record is final" (hard lock)

`LockedUTC` is a nullable `datetime2(3)` on `ExperimentVersion`. It is set exactly once, by finalization (§5.7), together with the score snapshot, in one transaction. The `ExperimentVersion` guard trigger enforces that: a version is never inserted locked, and `LockedUTC` goes from NULL to a value only while the finalization session-context key names that same version (`lock-requires-finalization` otherwise). This is an accident guard, not a security boundary (§7).

Semantics:
- `LockedUTC IS NULL`: the version and everything it governs may still be corrected per the rules in this section. This supports the normal workflow where evaluating a later execution, or receiving a delayed rating, legitimately causes correction of earlier evaluation data within the same `ExperimentVersion`.
- `LockedUTC IS NOT NULL`: the entire experimental record governed by that version is frozen — both mutation of existing data and creation/removal of child data are rejected (`version-locked`). Governed data is:
  - The `ExperimentVersion` row itself: `Name`, `VersionLabel`, `Description`, `EffectiveDateUTC`, `LockedUTC`.
  - All descendants: `Iteration`, `Execution`, `Parameter`, `ScoreRule`, `Artifact`, `ScoreObservation`, `ScoreObservationEvidence`, `ScoreObservationSnapshot`, `FinalizationException` (V11).
- `LockedUTC`, once set, cannot be cleared or changed.
- **Exceptions after lock** (and only these):
  - `ExperimentVersion.StatusID` may continue to change (e.g., to `Retired`) without altering or being blocked by `LockedUTC`. Retirement does not remove or alter an existing lock.
- `StatusID` plays no part in locking. The lock endpoint accepts an optional `StatusID` for compatibility and ignores it; a Locked/Retired/Complete status value never causes, and is never required by, finalization. `LockedUTC` is the durable fact of finalization.

Detecting writes that bypass ShepherdAPI after the fact (audit/tamper detection) is not attempted — no existing timestamp column reliably represents "when was this row written", since all existing timestamps are domain/business-event times (e.g. `ObservedUTC`). What the database does provide is *prevention*: the guard triggers (§5.8) reject governed writes from any connection, subject to the caveat on permissions in §7.

`Status` reference data (`Draft`, `Locked`, `Retired`, `Complete`, or others) is adjustable Alpha reference data, not a fixed architectural constraint.

### 5.4 Design freeze — "the first Execution freezes the design"

Once **any** `Execution` exists for an `ExperimentVersion`, the following are rejected with `design-frozen` (HTTP 409, distinct from `version-locked`):

- create, update and delete of its `Iteration`s, `Parameter`s and `ScoreRule`s, and
- updates to the version's `Name`, `VersionLabel`, `EffectiveDateUTC` and `Description`.

The freeze is permanent and independent of the Execution's phase, completion state (`EndUTC`), or whether observations exist. `StatusID`, Executions, Artifacts, ScoreObservations and evidence links are unaffected. Design writes go through controlled-write procedures (`usp_UpdateExperimentVersion`, `usp_Create/Update/DeleteIteration`, `usp_Create/Update/DeleteParameter`, `usp_Create/Update/DeleteScoreRule`) that take the parent-version lock before checking; the guard triggers repeat the check for writes that bypass them. Because Execution creation and design writes serialize on the same lock, a design write either commits before the first Execution or is rejected, and the first Execution always sees committed configuration. A row cannot be moved to another version to evade the freeze (identity/ownership columns are immutable, §5.8).

Check order for a design write: locked (`version-locked`) before frozen (`design-frozen`).

### 5.5 `ScoreRule` lifecycle — create, update, physical delete (Issue #7)

A `ScoreRule` follows exactly the rules of the other design tables (§5.4), with no exception. It is created (`POST /api/score-rules`), updated (`PUT /api/score-rules/{id}`) and physically deleted (`DELETE /api/score-rules/{id}`, HTTP 204) only while its version is **unlocked and has no Execution**: otherwise each write is `version-locked` (checked first) or `design-frozen`. `StatusID` never authorizes or blocks a rule write. The procedures `usp_CreateScoreRule`, `usp_UpdateScoreRule` and `usp_DeleteScoreRule` follow the parent-first lock protocol (§5.8), and `trg_ScoreRule_Guard` applies the same checks to any write that bypasses them, including a version locked without ever having had an Execution.

- **Update** replaces `WeightPercent`, `MinScore`, `MaxScore`, `Active` and `RuleNotes` (`MinScore < MaxScore` is a `400`). `ExperimentVersionID`, `PhaseID` and `ScoreCategoryID` are the rule's identity and ownership and never change (`identity-change-rejected`); to change them, delete the rule and create another.
- **Delete** removes the row. A deleted rule no longer exists, so it no longer occupies its `(ExperimentVersionID, PhaseID, ScoreCategoryID)` combination, a second delete is `404`, and an observation naming it is refused by the foreign key (`constraint-violation`).
- **Historical integrity.** Every rule that exists is usable, and once the first Execution exists the version's rules can no longer change in any way, so every rule a `ScoreObservation` or `ScoreObservationSnapshot` refers to is the rule as it was used. The foreign keys from both tables additionally refuse the delete of any referenced rule, whatever the version state.
- `ScoreRule.Active` is retained but ignored everywhere (eligibility, Execution creation, observations, finalization). Listings return every rule of the version.

Before Issue #7 the schema carried `ScoreRule.RowState` and rules were soft-deleted, including after finalization. The v9 baseline drops the column; the deployment scripts drop `usp_SoftDeleteScoreRule` where it was deployed and no longer reference `RowState`. Its error numbers 50301, 50401 and 50402 are retired and not reused.

### 5.6 `ScoreObservation` write validation

Observations are created and their content corrected only through `dbo.usp_CreateScoreObservation` and `dbo.usp_UpdateScoreObservationContent`, atomically with their checks and under the parent-version lock. Creation runs these checks in order; a content update runs only checks 1 and 3 (against the observation's existing rule):

1. The Execution (or observation) must exist (404) and its version must be unlocked (`version-locked`).
2. The rule must belong to the Execution's version and phase (`version-phase-mismatch`). A rule that does not exist is refused by the foreign key (`constraint-violation`).
3. `ObservedScore` must lie within the rule's `MinScore`..`MaxScore`, inclusive (`out-of-range-score`, HTTP 400).
4. A second observation for the same Execution, rule and rater is `duplicate-observation`.

Work in progress never blocks a valid observation: an Execution without `EndUTC`, or a phase with no other observations, is fine. `ExecutionID`, `ScoreRuleID` and `RaterID` of an observation are immutable; only the score, `ObservedUTC` and notes can be corrected, until the version is final.

### 5.7 Finalization and the score snapshot

`POST /api/experiment-versions/{id}/lock` finalizes the version by calling `dbo.usp_FinalizeExperimentVersion`, a repository-managed stored procedure that owns its transaction (`XACT_ABORT ON`, structured `TRY/CATCH`): any failure leaves no change from that attempt. Sequence:

1. Acquire the **exclusive** parent-version lock (waits for in-flight writers, blocks new ones).
2. The version must exist (404) and be unlocked (`version-locked`; finalizing twice is a 409).
3. No `ScoreObservationSnapshot` or `FinalizationException` rows may already exist for it (`finalize-snapshot-rows-exist`).
4. **Completeness.** The *configured phases* are the distinct `PhaseID`s of the version's ScoreRules. The version must have Executions (`finalize-no-executions`). (V11) Every acknowledged unknown end in the request must name an Execution of this version that has no `EndUTC`, with a non-blank explanation (`finalize-unknown-end-invalid`). Every Execution must have `EndUTC` or be acknowledged (`finalize-execution-not-ended`), and at least one observation (`finalize-execution-without-observation`; an acknowledgment never bypasses this); every Execution's phase must be a configured phase (`finalize-execution-phase-not-configured`); every configured phase must have at least one observation (`finalize-phase-without-observation`); and every configured phase's rules must total exactly `100.00` (`finalize-phase-weight-invalid`).
5. **Observation integrity** is revalidated: each observation's rule belongs to the Execution's version and phase and the score is within range (`finalize-observation-integrity`).
6. One `ScoreObservationSnapshot` row is inserted per `ScoreObservation` — all raters, no calculation: `ObservedScore` from the observation; `MinScore`, `MaxScore`, `WeightPercent` from the rule; `HigherIsBetter` from the rule's `ScoreCategory`; keys from the Execution and rule. (V11) One `FinalizationException` row (`end-time-unknown`) is inserted per acknowledged unknown end, with its explanation stored as given.
7. `LockedUTC`, every snapshot row's `SnapshotUTC` and every exception row's `RecordedUTC` are set to one database-generated UTC timestamp.
8. Commit.

**Session-context token.** The procedure sets the key `ShepherdAPI.FinalizingVersionID` to the version's id before the snapshot inserts and keeps it set through the `LockedUTC` update, because the guard triggers (snapshot, `FinalizationException` and `ExperimentVersion`) admit their writes only while it names that version. It clears the key right after the `LockedUTC` update, before `COMMIT`, and again in the `CATCH` block on every failure path, including a doomed transaction. The key is session state rather than transactional state, so the explicit clears matter; a pooled connection is also reset between uses.

Each failed check has its own error code, and its 409 body names the offending Executions, phases or observations (`executionIDs`, `phaseIDs`, `scoreObservationIDs`, `total`). The request's optional `StatusID` is ignored (§5.3).

**Snapshot table.** The six-column primary key (`ExperimentVersionID`, `ExecutionID`, `PhaseID`, `ScoreCategoryID`, `ScoreRuleID`, `ScoreObservationID`) is preserved. Rows are created only by finalization: the procedure sets the session-context key `ShepherdAPI.FinalizingVersionID` to the version's id around its inserts, and the insert trigger requires it (`snapshot-insert-not-permitted` otherwise). Inserts for a locked version are rejected, and updates and deletes are always rejected (`version-locked`). `CHECK` constraints (`MinScore < MaxScore`, `WeightPercent` 0–100, `ObservedScore` between `MinScore` and `MaxScore`) are added by the `database/upgrade/` script. The token prevents out-of-procedure writes by accident; it is not strong protection (§7).

**Finalization exceptions (V11).** An Execution whose end time cannot be established would otherwise block its version from ever being finalized. The lock request's optional `UnknownEnds` names each such Execution with a mandatory explanation (at most 2000 characters), and finalization records it as a `dbo.FinalizationException` row: version, Execution, `ExceptionCode` `'end-time-unknown'` (the only kind so far, enforced by a CHECK), the explanation (a CHECK refuses a blank one), and `RecordedUTC`. The primary key is (`ExecutionID`, `ExceptionCode`). The table follows the snapshot pattern exactly:
- **Writes:** rows are created only by finalization, in its transaction. The insert trigger requires the token for the row's version and an Execution of that version (`finalization-exception-insert-not-permitted` otherwise).
- **Immutability:** inserts for a locked version are rejected, and updates and deletes are always rejected (`version-locked`).
- **Reading:** `GET /api/experiment-versions/{id}/finalization-exceptions` returns them.

An acknowledgment is checked under the exclusive lock, so a run-facts update that sets `EndUTC` first makes it stale (`finalize-unknown-end-invalid`). After the lock, the Execution's run facts can never change, so its `EndUTC` stays NULL. A locked version can hold an Execution without `EndUTC` only through such a row; `maintenance/post-deploy-audit-v11.sql` checks that.

**Snapshot authority.** Once a version is finalized, its snapshot rows are the immutable, authoritative scoring definitions and observations for that version. The originating `ScoreRule` can no longer change (§5.5), and later changes to `ScoreCategory.HigherIsBetter` do not alter snapshot membership, the facts preserved in it, or their reproducibility. Historical analysis derives scoring facts from the snapshot, not from the current lifecycle state of the originating rules.

### 5.8 Parent-version lock protocol and database guards

Every governed table belongs, directly or through a parent, to exactly one `ExperimentVersion`. Concurrency and the rules above rest on one protocol:

- **Lock order.** The `ExperimentVersion` row is locked first and held to the end of the transaction. Multi-version statements lock in ascending id order, so they cannot deadlock one another.
- **Controlled writers take an UPDATE lock** (`UPDLOCK, HOLDLOCK, ROWLOCK`), so writers to one version serialize. **Finalization takes an EXCLUSIVE lock**, so it waits for in-flight writers and blocks new ones. Explicit hints keep this independent of `READ_COMMITTED_SNAPSHOT`.
- **Controlled-write procedures** (`usp_CreateExecution`, `usp_UpdateExecutionRunFacts`, `usp_CreateArtifact`, `usp_AddEvidence`, `usp_RemoveEvidence`, `usp_Create/UpdateScoreObservation…`, the design-write procedures of §5.4, `usp_FinalizeExperimentVersion`) lock first, then validate and write in one transaction with `SET XACT_ABORT ON`. Every write the API makes to a governed table goes through one, so none takes a child lock before the parent lock (the order a plain statement followed by its `AFTER` trigger would have). "Not found" is reported through an output flag so the API can return its existing 404. An evidence link spans an observation and an artifact, so `usp_AddEvidence` and `usp_RemoveEvidence` lock both owning versions, in ascending id order.
- **Standalone calls.** A controlled-write procedure owns the transaction it begins and rejects a caller's transaction with `THROW 50000` (a 500 `internal-error`: a programming error, not a client condition) as its very first statement, before `SET XACT_ABORT ON`, so the caller's transaction is left intact and still committable. A "not found" branch rolls back only the procedure's own empty transaction. The lock primitives and assertion helpers (`usp_AcquireVersionLock`, `usp_LockVersions`, `usp_AssertVersionsUnlocked`, `usp_AssertDesignWritable`, `usp_AssertDesignNotFrozen`) are called inside those transactions and are exempt.
- **Guard triggers** on every governed table are the defensive layer for writes that bypass the procedures. They run after the statement, take the same lock, and reject the whole statement (and its transaction) if any affected version is locked or — for design tables — frozen. Owners are resolved set-wise, so multirow statements spanning several versions are all-or-nothing. A trigger-detected conflict may deadlock and is then retryable (below).
- **Identity and ownership immutability.** Primary keys and ownership/identity columns cannot change (`identity-change-rejected`), so a row cannot be moved between versions. This is checked before the lock check. The identity columns are listed with each table's guard in [database/programmability/](../database/programmability/).
- **Bounded waits and retry.** Lock waits are bounded by `SET LOCK_TIMEOUT` (default 10 s, configuration `Database:LockTimeoutMs`). A lock-wait timeout (1222) or deadlock victim (1205) is surfaced as one retryable 409 (`retryable-conflict`, `retryable: true`); nothing is committed, so the client can retry. The run-facts, artifact and evidence writes additionally retry a deadlock victim (1205) exactly once, transparently, in the repository: one procedure call on a new connection with no caller transaction, re-run from the start so it re-validates current state under the lock. A lock timeout is never retried transparently, and finalization stays client-retryable.
- The API connects as a single principal, so these guards are integrity rules, not an authorization boundary (§7).

## 6. API Design Approach

### 6.1 Technology and code layout

ASP.NET Core Web API (.NET 8 LTS), Swashbuckle for OpenAPI/Swagger UI, Dapper for data access. The DDL is fixed and authoritative rather than evolved code-first, so an ORM's migration/change-tracking machinery adds abstraction without benefit; Dapper gives explicit, traceable SQL against a schema ShepherdAPI doesn't own the shape of.

Integrity rules that must hold under concurrency live in T-SQL objects checked into the repository — stored procedures, triggers, views and a table type — invoked through Dapper. Services shape rows and resolve 404s; they do not decide lock or lifecycle questions from stale reads.

Scripts are laid out under [database/](../database/README.md): the full-snapshot baseline (`Create_Experiments_Database_Version_10.sql`, for creating a database from scratch; it includes the `upgrade/` and `programmability/` scripts as of Issue #10, and the earlier `Create_Experiments_Database_And_Tables-N.sql` snapshots are kept for history), then `upgrade/` (schema changes after the baseline), then `programmability/` (procedures, views, triggers; numbered, applied in filename order). Every script is re-runnable (`CREATE OR ALTER`, guarded DDL). Permissions are not scripted (§7).

### 6.2 Resource model

REST resources map to the entity model in §4.1, roughly one resource per table, with nesting where the relationship is natural (e.g., evidence links scoped under a `ScoreObservation`). Reference/lookup entities (`Agent`, `ArtifactType`, `Phase`, `Rater`, `ScoreCategory`, `Status`) get simple CRUD endpoints since all new persistent operations — including experiment setup — pass through ShepherdAPI (§2). `ScoreObservationSnapshot` has no write endpoint; it is produced only by finalization (§5.7). `FinalizationException` (V11) likewise has no write endpoint, only `GET /api/experiment-versions/{id}/finalization-exceptions`; its rows come from the lock request's `UnknownEnds` (§5.7). `ScoreRule` has create, list, get, update and delete, all limited to the Design state (§5.5).

### 6.3 Validation

API-layer validation mirrors the DDL's existing `CHECK` constraints (SHA-256 hex/length, `ByteLength >= 0` (enforced by the database too since V11; the V10 constraint's `datalength` form accepted any value), `AttemptNumber > 0`, `EndUTC >= StartUTC`, `InterventionCount >= 0`, `MinScore < MaxScore`, `WeightPercent` 0–100) to return clean `400` responses instead of surfacing raw SQL errors. The lifecycle, locking, freeze and observation/finalization rules of §5 are enforced in the database (§5.8) and reported through the error contract (§6.6). The database constraints and procedures remain the source of truth; API validation is a UX convenience layered on top, not a substitute.

### 6.4 Query shape

Simple, FK-scoped list queries (e.g. `GET /executions?iterationId=`) and detail-by-id endpoints. No pagination, sorting infrastructure, or aggregation/rollup/summary endpoints — the latter is dashboard/analytics territory, explicitly out of scope (§3).

### 6.5 Deletion

No `DELETE` endpoints are exposed for `Execution`, `ScoreObservation` or `Artifact`, consistent with the immutability model in §5 (and the database rejects Execution deletes outright). `Iteration`, `Parameter` and `ScoreRule` can be deleted only while the version is in the Design state (§5.4, §5.5); the delete is physical. `ScoreObservationEvidence` links may be added or removed while their governing `ExperimentVersion` is not final (correcting a mis-attached piece of evidence); once locked, removal is blocked like any other governed mutation.

### 6.6 Error contract

All errors are RFC 7807 `ProblemDetails` with a stable machine-readable `code` extension, defined in one catalog (`ErrorCatalog`). Errors raised by T-SQL use `THROW` numbers in reserved ranges, mapped to catalog entries by the exception middleware:

| Range | Area | Codes |
|---|---|---|
| 50100–50199 | shared / lock protocol | `version-locked` (50100), `identity-change-rejected` (50101) |
| 50200–50299 | Execution guard | `invalid-rule-configuration` (50200), `execution-run-facts-frozen` (50201) |
| 50300–50399 | observation validation | `version-phase-mismatch` (50300), `out-of-range-score` (50302, HTTP 400); 50301 is retired (§5.5) |
| 50400–50499 | design freeze | `design-frozen` (50400), `execution-delete-rejected` (50403); 50401 and 50402 are retired (§5.5) |
| 50500–50599 | finalization | `finalize-no-executions` … `finalize-snapshot-rows-exist` (50500–50507), `snapshot-insert-not-permitted` (50508), `lock-requires-finalization` (50509), `finalize-unknown-end-invalid` (50510, V11), `finalization-exception-insert-not-permitted` (50511, V11) |

Application errors keep their existing codes (`not-found`, `record-locked`, `conflict`, `validation-failed`, `duplicate-key`, `constraint-violation`, `internal-error`); the duplicate observation constraint is recognized as `duplicate-observation`. Deadlock victims and lock-wait timeouts become the single retryable `retryable-conflict`. An unregistered `THROW` number is a 500 `internal-error`, which is how a procedure called inside a transaction (`THROW 50000`) is reported. Some errors carry extra members (for example the offending ids of a failed finalization check, or the phase's rules for `invalid-rule-configuration`). The catalog, the numbering and the OpenAPI `ProblemDetails` responses are the contract ShepherdWeb builds against; how ShepherdWeb presents them is its own concern.

### 6.7 Testing and deployment foundation

Integration tests run against a disposable database (`ShepherdAPI_IntegrationTest`) built from the current baseline plus `upgrade/` and `programmability/`, and dropped afterward; they never touch the real `Experiments` database. Unit tests cover the catalog, mapper, middleware and services without a database. A snapshot test compares the generated OpenAPI document with `contracts/openapi.json`. To deploy to an existing database, apply `upgrade/` then `programmability/` (see [database/README.md](../database/README.md)); the API build and those scripts must be deployed together, because the API now calls the procedures.

## 7. Deferred to a Later Version

The following were discussed during this review and deliberately not decided now, to avoid solving problems Alpha doesn't have:

- **Database permissions.** The API and tests connect with a high-privilege principal, so `DENY`-based protection is ineffective today and the guards are integrity rules, not security. The finalization session-context token (§5.7), which guards both snapshot inserts and the assignment of `LockedUTC`, prevents accidents only; strong protection of those writes, and of the controlled-write procedures against direct table access, depends on this work (a non-sysadmin API principal, a decision on which tables keep direct DML, and a column-level `DENY UPDATE (LockedUTC)`). It is tracked as GitHub Issue #11.
- A fixed, enforced `Status` vocabulary or per-entity scoping of valid status values.
- After-the-fact audit/tamper detection for writes that bypass ShepherdAPI.
- Row-level `CreatedUTC`/`ModifiedUTC` audit columns.
- Analytics, rollups and scoring calculation (including anything derived from the snapshot).
- Automated agent scoring and agent orchestration.

## 8. Implementation History

The lifecycle rules above were implemented, in dependency order, as GitHub Issues in `wbeglen/ShepherdAPI`:

| Issue | Delivers | Section |
|---|---|---|
| #5 | Shared error contract; test and deployment foundation | §6.6, §6.7 |
| #6 | Parent-version lock protocol; post-lock write guards; identity/ownership immutability | §5.3, §5.8 |
| #1 | Execution insert guard (rule configuration must total 100.00) | §5.1 |
| #3 | ScoreObservation write validation | §5.6 |
| #4 | ExperimentVersion design freeze; ScoreRule soft-delete (replaced by #7); Execution deletes rejected | §5.4, §5.5, §5.1 |
| #2 | Finalization and the score snapshot | §5.7 |
| #7 | `ScoreRule.RowState` removed; ScoreRule update and physical delete in the Design state only | §5.5 |
| #9 | `LockedUTC` assignable only by finalization (token guard on the `ExperimentVersion` trigger) | §5.3, §5.7 |
| #10 | Standalone transaction ownership for controlled-write procedures | §5.8 |
| #8 | Parent-first locking for run-facts, artifact and evidence writes; single deadlock retry | §5.1, §5.8 |

A decision to change any rule above should start from the corresponding issue.
