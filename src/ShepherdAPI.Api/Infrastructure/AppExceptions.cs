namespace ShepherdAPI.Api.Infrastructure;

/// <summary>The requested resource does not exist.</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>Request failed validation. Field-level messages are returned to the client as a 400.</summary>
public class ValidationAppException(IDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;

    public ValidationAppException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }
}

/// <summary>
/// The write was rejected because the governing ExperimentVersion is locked (LockedUTC is set),
/// or because the target row is otherwise immutable (e.g. an Execution whose EndUTC is already set).
/// Maps to HTTP 409 Conflict. See docs/ARCHITECTURE.md §5.
/// </summary>
public class RecordLockedException(string message) : Exception(message);

/// <summary>A uniqueness or foreign-key constraint in the database was violated by the request.</summary>
public class ConflictException(string message) : Exception(message);
