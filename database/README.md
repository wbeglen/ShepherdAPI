# Database scripts

## Layout and order

1. `Create_Experiments_Database_And_Tables-N.sql` — the baseline: a full snapshot that creates the `Experiments` database and all tables. The highest `N` is current (v9 today). It is for creating a database from scratch, never for applying to an existing one.
2. `upgrade/` — schema changes made after the current baseline (for example the `ScoreObservationSnapshot` CHECK constraints, which v9 does not yet include).
3. `programmability/` — stored procedures, views, functions and triggers.

Apply `upgrade/` first, then `programmability/`. Within each folder, files run in filename order, so use a numeric prefix (`010_...sql`, `020_...sql`). Files may contain `GO` batch separators.

Every script must be re-runnable: use `CREATE OR ALTER` for procedures, views, functions and triggers, and guard everything else with `IF NOT EXISTS` / `IF OBJECT_ID(...)` checks. Applying the whole set twice must succeed.

## Applying to a restored backup

Restore the backup, then run only the `upgrade/` and `programmability/` scripts against it (not the baseline). With Windows authentication against the local default instance:

```bash
for f in database/upgrade/*.sql database/programmability/*.sql; do
  sqlcmd -S localhost -E -d Experiments -b -I -i "$f" || break
done
```

`-b` makes `sqlcmd` stop with a non-zero exit code on the first error. `-I` turns QUOTED_IDENTIFIER on, which `sqlcmd` leaves off by default; the scripts also set it themselves. The glob expands in filename order within each folder, upgrade first.

## Tests

`DatabaseFixture` builds a throwaway database (`ShepherdAPI_IntegrationTest`) from the current baseline, applies `upgrade/` then `programmability/`, and drops it afterward. It never touches the real `Experiments` database.
