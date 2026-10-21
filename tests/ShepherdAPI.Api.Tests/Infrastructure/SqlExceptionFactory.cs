using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace ShepherdAPI.Api.Tests.Infrastructure;

/// <summary>
/// Builds a <see cref="SqlException"/> carrying a given error number, for unit tests of code that reacts to the number
/// alone (the deadlock retry). Microsoft.Data.SqlClient offers no public constructor, so this goes through reflection and
/// fails loudly, with the field names it found, if a future version changes the layout.
/// </summary>
public static class SqlExceptionFactory
{
    public static SqlException Create(int number, string message = "test error")
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        var error = RuntimeHelpers.GetUninitializedObject(typeof(SqlError));
        var numberField = typeof(SqlError).GetFields(all)
            .FirstOrDefault(f => f.FieldType == typeof(int) && f.Name.Contains("number", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("SqlError has no int number field: " + string.Join(", ", typeof(SqlError).GetFields(all).Select(f => f.Name)));
        numberField.SetValue(error, number);
        var messageField = typeof(SqlError).GetFields(all)
            .FirstOrDefault(f => f.FieldType == typeof(string) && f.Name.Contains("message", StringComparison.OrdinalIgnoreCase));
        messageField?.SetValue(error, message);

        var collection = (SqlErrorCollection)RuntimeHelpers.GetUninitializedObject(typeof(SqlErrorCollection));
        var listField = typeof(SqlErrorCollection).GetFields(all).FirstOrDefault(f => typeof(IList).IsAssignableFrom(f.FieldType))
            ?? throw new InvalidOperationException("SqlErrorCollection has no list field: " + string.Join(", ", typeof(SqlErrorCollection).GetFields(all).Select(f => f.Name)));
        var list = (IList)Activator.CreateInstance(listField.FieldType)!;
        list.Add(error);
        listField.SetValue(collection, list);

        var exception = (SqlException)RuntimeHelpers.GetUninitializedObject(typeof(SqlException));
        var errorsField = typeof(SqlException).GetFields(all).FirstOrDefault(f => f.FieldType == typeof(SqlErrorCollection))
            ?? throw new InvalidOperationException("SqlException has no SqlErrorCollection field: " + string.Join(", ", typeof(SqlException).GetFields(all).Select(f => f.Name)));
        errorsField.SetValue(exception, collection);
        return exception;
    }
}
