# ShepherdWeb compatibility: Issue #7 (ScoreRule.RowState removed)

Issue #7 removes `ScoreRule.RowState`, replaces the ScoreRule soft-delete with a physical delete, and adds a ScoreRule update. ShepherdWeb is not changed by the issue. This note lists what ShepherdWeb needs to change to match. File references are to the ShepherdWeb repository.

## Contract changes

| Change | Before | After |
|---|---|---|
| `ScoreRule` schema | Has `rowState` (`"A"` or `"D"`) | No `rowState` member |
| `GET /api/score-rules?experimentVersionId=` | Rules with `RowState = 'A'` only | Every rule of the version (a deleted rule no longer exists) |
| `GET /api/score-rules/{id}` | Resolves a retired rule | `404` for a deleted rule. A rule an observation refers to can never be deleted, so it always resolves |
| `PUT /api/score-rules/{id}` | Did not exist | New. Body `UpdateScoreRuleRequest`: `weightPercent`, `minScore`, `maxScore`, `active`, `ruleNotes`. Returns `200` with the rule. `400` for `minScore >= maxScore`, `404`, `409` `version-locked` / `design-frozen` |
| `DELETE /api/score-rules/{id}` | Soft-delete; allowed with no Executions **or after lock**; second delete `409` `score-rule-already-deleted` | Physical delete (`204`); allowed only while the version is unlocked and has no Execution; otherwise `409` `version-locked` / `design-frozen`; second delete `404` |
| Uniqueness of `(version, phase, category)` | A retired rule kept its combination | A deleted rule frees its combination; a new rule may reuse it |
| Error codes | `unavailable-rule` (50301), `score-rule-already-deleted` (50401), `score-rule-invalid-rowstate` (50402) | Retired. An observation naming a missing rule is `409` `constraint-violation` |

ShepherdWeb pins its contract in `contracts/shepherdapi.openapi.json` (see `contracts/SOURCE.md`, currently commit `dce802f`). Recapture it from the running API after Issue #7. The R8 workaround in `GeneratedClientCustomization.cs` (201 for creates, 204 for deletes) still applies; `PUT` returns `200`, which the generated client already expects.

## Code changes

- `Features/Executions/ExecutionRules.cs:70` — `RulesFor` filters `r.RowState != "D"`. Remove the filter; every listed rule is usable.
- `Features/Scoring/ExecutionScoring.razor:158–161, 272` and `Features/Scoring/VersionObservations.razor:126–128, 153` — the fallback that fetches rules missing from the version list ("retired since it was used") and the `" (retired rule)"` label. Remove both: an observed rule is always in the version list. Keep the `Rule #{id}` fallback only as a defensive label.
- `Features/ScoreRules/ScoreRuleTable.razor` — the **Retire** action, its confirmation text ("Rules cannot be edited… only be retired… ShepherdAPI keeps retired rules"), and the comments and 409 message about pairs taken by retired rules (lines 1, 37, 99–116, 148–149, 189–193, 208–214). Replace with:
  - **Delete**, with a confirmation that says the rule is removed and its phase and category become free again;
  - **Edit**, for weight, min/max, Active and notes, using `PUT /api/score-rules/{id}` (phase and category stay read-only; changing them is delete and re-create);
  - the duplicate-pair check can rely on the list alone, because the list now shows every rule that occupies a pair.
  Both actions are shown only while the version is Draft with no executions and not locked, as the configuration tabs already do.
- `Features/ScoreRules/ScoreRuleForm.cs` — reuse its validation (min < max, weight 0–100) for the edit form.
- Error mapping — drop any handling of `unavailable-rule`, `score-rule-already-deleted` and `score-rule-invalid-rowstate`.

## Tests

- `tests/ShepherdWeb.Tests/Features/ExecutionLogicTests.cs:101–107` — `RulesFor_UsesTheExecutionsPhase_AndSkipsRetiredRules` uses `RowState`; keep the phase case only.
- `tests/ShepherdWeb.Tests/TestSupport/AppTestContext.cs:180` — the canned score-rule JSON includes `"rowState":"A"`; remove it.
- `tests/ShepherdWeb.Tests/Features/ConfigurationComponentTests.cs:142–151` — asserts a locked version offers no **Retire**; rename to Delete/Edit and also cover a version that has executions.

## Documentation

ShepherdWeb `docs/ARCHITECTURE.md` describes the old lifecycle in F2, F3 (the `RowState='A'` check), §7.4, AD-7's retire confirmation, the "Retired ScoreRule (`RowState='D'`)" state row, the score-rules page description, old R2, and owner decision B4 ("Retire with a stated consequence; nothing else"). Issue #7 answers B4's open question: score-rule mistakes are corrected before the first execution by editing or deleting the rule.
