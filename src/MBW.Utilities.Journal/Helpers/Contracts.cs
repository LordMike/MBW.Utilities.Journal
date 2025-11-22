using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MBW.Utilities.Journal.Helpers;

internal static class Contracts
{
    public static void Requires([DoesNotReturnIf(false)]bool condition, [CallerArgumentExpression(nameof(condition))]string? message = null)
    {
        if (!condition)
            throw new ArgumentException(message ?? "Precondition failed");
    }

    public static void Requires([DoesNotReturnIf(false)]bool condition, Func<Exception> exceptionFactory)
    {
        if (!condition)
            throw exceptionFactory();
    }

    public static void Ensures([DoesNotReturnIf(false)]bool condition, [CallerArgumentExpression(nameof(condition))]string? message = null)
    {
        if (!condition)
            throw new InvalidOperationException(message ?? "Postcondition failed");
    }

    public static void Ensures([DoesNotReturnIf(false)]bool condition, Func<Exception> exceptionFactory)
    {
        if (!condition)
            throw exceptionFactory();
    }

    public static void Invariant([DoesNotReturnIf(false)]bool condition, [CallerArgumentExpression(nameof(condition))]string? message = null)
    {
        if (!condition)
            throw new InvalidOperationException(message ?? "Invariant failed");
    }

    public static void Invariant([DoesNotReturnIf(false)]bool condition, Func<Exception> exceptionFactory)
    {
        if (!condition)
            throw exceptionFactory();
    }
}
