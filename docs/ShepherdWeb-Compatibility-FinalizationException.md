# ShepherdWeb compatibility: finalization with an unknown Execution end (V11)

V11 lets a version be finalized while an Execution's end time is unknown, but only when the lock request names that Execution with a mandatory explanation. Each acknowledgment becomes an immutable `FinalizationException` row, committed together with the snapshot and `LockedUTC`. Existing clients are unaffected: a lock request without `unknownEnds` behaves exactly as before. This note lists what ShepherdWeb needs in order to use the feature.

## Contract changes

| Change | Detail |
|---|---|
| `LockExperimentVersionRequest.unknownEnds` | Optional array of `UnknownEndAcknowledgment` `{ executionID: long, explanation: string }`. Explanations are required, not blank, at most 2000 characters, and stored exactly as given. Each Execution may appear only once. |
| `POST /api/experiment-versions/{id}/lock` | Now also declares `400` (`ValidationProblemDetails`) for a malformed list: an ID of 0 or less, a blank or too-long explanation, or a duplicate ID. |
| New `409` `finalize-unknown-end-invalid` | An acknowledged Execution isn't this version's, doesn't exist, or already has an `EndUTC`. The body names them in `executionIDs`, with `total`. Nothing changes. |
| `409` `finalize-execution-not-ended` | Now means an open Execution that wasn't acknowledged. The body still names the Executions. |
| New `GET /api/experiment-versions/{id}/finalization-exceptions` | Returns `FinalizationException[]`: `{ experimentVersionID, executionID, exceptionCode ("end-time-unknown"), explanation, recordedUTC }`. The list is empty for a version with no exceptions, including every unlocked version, and the call returns `404` for a missing version. `recordedUTC` equals the version's `LockedUTC`. |
| New `409` `finalization-exception-insert-not-permitted` | Raised only for direct database writes; ShepherdWeb should never see it. |

Recapture the pinned contract from ShepherdAPI's committed `contracts/openapi.json`. The new GET is on `ExperimentVersionsClient`, which keeps its R8 mapping; nothing changes for R8.

## What ShepherdWeb should change

- **Lock dialog** (`VersionOverview.razor`): while any Execution is open, the dialog lists each open Execution, showing its ID, iteration, agent, phase and start time. Each gets:
  - a checkbox: "End time is unknown; finalize without it";
  - a required explanation (up to 2000 characters).

  **Lock permanently** is enabled only when every open Execution is ticked and explained. With no open Executions, nothing changes. The dialog must state the consequence: the end time can never be recorded afterwards. The request sends `unknownEnds`.
- **Errors:** `finalize-unknown-end-invalid` is a conflict with ShepherdAPI's title and detail. Code-based mapping already shows it.
- **Display:** on a locked version, an Execution with no `EndUTC` is **"End time unknown"**, with its explanation from the new endpoint, not "in progress". This affects the Execution page, the Overview's count, and the version's Executions table and matrix.
- **Unchanged:** the status sync after Lock (owner decision D1).
