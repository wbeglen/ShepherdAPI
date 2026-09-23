# ShepherdAPI Architecture — Alpha Version

Status: Approved architecture, ready for implementation planning.
Scope: Alpha version only. This document does not describe a target end-state; it describes what Alpha is authorized to build.

## 1. Purpose

Good Shepherd is an experimental system for measuring agentic software development performance. ShepherdAPI is its durable API boundary.

The Alpha version supports rapid, accurate human recording and retrieval of experiment scores, notes, and testing evidence metadata. It serves one client (a separate effort, out of scope here) in this version, but the API is designed so it is not unnecessarily hard to add further clients — including automated agents — later, without building any distributed-system or scaling infrastructure now.

ShepherdAPI is a private, low-volume system for Alpha. The architecture optimizes for correctness, development speed, maintainability, traceability, and straightforward evolution — not throughput or scale.

## 2. System Boundary

- ShepherdAPI is the sole system boundary. Clients never access the `Experiments` database directly.
- The existing `Experiments` database ([database/Create_Experiments_Database_And_Tables-6.sql](../database/Create_Experiments_Database_And_Tables-6.sql)) is authoritative. Its DDL is not redesigned; the one addition made during this review (`ExperimentVersion.LockedUTC`, see §5) was an explicit, authorized, additive change, not a redesign.
- All new persistent operations pass through ShepherdAPI.
- `Execution.ExecutionID` is `BIGINT`.
- UTC is used for all persisted timestamps.
- ShepherdAPI exposes an OpenAPI contract with Swagger/interactive documentation enabled for Alpha development and manual testing. Clients (ShepherdWeb and future clients) generate typed clients from that contract rather than duplicating API models by hand.

## 3. Out of Scope (Alpha)

Authentication/authorization, community submissions/challenges, multi-tenancy, a public-facing API, billing, analytics, automated agent scoring, agent orchestration, dashboards, cloud artifact storage, production deployment mechanisms, and mutation of completed historical experiment data (see §5 for what "completed" means).

## 4. Data Model

The data model is the existing `Experiments` database DDL, unmodified except for the addition described in §5.3. ShepherdAPI does not introduce parallel or duplicate persistence — it is a thin, explicit boundary over this schema.

### 4.1 Entity overview

```
ExperimentVersion
 ├─ Iteration
 │   └─ Execution
 │       ├─ Artifact
 │       └─ ScoreObservation
 │           └─ ScoreObservationEvidence ──> Artifact
 ├─ Parameter
 └─ ScoreRule ──> Phase, ScoreCategory

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

Three independent signals govern this, each answering a different question, each backed by an existing or newly-added column — no additional schema and no invented status semantics beyond what's described here.

### 5.1 `Execution.EndUTC` — "the run finished"

Set by the machine/run process. Structural signal, already backed by the existing `endutc_is_null_or_greaterequal_startutc` check constraint — no curation required.

Once `EndUTC` is set, the execution's own run-facts become immutable through ShepherdAPI: `ModelUsed`, `InterfaceUsed`, `InterventionCount`, `OutcomeNotes`, `EndUTC` itself. This is independent of, and unaffected by, the `ExperimentVersion` lock described in §5.3.

**Immutable from creation, regardless of `EndUTC`:** `Execution.IterationID`, `AgentID`, `PhaseID`, `AttemptNumber`, `StartUTC`. These identify *which* execution this is; changing them after creation would fabricate a different historical record under the same ID rather than correct one.

`Execution.StatusID` carries no architectural meaning in Alpha. It remains a required column (per the unmodified DDL) but no ShepherdAPI enforcement logic keys off its value.

### 5.2 `Complete` (status value) — "the human thinks the work is finished"

Informational only. Not an immutability boundary. Recording, correction, and late evaluation input may continue after an `ExperimentVersion` or its executions are marked `Complete`.

### 5.3 `ExperimentVersion.LockedUTC` — "the human declares the experimental record final" (hard lock)

Added to the DDL in this review (`[LockedUTC] [datetime2](3) NULL` on `ExperimentVersion`, following the existing column conventions — nullable `datetime2(3)`, same pattern as `Artifact.StoredUTC`).

Semantics:
- `LockedUTC IS NULL`: the `ExperimentVersion` and everything it governs may still be corrected per the rules above. This supports the normal workflow where evaluating a later execution, or receiving a delayed rating, legitimately causes correction of earlier evaluation data within the same `ExperimentVersion`.
- `LockedUTC IS NOT NULL`: the entire experimental record governed by that `ExperimentVersion` is frozen through normal ShepherdAPI operations — both mutation of existing data and creation/removal of new child data are rejected. Governed data is:
  - The `ExperimentVersion` row itself: `Name`, `VersionLabel`, `Description`, `EffectiveDateUTC`, `LockedUTC`.
  - All descendants: `Iteration`, `Execution` (including executions still open, i.e. `EndUTC IS NULL`, at the moment of locking — the `ExperimentVersion` lock is a superset freeze), `Parameter`, `ScoreRule`, `Artifact`, `ScoreObservation`, `ScoreObservationEvidence`.
- `LockedUTC`, once set, cannot be cleared or changed through normal ShepherdAPI operations.
- **`ExperimentVersion.StatusID` is the sole post-lock exception** — it may continue to change (e.g., to `Retired`) without altering or being blocked by `LockedUTC`. Retirement does not remove or alter an existing lock.
- If a `Locked` status value is retained in the `Status` reference data, transitioning `StatusID` to it may be the single human action that atomically sets `LockedUTC` — one API operation, not two independently-driven writes that could disagree. `LockedUTC`, not the `StatusID` value, is the durable fact of finalization.

Enforcement is entirely at the ShepherdAPI layer (deterministic rejection of writes once locked). Detecting writes that bypass ShepherdAPI (direct database access) is a separate concern, explicitly out of scope for this invariant — no existing timestamp column reliably represents "when was this row written" (all existing timestamps are domain/business-event times, e.g. `ObservedUTC`, that legitimately differ from write time), so timestamp-based post-hoc tamper detection isn't attempted here.

`Status` reference data (`Draft`, `Locked`, `Retired`, `Complete`, or others) is adjustable Alpha reference data, not a fixed architectural constraint — ShepherdAPI does not hardcode assumptions about which named values exist beyond the `LockedUTC`-setting transition described above.

## 6. API Design Approach

### 6.1 Technology

ASP.NET Core Web API (.NET 8 LTS), Swashbuckle for OpenAPI/Swagger UI, Dapper for data access. The DDL is fixed and authoritative rather than evolved code-first, so an ORM's migration/change-tracking machinery adds abstraction without benefit; Dapper gives explicit, traceable SQL against a schema ShepherdAPI doesn't own the shape of.

### 6.2 Resource model

REST resources map to the entity model in §4.1, roughly one resource per table, with nesting where the relationship is natural (e.g., evidence links scoped under a `ScoreObservation`). Reference/lookup entities (`Agent`, `ArtifactType`, `Phase`, `Rater`, `ScoreCategory`, `Status`) get simple CRUD endpoints since all new persistent operations — including experiment setup — pass through ShepherdAPI (§2).

### 6.3 Validation

API-layer validation mirrors the DDL's existing `CHECK` constraints (SHA-256 hex/length, `ByteLength >= 0`, `AttemptNumber > 0`, `EndUTC >= StartUTC`, `InterventionCount >= 0`, `MinScore < MaxScore`, `WeightPercent` 0–100) to return clean `400` responses instead of surfacing raw SQL errors, plus the lifecycle rules in §5 (reject writes to locked or identity-immutable fields). The database constraints remain the source of truth; API validation is a UX convenience layered on top, not a substitute.

### 6.4 Query shape

Simple, FK-scoped list queries (e.g. `GET /executions?iterationId=`) and detail-by-id endpoints. No pagination, sorting infrastructure, or aggregation/rollup/summary endpoints — the latter is dashboard/analytics territory, explicitly out of scope (§3).

### 6.5 Deletion

No `DELETE` endpoints are exposed for `Execution`, `ScoreObservation`, or `Artifact`, consistent with the immutability model in §5. `ScoreObservationEvidence` links may be added or removed while their governing `ExperimentVersion` is unlocked (correcting a mis-attached piece of evidence); once locked, removal is blocked like any other governed mutation.

## 7. Deferred to a Later Version

The following were discussed during this review and deliberately not decided now, to avoid solving problems Alpha doesn't have:

- A fixed, enforced `Status` vocabulary or per-entity scoping of valid status values.
- Database-level integrity/auditing to detect writes that bypass ShepherdAPI.
- Any precondition on locking an `ExperimentVersion` (e.g., requiring all child executions to have `EndUTC` set first) — Alpha trusts the human to lock when appropriate.
- Row-level `CreatedUTC`/`ModifiedUTC` audit columns.
- Automated agent scoring and agent orchestration.
