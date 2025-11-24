using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MBW.Utilities.Journal.Helpers;

internal static class Contracts
{
    internal static void Requires([DoesNotReturnIf(false)]bool condition, [CallerArgumentExpression(nameof(condition))]string? message = null)
    {
        if (!condition)
            throw new ArgumentException(message ?? "Precondition failed");
    }

    internal static void Requires([DoesNotReturnIf(false)]bool condition, Func<Exception> exceptionFactory)
    {
        if (!condition)
            throw exceptionFactory();
    }

    internal static void Ensures([DoesNotReturnIf(false)]bool condition, [CallerArgumentExpression(nameof(condition))]string? message = null)
    {
        if (!condition)
            throw new InvalidOperationException(message ?? "Postcondition failed");
    }

    internal static void Ensures([DoesNotReturnIf(false)]bool condition, Func<Exception> exceptionFactory)
    {
        if (!condition)
            throw exceptionFactory();
    }

    internal static void Invariant([DoesNotReturnIf(false)]bool condition, [CallerArgumentExpression(nameof(condition))]string? message = null)
    {
        if (!condition)
            throw new InvalidOperationException(message ?? "Invariant failed");
    }

    internal static void Invariant([DoesNotReturnIf(false)]bool condition, Func<Exception> exceptionFactory)
    {
        if (!condition)
            throw exceptionFactory();
    }
}
