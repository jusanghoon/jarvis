using System;

namespace Jarvis.Core.System;

public readonly record struct ExecutionResult(bool Success, string? Error, bool RequiresConfirmation = false, string? ConfirmationPrompt = null)
{
    public static ExecutionResult Ok() => new(true, null);

    public static ExecutionResult Fail(string error) => new(false, string.IsNullOrWhiteSpace(error) ? "Unknown error." : error);

    public static ExecutionResult NeedsConfirmation(string prompt, string? error = null)
        => new(false, string.IsNullOrWhiteSpace(error) ? null : error, RequiresConfirmation: true, ConfirmationPrompt: prompt);
}

public readonly record struct ExecutionResult<T>(bool Success, T? Value, string? Error, bool RequiresConfirmation = false, string? ConfirmationPrompt = null)
{
    public static ExecutionResult<T> Ok(T? value) => new(true, value, null);

    public static ExecutionResult<T> Fail(string error) => new(false, default, string.IsNullOrWhiteSpace(error) ? "Unknown error." : error);

    public static ExecutionResult<T> NeedsConfirmation(string prompt, string? error = null)
        => new(false, default, string.IsNullOrWhiteSpace(error) ? null : error, RequiresConfirmation: true, ConfirmationPrompt: prompt);
}
