# FbAuto target-machine environment self-check.
# Called by check-env.cmd. Writes env-report.txt next to this script.
#
# Kept pure ASCII on purpose: Windows PowerShell 5.1 reads a BOM-less
# UTF-8 file as ANSI, so any Chinese literal here would come out garbled.
# Values that come from the OS (e.g. the OS caption) may still be Chinese.

$ErrorActionPreference = 'SilentlyContinue'
$ProgressPreference = 'SilentlyContinue'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$lines = New-Object System.Collections.Generic.List[string]

function Emit([string]$key, $value) {
    if ($null -eq $value) { $value = '' }
    $text = [string]$value

    # Registry defaults and HTTP headers come back with trailing CR/LF and
    # stray quotes; a single line break shifts every following column and
    # makes the report hard to read when pasted into a chat.
    $text = $text -replace '[\r\n\t]+', ' '
    $text = $text.Trim().Trim('"')
    if ($text.Length -gt 88) { $text = $text.Substring(0, 88) + '...' }
    if ($text -eq '') { $text = '(empty)' }

    $lines.Add(('{0,-30} {1}' -f $key, $text))
}

function Section([string]$title) {
    $lines.Add('')
    $lines.Add('==== ' + $title + ' ====')
}

# ---------------------------------------------------------------- machine
Section 'MACHINE'
Emit 'time.local' (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Emit 'time.utc' ((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss'))
Emit 'computer' $env:COMPUTERNAME
Emit 'user' $env:USERNAME
Emit 'arch' $env:PROCESSOR_ARCHITECTURE

$os = Get-CimInstance Win32_OperatingSystem
Emit 'os.caption' $os.Caption
Emit 'os.version' ($os.Version + ' build ' + $os.BuildNumber)
if ($os.TotalVisibleMemorySize) {
    Emit 'ram.gb' ([math]::Round($os.TotalVisibleMemorySize / 1MB, 1))
}

$tz = Get-TimeZone
if ($tz) {
    $sign = '+'
    $hours = $tz.BaseUtcOffset.TotalHours
    if ($hours -lt 0) { $sign = '-'; $hours = -$hours }
    Emit 'timezone' ($tz.Id + ' UTC' + $sign + $hours)
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Emit 'is.admin' $isAdmin

Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Used -ne $null } | ForEach-Object {
    Emit ('disk.' + $_.Name + '.free.gb') ([math]::Round($_.Free / 1GB, 1))
}

# ---------------------------------------------------------------- app folder
Section 'APP FOLDER'
Emit 'folder' $here

$required = @(
    'FbAuto.Presentation.exe',
    'FbAuto.Presentation.dll',
    'FbAuto.Core.dll',
    'FbAuto.Application.dll',
    'FbAuto.Automation.dll',
    'FbAuto.Browser.dll',
    'Microsoft.Playwright.dll'
)
foreach ($name in $required) {
    Emit $name (Test-Path (Join-Path $here $name))
}

# The Playwright driver is the single most common thing lost when copying:
# without it every page action fails, but the error says "browser did not start".
# Layout produced by Microsoft.Playwright.targets:
#   .playwright\node\win32_x64\node.exe   (.playwright\node\node.exe does NOT exist)
#   .playwright\package\cli.js
Emit '.playwright\node.exe' (Test-Path (Join-Path $here '.playwright\node\win32_x64\node.exe'))
Emit '.playwright\cli.js' (Test-Path (Join-Path $here '.playwright\package\cli.js'))

# Self-contained proof: if coreclr.dll is present the target machine does not
# need the .NET runtime installed.
Emit 'coreclr.dll' (Test-Path (Join-Path $here 'coreclr.dll'))

Emit 'filecount.top' (Get-ChildItem $here -File).Count
Emit 'filecount.all' (Get-ChildItem $here -Recurse -File).Count

foreach ($opt in @('accounts.sample.csv', 'accounts-HOWTO.md', 'accounts.csv', 'config.json', 'license.dat')) {
    Emit ($opt + '.exists') (Test-Path (Join-Path $here $opt))
}

$logDir = Join-Path $here 'Logs'
if (Test-Path $logDir) {
    $latest = Get-ChildItem $logDir -Filter '*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latest) {
        Emit 'log.latest' $latest.Name
        Emit 'log.latest.size' $latest.Length
        $errCount = (Select-String -Path $latest.FullName -Pattern '\[ERR\]' -SimpleMatch).Count
        Emit 'log.latest.err.count' $errCount
    }
}

# ---------------------------------------------------------------- chrome
Section 'CHROME'
$chromeCandidates = New-Object System.Collections.Generic.List[string]

$reg = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe'
if ($reg -and $reg.'(default)') { $chromeCandidates.Add($reg.'(default)') }

$chromeCandidates.Add('C:\Program Files\Google\Chrome\Application\chrome.exe')
$chromeCandidates.Add('C:\Program Files (x86)\Google\Chrome\Application\chrome.exe')
if ($env:LOCALAPPDATA) {
    $chromeCandidates.Add((Join-Path $env:LOCALAPPDATA 'Google\Chrome\Application\chrome.exe'))
}

$chrome = $chromeCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if ($chrome) {
    Emit 'chrome.path' $chrome
    $ver = (Get-Item $chrome).VersionInfo.ProductVersion
    Emit 'chrome.version' $ver
} else {
    Emit 'chrome.path' 'NOT FOUND'
}

# ---------------------------------------------------------------- bitbrowser
Section 'BITBROWSER'
$bitOk = $false
try {
    $tcp = New-Object System.Net.Sockets.TcpClient
    $bitOk = $tcp.ConnectAsync('127.0.0.1', 54345).Wait(2000)
    $tcp.Close()
} catch {
    $bitOk = $false
}
Emit 'bit.api.54345' $(if ($bitOk) { 'REACHABLE' } else { 'NOT RUNNING' })

# ---------------------------------------------------------------- network
Section 'NETWORK'
function Test-Http([string]$url) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $resp = Invoke-WebRequest -Uri $url -TimeoutSec 12 -UseBasicParsing
        $sw.Stop()
        return ('HTTP ' + [int]$resp.StatusCode + ' / ' + [int]$sw.Elapsed.TotalMilliseconds + ' ms')
    } catch {
        $sw.Stop()
        $msg = $_.Exception.Message
        if ($msg.Length -gt 80) { $msg = $msg.Substring(0, 80) }
        return ('FAIL / ' + [int]$sw.Elapsed.TotalMilliseconds + ' ms / ' + $msg)
    }
}

foreach ($url in @('https://www.facebook.com/', 'https://www.google.com/', 'https://graph.facebook.com/')) {
    Emit $url (Test-Http $url)
}

$dns = Resolve-DnsName 'facebook.com' -Type A | Select-Object -First 3 -ExpandProperty IPAddress
if ($dns) { Emit 'dns.facebook' ($dns -join ', ') } else { Emit 'dns.facebook' 'RESOLVE FAILED' }

try {
    $info = Invoke-RestMethod 'https://ipinfo.io/json' -TimeoutSec 10
    Emit 'egress.ip' $info.ip
    Emit 'egress.location' (([string]$info.city) + ', ' + ([string]$info.country))
    Emit 'egress.org' $info.org
} catch {
    Emit 'egress.ip' 'lookup failed'
}

# ---------------------------------------------------------------- output
$text = $lines -join [Environment]::NewLine
Write-Host $text
Write-Host ''

$report = Join-Path $here 'env-report.txt'
$text | Out-File -FilePath $report -Encoding utf8
Write-Host ('Report written to: ' + $report)
