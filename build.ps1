<#
.SYNOPSIS
    Builds, validates and packages IP Switch as a single portable executable.

.DESCRIPTION
    1. Checks resources (localization keys and icons) with tools/Check-Resources.ps1.
    2. Builds in Release (0 warnings expected; XML documentation is mandatory).
    3. Runs the unit tests (can be skipped with -SkipTests).
    4. Copies the result to artifacts\IPSwitch.exe and writes its SHA-256 next to it.

.PARAMETER SkipTests
    Does not run the unit tests.

.EXAMPLE
    ./build.ps1
#>
[CmdletBinding()]
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$project = 'src\IPSwitch\IPSwitch.csproj'
$output = 'src\IPSwitch\bin\Release\net48\IPSwitch.exe'

dotnet restore 'IPSwitch.slnx'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& "$PSScriptRoot\tools\Check-Resources.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $project -c Release --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipTests) {
    dotnet test 'tests\IPSwitch.Tests\IPSwitch.Tests.csproj' -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

New-Item -ItemType Directory -Force 'artifacts' | Out-Null
Copy-Item $output 'artifacts\IPSwitch.exe' -Force
$hash = (Get-FileHash 'artifacts\IPSwitch.exe' -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content 'artifacts\IPSwitch.exe.sha256' "$hash  IPSwitch.exe" -Encoding ASCII

$size = (Get-Item 'artifacts\IPSwitch.exe').Length / 1MB
Write-Host ("artifacts\IPSwitch.exe ({0:N2} MB)  SHA-256 {1}" -f $size, $hash) -ForegroundColor Green
