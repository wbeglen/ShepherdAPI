# Database scripts

## Layout and order

1. The baseline: a full snapshot that creates the `Experiments` database, all tables, constraints, triggers, procedures, views and functions. The current baseline is `Create_Experiments_Database_Version_10.sql` (V10). It equals the v9 baseline plus every script in `upgrade/` and `programmability/` as of Issue #10. The earlier snapshots, `Create_Experiments_Database_And_Tables-6.sql` through `-9.sql`, are kept for history. A baseline is for creating a database from scratch, never for applying to an existing one.
2. `upgrade/` — schema changes made after a baseline. V10 already includes `001` (the `ScoreObservationSnapshot` CHECK constraints). The script stays because it brings a v9 database up to V10, and it changes nothing on a V10 database. `002` and `003` are the V11 schema changes, which no baseline includes yet (see "Upgrading V10 to V11").
3. `programmability/` — stored procedures, views, functions, triggers and table types.
4. `maintenance/` — read-only operational scripts, such as the pre-deployment audit. They are never part of a deployment.

Controlled-write procedures (`usp_Create…`, `usp_Update…`, `usp_Delete…`, `usp_AddEvidence`, `usp_RemoveEvidence`, `usp_FinalizeExperimentVersion`) must be called standalone, outside any transaction; they raise error 50000 otherwise. The lock primitives and assertion helpers are the exception.

Apply `upgrade/` first, then `programmability/`. Within each folder, files run in filename order, so use a numeric prefix (`010_...sql`, `020_...sql`). Files may contain `GO` batch separators.

Every script must be re-runnable: use `CREATE OR ALTER` for procedures, views, functions and triggers, and guard everything else with `IF NOT EXISTS` / `IF OBJECT_ID(...)` checks. Applying the whole set twice must succeed.

## Upgrading V10 to V11

V11 lets a version be finalized while an Execution's end time is unknown, but only when the lock request names that Execution with a mandatory explanation. Each such acknowledgment is recorded, in the finalization transaction, as an immutable `dbo.FinalizationException` row. It also corrects an Artifact constraint. The changes:

| Script | Change |
|---|---|
| `upgrade/002_finalization_exception.sql` | New table `dbo.FinalizationException` with its primary key, two foreign keys, two CHECKs and an index. No existing table changes. |
| `upgrade/003_artifact_bytelength_check.sql` | Drops `[artifact-bytelength-greater-equal-zero]` and recreates it, with the same name, as `CHECK ([ByteLength] >= 0)`. The V10 definition, `datalength([ByteLength]) >= 0`, was always true. It's added `WITH CHECK`, so existing rows are validated. |
| `programmability/010_owner_views.sql` | Adds `vOwnerFinalizationException`. |
| `programmability/092_trigger_finalization_exception.sql` | New guard: rows are written only by finalization, never for a locked version, and never updated or deleted. |
| `programmability/094_type_finalization_exception_list.sql` | New table type `dbo.FinalizationExceptionList`, the procedure's acknowledgment list. |
| `programmability/095_procedure_finalize_experiment_version.sql` | Optional `@UnknownEnds`. Without it, finalization behaves exactly as in V10. |

To upgrade:

1. Back up the V10 database.
2. Stop ShepherdAPI and run the pre-deployment audit (below). Section 8 must be empty.
3. Apply `upgrade/` then `programmability/`, with the loop in "Applying to a restored backup". Every script is re-runnable, so the V10 scripts in those folders change nothing.
4. Run `maintenance/post-deploy-audit-v11.sql` (below). Expect `DEPLOYMENT VERIFIED`.
5. Back up the V11 database.
6. Deploy the matching ShepherdAPI build: its lock endpoint and its new read endpoint need V11.

## Pre-deployment audit

Before you apply the scripts to a database that already holds data, stop ShepherdAPI and run `maintenance/pre-deploy-audit.sql` against that database. The script only reads. It checks the data assumptions behind Issues #7–#10, and (section 8) behind the V11 upgrade:

- the v9 schema shape
- that a version is locked if and only if it has snapshot rows
- ScoreRule ranges, uniqueness and per-phase weights
- observation integrity
- open Executions
- cross-Execution evidence links
- other connections to the database
- (V11) no Artifact with a negative `ByteLength`, which would make `upgrade/003` fail; and, where V11 is already applied, no `FinalizationException` row on an unlocked version

```bash
sqlcmd -S localhost -E -d Experiments -b -I -W -i database/maintenance/pre-deploy-audit.sql
```

In SSMS, open the file and run the whole script. It switches to `Experiments` itself.

The inventory (section 1) and the connection list (section 7) are informational. SSMS's own connections show up in section 7 and are harmless. Every other result set should be empty. Resolve any row before deploying.

An open Execution (section 5) can still be ended after deployment, but it blocks finalization until it is. Once the scripts are applied, a version that has Executions has a frozen design. Make any rule, parameter or iteration changes before you deploy.

## Post-deployment audit (V11)

After applying `upgrade/` and `programmability/` to a V10 database, run `maintenance/post-deploy-audit-v11.sql` against it. It does everything the V10 audit does (below), and also checks:

- **Deployment:** the `FinalizationException` table, its key, foreign keys and CHECKs, its guard trigger, owner view and table type, and the V11 finalization procedure (by fingerprint). It also checks that `[artifact-bytelength-greater-equal-zero]` has the corrected definition `([ByteLength]>=(0))` and is enabled and trusted.
- **Data intact:**
  - The row count of `FinalizationException` matches the expected value (0 right after the upgrade).
  - Every exception row belongs to a locked version, at its `LockedUTC`, for one of its Executions that has no `EndUTC`, with a valid code and a non-blank explanation.
  - A locked version holds an Execution without `EndUTC` only through such a row.
- **Information:** each accepted unknown end is listed with its explanation.

```bash
sqlcmd -S localhost -E -d Experiments -b -I -W -i database/maintenance/post-deploy-audit-v11.sql
```

Right after the upgrade, the expected result is the same as for V10: `DEPLOYMENT VERIFIED`, with one `WARN` for the open Execution 9. Under V11, Execution 9 can also be accepted at Lock with an explanation. The expected counts, now including `@ExpectedExceptionRows`, are declared at the top of the script. After a version is finalized, update them, or rely on the integrity checks alone.

## Post-deployment audit (V10)

`maintenance/post-deploy-audit-v10.sql` is kept as the record of the V10 deployment. After applying `upgrade/` and `programmability/` to a v9 database, run it against the same database. Here V10 means the v9 baseline plus every script in those two folders as of Issue #10, which is the schema `Create_Experiments_Database_Version_10.sql` creates. The audit only reads. It verifies the following:

- **Deployment.** Every expected view, function, table type, trigger, procedure and constraint exists. Each trigger is enabled, `AFTER INSERT, UPDATE, DELETE`, and on the right table. Every CHECK and FOREIGN KEY in `dbo` is enabled and trusted. Every module was created with `QUOTED_IDENTIFIER` and `ANSI_NULLS` on. The deployed code is the current version: the Issue #10 standalone guard is present, the Issue #8 and #9 fingerprints match, nothing still refers to `RowState`, and `usp_SoftDeleteScoreRule` is gone.
- **Data intact.** The row counts match the pre-deployment audit, and the known open Execution 9 is still present. The integrity checks from the pre-deployment audit still pass.
- **Finalization readiness.** Unlocked versions that have Executions are checked against `usp_FinalizeExperimentVersion`'s rules: 50501 through 50505.

```bash
sqlcmd -S localhost -E -d Experiments -b -I -W -i database/maintenance/post-deploy-audit-v10.sql
```

In SSMS, open the file and run the whole script. It switches to `Experiments` itself.

The audit returns three result sets: an inventory per version, the findings, and a one-row verdict. Each finding has a severity:

| Severity | Meaning | Action |
|---|---|---|
| `FAIL` | Deployment failure: something is missing, disabled or out of date, or the data no longer matches the pre-deployment audit | Fix before using the API. The script ends with error 16, so `sqlcmd -b` exits non-zero. |
| `WARN` | Finalization-readiness warning: the deployment is fine, but finalization would refuse the version. Detail names the error code. | Complete the data before finalizing. |
| `INFO` | For information only, such as a `dbo` module the V10 scripts don't create | None |

Right after deployment, the expected result is `DEPLOYMENT VERIFIED` with one `WARN`: Execution 9 is still open. End it through `usp_UpdateExecutionRunFacts` before finalizing version 1.

The expected row counts and the known open Execution are declared at the top of the script. They come from the pre-deployment audit of 2026-10-23. Update them if the data has since been changed on purpose.

## Applying to a restored backup

Restore the backup, then run the audit above. Then run only the `upgrade/` and `programmability/` scripts against it (not the baseline). With Windows authentication against the local default instance:

```bash
for f in database/upgrade/*.sql database/programmability/*.sql; do
  sqlcmd -S localhost -E -d Experiments -b -I -i "$f" || break
done
```

`-b` makes `sqlcmd` stop with a non-zero exit code on the first error. `-I` turns QUOTED_IDENTIFIER on, which `sqlcmd` leaves off by default; the scripts also set it themselves. The glob expands in filename order within each folder, upgrade first.

## Tests

`DatabaseFixture` builds a throwaway database (`ShepherdAPI_IntegrationTest`) from the current baseline (`RepoPaths.BaselineDdlFileName`, now V10), applies `upgrade/` then `programmability/`, and drops it afterward. It never touches the real `Experiments` database. The baseline already contains the V10 scripts, so applying them again also proves they are re-runnable. A script added after V10, such as the V11 scripts, is applied on top, the same way it would be in a deployment.
