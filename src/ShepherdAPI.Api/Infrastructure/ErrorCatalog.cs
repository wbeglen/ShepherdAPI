using System.Net;

namespace ShepherdAPI.Api.Infrastructure;

/// <summary>
/// One entry in the shared error catalog: a stable machine-readable <see cref="Code"/>, the HTTP
/// status and title it maps to, and (for errors raised by T-SQL) the THROW number reserved for it.
/// </summary>
public sealed record ErrorEntry(string Code, HttpStatusCode Status, string Title, int? SqlNumber = null, bool Retryable = false);

/// <summary>
/// The single place error codes, statuses and THROW numbers are defined. T-SQL raises
/// <c>THROW &lt;SqlNumber&gt;, ...</c>; <see cref="SqlErrorMapper"/> and the exception middleware turn
/// that into a ProblemDetails carrying the entry's <c>code</c>.
///
/// Reserved THROW ranges (all at or above 50000, the user-defined range):
///   50100-50199 shared / lock protocol
///   50200-50299 Execution guard
///   50300-50399 observation validation
///   50400-50499 design freeze (including the ScoreRule soft-delete rejections)
///   50500-50599 finalization
/// </summary>
public static class ErrorCatalog
{
    public const int SharedRangeStart = 50100;
    public const int ExecutionGuardRangeStart = 50200;
    public const int ObservationRangeStart = 50300;
    public const int DesignFreezeRangeStart = 50400;
    public const int FinalizationRangeStart = 50500;
    public const int RangeSize = 100;

    // -- Errors raised by existing application code (codes added; titles and statuses unchanged) --
    public static readonly ErrorEntry NotFound = new("not-found", HttpStatusCode.NotFound, "Not Found");
    public static readonly ErrorEntry RecordLocked = new("record-locked", HttpStatusCode.Conflict, "Record Locked");
    public static readonly ErrorEntry Conflict = new("conflict", HttpStatusCode.Conflict, "Conflict");
    public static readonly ErrorEntry ValidationFailed = new("validation-failed", HttpStatusCode.BadRequest, "Validation Failed");
    public static readonly ErrorEntry DuplicateKey = new("duplicate-key", HttpStatusCode.Conflict, "Conflict");
    public static readonly ErrorEntry ConstraintViolation = new("constraint-violation", HttpStatusCode.Conflict, "Conflict");
    public static readonly ErrorEntry InternalError = new("internal-error", HttpStatusCode.InternalServerError, "Internal Server Error");

    // -- Shared / lock protocol (50100-50199) --
    public static readonly ErrorEntry VersionLocked = new("version-locked", HttpStatusCode.Conflict, "Version Locked", 50100);
    public static readonly ErrorEntry IdentityChangeRejected = new("identity-change-rejected", HttpStatusCode.Conflict, "Identity Change Rejected", 50101);

    // -- Execution guard (50200-50299) --
    public static readonly ErrorEntry InvalidRuleConfiguration = new("invalid-rule-configuration", HttpStatusCode.Conflict, "Invalid Rule Configuration", 50200);

    // -- Observation validation (50300-50399) --
    public static readonly ErrorEntry VersionPhaseMismatch = new("version-phase-mismatch", HttpStatusCode.Conflict, "Version Or Phase Mismatch", 50300);
    public static readonly ErrorEntry UnavailableRule = new("unavailable-rule", HttpStatusCode.Conflict, "Unavailable Rule", 50301);
    public static readonly ErrorEntry OutOfRangeScore = new("out-of-range-score", HttpStatusCode.BadRequest, "Out Of Range Score", 50302);

    /// <summary>Raised by the unique constraint, not by THROW, so it has no SQL number.</summary>
    public static readonly ErrorEntry DuplicateObservation = new("duplicate-observation", HttpStatusCode.Conflict, "Duplicate Observation");

    // -- Design freeze (50400-50499) --
    public static readonly ErrorEntry DesignFrozen = new("design-frozen", HttpStatusCode.Conflict, "Design Frozen", 50400);
    public static readonly ErrorEntry ScoreRuleAlreadyDeleted = new("score-rule-already-deleted", HttpStatusCode.Conflict, "ScoreRule Already Deleted", 50401);
    public static readonly ErrorEntry ScoreRuleInvalidRowState = new("score-rule-invalid-rowstate", HttpStatusCode.Conflict, "ScoreRule Invalid RowState", 50402);

    // -- Finalization (50500-50599) --
    public static readonly ErrorEntry FinalizeNoExecutions = new("finalize-no-executions", HttpStatusCode.Conflict, "Finalization Failed: No Executions", 50500);
    public static readonly ErrorEntry FinalizeExecutionNotEnded = new("finalize-execution-not-ended", HttpStatusCode.Conflict, "Finalization Failed: Execution Not Ended", 50501);
    public static readonly ErrorEntry FinalizeExecutionWithoutObservation = new("finalize-execution-without-observation", HttpStatusCode.Conflict, "Finalization Failed: Execution Without Observation", 50502);
    public static readonly ErrorEntry FinalizeExecutionPhaseNotConfigured = new("finalize-execution-phase-not-configured", HttpStatusCode.Conflict, "Finalization Failed: Execution Phase Not Configured", 50503);
    public static readonly ErrorEntry FinalizePhaseWithoutObservation = new("finalize-phase-without-observation", HttpStatusCode.Conflict, "Finalization Failed: Phase Without Observation", 50504);
    public static readonly ErrorEntry FinalizePhaseWeightInvalid = new("finalize-phase-weight-invalid", HttpStatusCode.Conflict, "Finalization Failed: Phase Weight Total Invalid", 50505);
    public static readonly ErrorEntry FinalizeObservationIntegrity = new("finalize-observation-integrity", HttpStatusCode.Conflict, "Finalization Failed: Observation Integrity", 50506);
    public static readonly ErrorEntry FinalizeSnapshotRowsExist = new("finalize-snapshot-rows-exist", HttpStatusCode.Conflict, "Finalization Failed: Snapshot Rows Already Exist", 50507);
    public static readonly ErrorEntry SnapshotInsertNotPermitted = new("snapshot-insert-not-permitted", HttpStatusCode.Conflict, "Snapshot Insert Not Permitted", 50508);

    // -- Concurrency (deadlock victim 1205 and lock-wait timeout 1222 are one retryable error) --
    public const int DeadlockVictim = 1205;
    public const int LockTimeout = 1222;
    public static readonly ErrorEntry RetryableConflict = new("retryable-conflict", HttpStatusCode.Conflict, "Concurrent Operation Conflict", Retryable: true);

    public static IReadOnlyList<ErrorEntry> All { get; } =
    [
        NotFound, RecordLocked, Conflict, ValidationFailed, DuplicateKey, ConstraintViolation, InternalError,
        VersionLocked, IdentityChangeRejected,
        InvalidRuleConfiguration,
        VersionPhaseMismatch, UnavailableRule, OutOfRangeScore, DuplicateObservation,
        DesignFrozen, ScoreRuleAlreadyDeleted, ScoreRuleInvalidRowState,
        FinalizeNoExecutions, FinalizeExecutionNotEnded, FinalizeExecutionWithoutObservation,
        FinalizeExecutionPhaseNotConfigured, FinalizePhaseWithoutObservation, FinalizePhaseWeightInvalid,
        FinalizeObservationIntegrity, FinalizeSnapshotRowsExist, SnapshotInsertNotPermitted,
        RetryableConflict,
    ];

    private static readonly Dictionary<int, ErrorEntry> BySqlNumber =
        All.Where(e => e.SqlNumber is not null).ToDictionary(e => e.SqlNumber!.Value);

    public static ErrorEntry? FindBySqlNumber(int number) => BySqlNumber.GetValueOrDefault(number);

    public static ErrorEntry? FindByCode(string code) => All.FirstOrDefault(e => e.Code == code);

    public static bool IsInReservedRange(int number) =>
        number >= SharedRangeStart && number < FinalizationRangeStart + RangeSize;
}
