using System;
using System.Text.RegularExpressions;
using Jarvis.Core.System;

namespace javis.Services.SystemActions;

public static class SafetyGuard
{
    private static readonly Regex DestructiveShellRegex = new(
        pattern: "\\b(del|erase|rmdir|rd|format|reg\\s+delete|bcdedit|diskpart|shutdown|sc\\s+delete)\\b",
        options: RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ConfirmShellRegex = new(
        pattern: "\\b(reg\\s+add|reg\\s+import|netsh|Set-ExecutionPolicy|Remove-Item)\\b",
        options: RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static ExecutionResult ValidateShellCommand(string command)
    {
        command = (command ?? "").Trim();
        if (command.Length == 0)
            return ExecutionResult.Fail("command is required.");

        if (DestructiveShellRegex.IsMatch(command))
            return ExecutionResult.Fail("Blocked by safety guard: destructive command.");

        if (ConfirmShellRegex.IsMatch(command))
            return ExecutionResult.NeedsConfirmation("중요한 시스템 설정 변경이 포함돼요. 정말 실행할까요?");

        return ExecutionResult.Ok();
    }
}
