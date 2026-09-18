using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using WPO.Domain.Enums;
using WPO.Domain.Models;

namespace WPO.Core.Startup;

/// <summary>
/// Windows-only, read-only reader for the most common autorun locations: the
/// current-user and local-machine "Run" registry keys, plus the current
/// user's and the all-users Startup shell folders. Never writes the registry,
/// never modifies/deletes a shortcut or file, never ends a process, and never
/// shells out to PowerShell or any other process. Every registry/filesystem
/// access is wrapped so a single bad entry is skipped instead of throwing.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsStartupEntryReader : IStartupEntryReader
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public IReadOnlyList<StartupEntryCandidate> GetCandidates()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<StartupEntryCandidate>();
        }

        var candidates = new List<StartupEntryCandidate>();

        TryAddRegistryCandidates(Registry.CurrentUser, StartupItemSource.CurrentUserRunRegistry, candidates);
        TryAddRegistryCandidates(Registry.LocalMachine, StartupItemSource.LocalMachineRunRegistry, candidates);
        TryAddFolderCandidates(TryGetFolderPath(Environment.SpecialFolder.Startup), StartupItemSource.CurrentUserStartupFolder, candidates);
        TryAddFolderCandidates(TryGetFolderPath(Environment.SpecialFolder.CommonStartup), StartupItemSource.AllUsersStartupFolder, candidates);

        return candidates;
    }

    public Task<StartupItem> InspectAsync(StartupEntryCandidate candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executablePath = ResolveExecutablePath(candidate);
        string? publisher = null;
        bool? isSigned = null;

        if (executablePath is not null)
        {
            try
            {
                if (File.Exists(executablePath))
                {
                    publisher = TryGetPublisher(executablePath);
                    isSigned = TryCheckSignature(executablePath);
                }
            }
            catch (Exception)
            {
                // Leave publisher/signature as unavailable rather than guessing.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var isEnabled = DetermineEnabledState(candidate);
        var notes = BuildNotes(publisher, isSigned, executablePath);

        return Task.FromResult(new StartupItem
        {
            Name = candidate.Name,
            ExecutablePath = executablePath,
            Publisher = publisher,
            IsSigned = isSigned,
            IsEnabled = isEnabled,
            Source = candidate.Source,
            RiskLevel = RiskLevel.Low,
            Notes = notes
        });
    }

    private static void TryAddRegistryCandidates(RegistryKey hive, StartupItemSource source, List<StartupEntryCandidate> candidates)
    {
        RegistryKey? runKey;
        try
        {
            runKey = hive.OpenSubKey(RunKeyPath, writable: false);
        }
        catch (Exception)
        {
            return;
        }

        if (runKey is null)
        {
            return;
        }

        using (runKey)
        {
            string[] valueNames;
            try
            {
                valueNames = runKey.GetValueNames();
            }
            catch (Exception)
            {
                return;
            }

            foreach (var name in valueNames)
            {
                try
                {
                    var raw = runKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    candidates.Add(new StartupEntryCandidate { Name = name, Source = source, RawCommand = raw });
                }
                catch (Exception)
                {
                    // Skip this single malformed/inaccessible value; keep enumerating.
                }
            }
        }
    }

    private static void TryAddFolderCandidates(string? folderPath, StartupItemSource source, List<StartupEntryCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        string[] files;
        try
        {
            if (!Directory.Exists(folderPath))
            {
                return;
            }

            files = Directory.GetFiles(folderPath);
        }
        catch (Exception)
        {
            return;
        }

        foreach (var file in files)
        {
            try
            {
                var fileName = Path.GetFileName(file);
                if (string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add(new StartupEntryCandidate
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Source = source,
                    RawCommand = file
                });
            }
            catch (Exception)
            {
                // Skip this single file; keep enumerating the rest of the folder.
            }
        }
    }

    private static string? TryGetFolderPath(Environment.SpecialFolder folder)
    {
        try
        {
            var path = Environment.GetFolderPath(folder);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ResolveExecutablePath(StartupEntryCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.RawCommand))
        {
            return null;
        }

        if (candidate.Source is StartupItemSource.CurrentUserStartupFolder or StartupItemSource.AllUsersStartupFolder)
        {
            // Startup-folder candidates already carry the file's own path
            // (this reader does not resolve .lnk shell-link targets).
            return candidate.RawCommand;
        }

        return ParseRegistryCommandPath(candidate.RawCommand);
    }

    private static string? ParseRegistryCommandPath(string rawCommand)
    {
        var trimmed = rawCommand.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed[0] == '"')
        {
            var closingQuoteIndex = trimmed.IndexOf('"', 1);
            return closingQuoteIndex > 0 ? trimmed[1..closingQuoteIndex] : trimmed.Trim('"');
        }

        var spaceIndex = trimmed.IndexOf(' ');
        return spaceIndex > 0 ? trimmed[..spaceIndex] : trimmed;
    }

    private static string? TryGetPublisher(string executablePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool? TryCheckSignature(string executablePath)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate = X509Certificate.CreateFromSignedFile(executablePath);
#pragma warning restore SYSLIB0057
            return certificate is not null;
        }
        catch (CryptographicException)
        {
            // No recognizable Authenticode signature. This is informational
            // only and must never be treated as evidence of malware.
            return false;
        }
        catch (Exception)
        {
            // Access denied, IO error, etc.: signature status could not be determined.
            return null;
        }
    }

    private static bool? DetermineEnabledState(StartupEntryCandidate candidate)
    {
        if (candidate.Source is StartupItemSource.CurrentUserStartupFolder or StartupItemSource.AllUsersStartupFolder)
        {
            // Presence in the Startup folder means the entry runs; there is no
            // separate "disabled" flag for shortcut-based autoruns.
            return true;
        }

        try
        {
            using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath, writable: false);
            if (approvedKey?.GetValue(candidate.Name) is not byte[] { Length: > 0 } raw)
            {
                return null;
            }

            // Task Manager marks a user-disabled entry with a leading 0x02/0x03
            // byte in its approval marker; anything else observed means enabled.
            return raw[0] != 0x02 && raw[0] != 0x03;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? BuildNotes(string? publisher, bool? isSigned, string? executablePath)
    {
        if (executablePath is null)
        {
            return "未能定位可执行文件路径，发布者与签名状态未知；这不代表该项存在风险。";
        }

        if (isSigned is null || publisher is null)
        {
            return "发布者或签名状态未知；未知本身不代表该项是恶意软件，请自行核实来源。";
        }

        return isSigned == true
            ? $"由“{publisher}”签名。"
            : $"未检测到有效数字签名（发布者：{publisher}）；未签名不代表该项是恶意软件。";
    }
}
