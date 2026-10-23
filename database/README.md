# Database scripts

## Layout and order

1. `Create_Experiments_Database_And_Tables-N.sql` — the baseline: a full snapshot that creates the `Experiments` database and all tables. The highest `N` is current (v9 today). It is for creating a database from scratch, never for applying to an existing one.
2. `upgrade/` — schema changes made after the current baseline (for example the `ScoreObservationSnapshot` CHECK constraints, which v9 does not yet include).
3. `programmability/` — stored procedures, views, functions and triggers.
4. `maintenance/` — read-only operational scripts, such as the pre-deployment audit. They are never part of a deployment.

Controlled-write procedures (`usp_Create…`, `usp_Update…`, `usp_Delete…`, `usp_AddEvidence`, `usp_RemoveEvidence`, `usp_FinalizeExperimentVersion`) must be called standalone, outside any transaction; they raise error 50000 otherwise. The lock primitives and assertion helpers are the exception.

Apply `upgrade/` first, then `programmability/`. Within each folder, files run in filename order, so use a numeric prefix (`010_...sql`, `020_...sql`). Files may contain `GO` batch separators.

Every script must be re-runnable: use `CREATE OR ALTER` for procedures, views, functions and triggers, and guard everything else with `IF NOT EXISTS` / `IF OBJECT_ID(...)` checks. Applying the whole set twice must succeed.

## Pre-deployment audit

Before you apply the scripts to a database that already holds data, stop ShepherdAPI and run `maintenance/pre-deploy-audit.sql` against that database. The script only reads. It checks the data assumptions behind Issues #7–#10:

- the v9 schema shape
- that a version is locked if and only if it has snapshot rows
- ScoreRule ranges, uniqueness and per-phase weights
- observation integrity
- open Executions
- cross-Execution evidence links
- other connections to the database

```bash
sqlcmd -S localhost -E -d Experiments -b -I -W -i database/maintenance/pre-deploy-audit.sql
```

In SSMS, open the file and run the whole script. It switches to `Experiments` itself.

The inventory (section 1) and the connection list (section 7) are informational. SSMS's own connections show up in section 7 and are harmless. Every other result set should be empty. Resolve any row before deploying.

An open Execution (section 5) can still be ended after deployment, but it blocks finalization until it is. Once the scripts are applied, a version that has Executions has a frozen design. Make any rule, parameter or iteration changes before you deploy.

## Applying to a restored backup

Restore the backup, then run the audit above. Then run only the `upgrade/` and `programmability/` scripts against it (not the baseline). With Windows authentication against the local default instance:

```bash
for f in database/upgrade/*.sql database/programmability/*.sql; do
  sqlcmd -S localhost -E -d Experiments -b -I -i "$f" || break
done
```

`-b` makes `sqlcmd` stop with a non-zero exit code on the first error. `-I` turns QUOTED_IDENTIFIER on, which `sqlcmd` leaves off by default; the scripts also set it themselves. The glob expands in filename order within each folder, upgrade first.

## Tests

`DatabaseFixture` builds a throwaway database (`ShepherdAPI_IntegrationTest`) from the current baseline, applies `upgrade/` then `programmability/`, and drops it afterward. It never touches the real `Experiments` database.
