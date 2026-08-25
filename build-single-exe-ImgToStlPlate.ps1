[CmdletBinding()]
param(
    [string]$BackendDir = $PSScriptRoot,
    [string]$FrontendDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'ImgToStlPlate.FE'),
    [int]$Port = 5108,
    [string]$ListenAddress = '0.0.0.0',
    [string]$OutputDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'publish'),
    [switch]$SkipFrontendBuild,
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$msg) {
    Write-Host "`n=== $msg ===" -ForegroundColor Cyan
}

function Resolve-FullPath([string]$Path) {
    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Assert-RemovablePath([string]$Path, [string]$Purpose) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Purpose must not be empty."
    }

    $full = (Resolve-FullPath $Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if ([string]::IsNullOrWhiteSpace($full)) {
        throw "$Purpose resolves to an empty path: '$Path'."
    }

    $root = [IO.Path]::GetPathRoot($full).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if ($full -eq $root) {
        throw "$Purpose must not be a drive or share root: '$full'."
    }

    $guard = $PSScriptRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $fullWithSeparator = $full + [IO.Path]::DirectorySeparatorChar
    if ($guard -eq $full -or $guard.StartsWith($fullWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose must not contain the repository: '$full'."
    }

    return $full
}

function Remove-DirectorySafely([string]$Path, [string]$Purpose) {
    $full = Assert-RemovablePath $Path $Purpose
    if (Test-Path -LiteralPath $full) {
        Remove-Item -LiteralPath $full -Recurse -Force
    }
    return $full
}

$BackendDir = Resolve-FullPath $BackendDir
$FrontendDir = Resolve-FullPath $FrontendDir
$OutputDir = Assert-RemovablePath $OutputDir 'OutputDir'

$ApiProject = Join-Path $BackendDir 'ImgToStlPlate.API'
$PublishDir = Join-Path $ApiProject 'bin\Release\net9.0\win-x64\publish'

if (-not (Test-Path $ApiProject)) {
    throw "Backend project not found: $ApiProject"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK not found in PATH'
}

if (-not $SkipFrontendBuild) {
    Write-Step "Building frontend ($FrontendDir)"
    if (-not (Test-Path (Join-Path $FrontendDir 'package.json'))) {
        throw "Frontend project not found: $FrontendDir"
    }
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        throw 'npm not found in PATH'
    }
    Push-Location $FrontendDir
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'npm build failed' }
    }
    finally {
        Pop-Location
    }
}

$browserOut = Join-Path $FrontendDir 'dist\ImgToStlPlate\browser'
if (-not (Test-Path $browserOut)) {
    throw "Frontend build output not found: $browserOut (with -SkipFrontendBuild, run 'npm run build' in $FrontendDir first)"
}

Write-Step "Copying frontend output to wwwroot"
$wwwroot = Remove-DirectorySafely (Join-Path $ApiProject 'wwwroot') 'wwwroot'
New-Item -ItemType Directory -Path $wwwroot | Out-Null
Copy-Item (Join-Path $browserOut '*') $wwwroot -Recurse -Force

Write-Step "Checking bundle for hardcoded API origins"
$scannedExtensions = @('.js', '.mjs', '.css', '.html')
$offenders = Get-ChildItem -LiteralPath $wwwroot -Recurse -File |
    Where-Object { $scannedExtensions -contains $_.Extension.ToLowerInvariant() } |
    Select-String -Pattern 'https?://(localhost|127\.0\.0\.1)[:/]' -List |
    ForEach-Object { '{0}: {1}' -f $_.Path, $_.Line.Trim().Substring(0, [Math]::Min(120, $_.Line.Trim().Length)) }
if ($offenders) {
    throw "The packaged bundle contains an absolute localhost origin. The frontend must call the API through relative '/api' URLs.`n$($offenders -join "`n")"
}

$programCs = Join-Path $ApiProject 'Program.cs'
if (-not (Select-String -Path $programCs -Pattern 'UseStaticFiles' -Quiet)) {
    Write-Warning 'Program.cs does not contain UseStaticFiles - SPA hosting is not wired up. The exe will serve only the API.'
}

Write-Step "Publishing single-file exe"
& dotnet publish (Join-Path $ApiProject 'ImgToStlPlate.API.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -p:NoWarn=MSB3246
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Write-Step "Writing appsettings.json"
$config = @{
    Logging = @{
        LogLevel = @{
            Default = 'Information'
            'Microsoft.AspNetCore' = 'Warning'
        }
    }
    AllowedHosts = '*'
    Urls = "http://$ListenAddress`:$Port"
} | ConvertTo-Json -Depth 5
Set-Content -Path (Join-Path $PublishDir 'appsettings.json') -Value $config -Encoding UTF8

Write-Step "Copying to $OutputDir"
Get-Process -Name 'ImgToStlPlate.API' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$OutputDir*" } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Remove-DirectorySafely $OutputDir 'OutputDir' | Out-Null
New-Item -ItemType Directory -Path $OutputDir | Out-Null
Copy-Item (Join-Path $PublishDir '*') $OutputDir -Recurse -Force

Write-Step "Configuring Windows Firewall (TCP $Port)"
$ruleName = "ImgToStlPlate API TCP $Port"
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    & netsh advfirewall firewall delete rule name=$ruleName 2>$null | Out-Null
    & netsh advfirewall firewall add rule name=$ruleName dir=in action=allow protocol=TCP localport=$Port
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Firewall rule added: $ruleName (inbound TCP $Port)" -ForegroundColor Green
    }
    else {
        Write-Warning "Failed to add firewall rule. Allow the prompt when the app first starts."
    }
}
else {
    Write-Warning 'Not running as Administrator - skipping automatic firewall rule.'
    Write-Warning "Run this script as Administrator once, or allow the Windows firewall prompt when the app first starts."
    Write-Warning "Manual: netsh advfirewall firewall add rule name=`"$ruleName`" dir=in action=allow protocol=TCP localport=$Port"
}

$lanIps = Get-NetIPConfiguration -ErrorAction SilentlyContinue |
    Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } |
    ForEach-Object { $_.IPv4Address.IPAddress } |
    Where-Object { $_ -ne '0.0.0.0' }

Write-Host "`nDone. Artifacts in: $OutputDir" -ForegroundColor Green
Write-Host "Local access : http://localhost:$Port" -ForegroundColor Green
if ($lanIps) {
    Write-Host "LAN access   :" -ForegroundColor Green
    foreach ($ip in $lanIps) {
        Write-Host ("                http://{0}:{1}" -f $ip, $Port) -ForegroundColor Green
    }
}
else {
    Write-Host "LAN access   : (no non-loopback IPv4 address detected)" -ForegroundColor Yellow
}

if ($Launch) {
    Start-Process -FilePath (Join-Path $OutputDir 'ImgToStlPlate.API.exe') -WorkingDirectory $OutputDir
}
