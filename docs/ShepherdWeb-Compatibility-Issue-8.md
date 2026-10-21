# ShepherdWeb compatibility: Issues #8, #9 and #10

These changes move the run-facts, artifact and evidence writes into controlled-write procedures, restrict who can set
`LockedUTC`, and make the procedures standalone. ShepherdWeb is not changed by the issues. This note lists what ShepherdWeb
needs to change or recheck.

## Error responses that change (Issue #8)

| Operation | Before | After |
|---|---|---|
| `PUT /api/executions/{id}/run-facts` on a locked version | `409` `record-locked` | `409` `version-locked` |
| `PUT /api/executions/{id}/run-facts` once `EndUTC` is set | `409` `record-locked` | `409` `execution-run-facts-frozen` (new) |
| `POST /api/artifacts` on a locked version | `409` `record-locked` | `409` `version-locked` |
| `POST /api/score-observations/{id}/evidence` on a locked version | `409` `record-locked` | `409` `version-locked` |
| `DELETE /api/score-observations/{id}/evidence/{artifactId}` on a locked version | `409` `record-locked` | `409` `version-locked` |

- When the version is locked **and** the Execution has ended (always true after finalization), run-facts reports
  `version-locked`: the lock error takes precedence.
- `record-locked` is no longer produced by these operations. Any ShepherdWeb handling keyed on `record-locked` for them should
  be moved to `version-locked`, and `execution-run-facts-frozen` added where the UI explains why an ended Execution cannot be
  edited.
- `DELETE .../evidence/{artifactId}` for a link that does not exist is `404` even on a locked version (previously the lock
  error came first).

## Retry behavior (Issue #8)

The four operations now retry once by themselves after a SQL Server deadlock. A request that still fails with a deadlock or a
lock timeout returns the existing `409` `retryable-conflict` with `retryable: true`; ShepherdWeb should keep offering a retry.
Finalization (`POST .../lock`) is not retried by the API and stays client-retryable.

## Contract changes

`contracts/openapi.json` now documents these operations with their real success codes (`201` for the artifact and evidence
creates, `200` for run-facts, `204` for the evidence delete) and with `404` and `409` `ProblemDetails` responses. The success
codes were already what the API returned; the R8 workaround in ShepherdWeb's `GeneratedClientCustomization.cs` (201 for creates,
204 for deletes) still applies until the pinned contract is recaptured. Recapture it from the running API.

## Issue #9: `lock-requires-finalization` (new, `409`)

Only finalization can set `LockedUTC`. The API has no other path to it, so ShepherdWeb should never see this error; it is
raised for direct database writes. No ShepherdWeb change is required beyond treating an unknown `code` as a generic conflict.

## Issue #10: standalone procedures

The procedures reject being called inside a transaction with a `500` `internal-error`. Only a developer calling them from SQL
can hit this; the API never does. No ShepherdWeb change.
