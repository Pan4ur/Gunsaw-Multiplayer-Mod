@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "GUNSAW_HEADLESS_BAT=%~f0"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -Command "$parts = [IO.File]::ReadAllText($env:GUNSAW_HEADLESS_BAT) -split '(?m)^:powershell\r?$', 2; & ([scriptblock]::Create($parts[1]))"
set "result=%errorlevel%"
if not "%result%"=="0" pause
exit /b %result%

:powershell
$ErrorActionPreference = 'Stop'
$text = if ((Get-UICulture).TwoLetterISOLanguageName -eq 'ru') {
    @{
        Title = 'Запуск headless-лобби Gunsaw'
        Game = 'Папка игры или путь к Gunsaw.exe (Enter для отмены)'
        GameMissing = 'В этой папке не найдены Gunsaw.exe и Gunsaw_Data.'
        Name = 'Имя лобби (Enter, чтобы оставить сохранённое)'
        Relay = 'Адрес релея (Enter для сохранённого адреса, по умолчанию pl.expie.fun)'
        RelayInvalid = 'Введите адрес релея или URL с http:// либо https://.'
        File = 'Путь к файлу с кодом карты'
        UseDefault = 'Enter для default_map.txt'
        PanelOnly = 'Enter для запуска панели без лобби'
        FileMissing = 'Файл карты не найден.'
        Panel = 'Панель управления'
        Log = 'Журнал'
        Failed = 'Не удалось запустить headless-лобби'
        Exited = 'Gunsaw завершился с кодом'
    }
} else {
    @{
        Title = 'Start a Gunsaw headless lobby'
        Game = 'Game folder or path to Gunsaw.exe (Enter to cancel)'
        GameMissing = 'Gunsaw.exe and Gunsaw_Data were not found in that folder.'
        Name = 'Lobby name (Enter to keep the saved name)'
        Relay = 'Relay address (Enter to keep the saved address, default pl.expie.fun)'
        RelayInvalid = 'Enter a relay hostname or an http:// or https:// URL.'
        File = 'Path to a file containing a map code'
        UseDefault = 'Enter for default_map.txt'
        PanelOnly = 'Enter to start the panel without a lobby'
        FileMissing = 'Map file was not found.'
        Panel = 'Admin panel'
        Log = 'Log'
        Failed = 'Could not start the headless lobby'
        Exited = 'Gunsaw exited with code'
    }
}

function Test-GameFolder($folder) {
    [IO.File]::Exists((Join-Path $folder 'Gunsaw.exe')) -and
        [IO.Directory]::Exists((Join-Path $folder 'Gunsaw_Data'))
}

function Quote-GameArgument([string]$value) {
    '"' + ($value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

try {
    Write-Host $text.Title
    $cwd = (Get-Location).ProviderPath
    $scriptFolder = [IO.Path]::GetDirectoryName($env:GUNSAW_HEADLESS_BAT)
    $game = @($scriptFolder, $cwd) | Where-Object { Test-GameFolder $_ } | Select-Object -First 1
    while (-not $game) {
        $entry = (Read-Host $text.Game).Trim().Trim('"')
        if (-not $entry) { exit 0 }
        $folder = if ([IO.Path]::IsPathRooted($entry)) { $entry } else { Join-Path $cwd $entry }
        if ([IO.Path]::GetFileName($folder) -eq 'Gunsaw.exe') { $folder = [IO.Path]::GetDirectoryName($folder) }
        if (Test-GameFolder $folder) { $game = [IO.Path]::GetFullPath($folder) }
        else { Write-Host $text.GameMissing }
    }

    $name = (Read-Host $text.Name).Trim()
    do {
        $relay = (Read-Host $text.Relay).Trim()
        if (-not $relay) { break }
        $url = if ($relay.Contains('://')) { $relay } else { 'https://' + $relay }
        $uri = $null
        if ([Uri]::TryCreate($url, [UriKind]::Absolute, [ref]$uri) -and
            $uri.Scheme -in 'http', 'https' -and $uri.Host) { break }
        Write-Host $text.RelayInvalid
    } while ($true)

    $defaultMap = Join-Path $game 'default_map.txt'
    $mapOption = if ([IO.File]::Exists($defaultMap)) { $text.UseDefault } else { $text.PanelOnly }
    do {
        $entry = (Read-Host ('{0} ({1})' -f $text.File, $mapOption)).Trim().Trim('"')
        if (-not $entry) {
            $map = if ([IO.File]::Exists($defaultMap)) { $defaultMap }
            break
        }
        $map = if ([IO.Path]::IsPathRooted($entry)) { $entry } else { Join-Path $game $entry }
        if ([IO.File]::Exists($map)) { break }
        Write-Host $text.FileMissing
    } while ($true)

    $log = Join-Path $game 'headless.log'
    $arguments = @('-batchmode', '-nographics', '-headlessLobby', '-logFile', '-')
    if ($name) { $arguments += @('--name', $name) }
    if ($relay) { $arguments += @('--master', $relay) }
    if ($map) { $arguments += @('-headlessMap', [IO.Path]::GetFullPath($map)) }
    Write-Host ($text.Panel + '  http://127.0.0.1:8765/')
    Write-Host ($text.Log + '  ' + $log)
    $commandLine = ($arguments | ForEach-Object { Quote-GameArgument $_ }) -join ' '
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

public static class GunsawHeadlessLauncher
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, IntPtr name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private static void JoinLifetimeJob()
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, IntPtr.Zero);
        if (job == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var limits = new ExtendedLimits();
        limits.Basic.LimitFlags = KillOnJobClose;
        int error;
        using (var launcher = Process.GetCurrentProcess())
        {
            if (SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf(limits)) &&
                AssignProcessToJobObject(job, launcher.Handle))
                return;
            error = Marshal.GetLastWin32Error();
        }

        CloseHandle(job);
        throw new Win32Exception(error);
    }

    public static int Run(string exe, string directory, string arguments, string logPath)
    {
        JoinLifetimeJob();
        var start = new ProcessStartInfo(exe, arguments)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        using (var log = new StreamWriter(logPath) { AutoFlush = true })
        using (var game = Process.Start(start))
        {
            var outputLock = new object();
            DataReceivedEventHandler show = (_, line) =>
            {
                if (line.Data == null) return;
                lock (outputLock)
                {
                    Console.WriteLine(line.Data);
                    log.WriteLine(line.Data);
                }
            };
            ConsoleCancelEventHandler stop = (_, key) =>
            {
                key.Cancel = true;
                try { game.Kill(); }
                catch (InvalidOperationException) { }
            };
            game.OutputDataReceived += show;
            game.ErrorDataReceived += show;
            Console.CancelKeyPress += stop;
            try
            {
                game.BeginOutputReadLine();
                game.BeginErrorReadLine();
                game.WaitForExit();
                return game.ExitCode;
            }
            finally { Console.CancelKeyPress -= stop; }
        }
    }
}
'@
    $result = [GunsawHeadlessLauncher]::Run((Join-Path $game 'Gunsaw.exe'), $game, $commandLine, $log)
    if ($result) { Write-Host ($text.Exited + ' ' + $result) }
    exit $result
} catch {
    Write-Host ($text.Failed + '. ' + $_.Exception.Message)
    exit 1
}