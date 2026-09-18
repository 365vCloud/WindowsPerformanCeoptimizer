namespace WPO.Domain.Enums;

/// <summary>
/// Where a read-only startup item entry was discovered. Common Windows autorun
/// locations only; this optimizer never reads or writes any other autorun
/// mechanism (services, scheduled tasks, WMI subscriptions, etc.).
/// </summary>
public enum StartupItemSource
{
    /// <summary>HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run</summary>
    CurrentUserRunRegistry = 0,

    /// <summary>HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run</summary>
    LocalMachineRunRegistry = 1,

    /// <summary>Current user's Startup shell folder.</summary>
    CurrentUserStartupFolder = 2,

    /// <summary>All Users (common) Startup shell folder.</summary>
    AllUsersStartupFolder = 3
}
