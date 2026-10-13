<#
.SYNOPSIS
    Static resource checks that the compiler does not perform.

.DESCRIPTION
    1. Resources/Strings.en.xaml and Strings.es.xaml contain exactly the same keys.
    2. Every text key used in XAML ({DynamicResource Key_X}) or in C# ("Key_X") exists.
    3. Every icon (SymbolIcon / Symbol="...") exists in the referenced WPF-UI version.

    A failure in any of these points would only show up at run time (untranslated text or an exception
    when loading the view), so this script runs in build.ps1 and in CI.

.OUTPUTS
    Exit code 0 if everything is correct; 1 if there are errors.

.EXAMPLE
    ./tools/Check-Resources.ps1
#>
[CmdletBinding()]
param(
    [string]$Root = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\IPSwitch')
)

$ErrorActionPreference = 'Stop'
$errors = New-Object System.Collections.Generic.List[string]

function Get-ResourceKeys([string]$file) {
    [regex]::Matches((Get-Content -Raw -Encoding UTF8 $file), 'x:Key="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
}

# --- 1. Language parity ---------------------------------------------------------------------
$en = @(Get-ResourceKeys (Join-Path $Root 'Resources\Strings.en.xaml'))
$es = @(Get-ResourceKeys (Join-Path $Root 'Resources\Strings.es.xaml'))
$en | Where-Object { $es -notcontains $_ } | ForEach-Object { $errors.Add("Key only in Strings.en.xaml: $_") }
$es | Where-Object { $en -notcontains $_ } | ForEach-Object { $errors.Add("Key only in Strings.es.xaml: $_") }

# --- 2. Used keys ---------------------------------------------------------------------------
$sources = Get-ChildItem $Root -Recurse -Include *.cs, *.xaml |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|tests)\\' -and $_.Name -notlike 'Strings.*.xaml' }

$used = foreach ($file in $sources) {
    $text = Get-Content -Raw -Encoding UTF8 $file.FullName
    $text = [regex]::Replace($text, '<!--.*?-->', '', 'Singleline')   # Ignore XAML comments
    # Application keys always have the form Prefix_Name
    [regex]::Matches($text, 'DynamicResource\s+([A-Za-z]+_[A-Za-z0-9]+)') | ForEach-Object { $_.Groups[1].Value }
    if ($file.Extension -eq '.cs') {
        [regex]::Matches($text, '"([A-Z][A-Za-z]+_[A-Za-z0-9]+)"') | ForEach-Object { $_.Groups[1].Value }
    }
}
$used = $used | Sort-Object -Unique
$used | Where-Object { $en -notcontains $_ } | ForEach-Object { $errors.Add("Missing localization key: $_") }

# --- 3. WPF-UI icons ------------------------------------------------------------------------
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$csproj = Get-Content -Raw (Join-Path $Root 'IPSwitch.csproj')
$wpfUiVersion = [regex]::Match($csproj, 'Include="WPF-UI"\s+Version="([^"]+)"').Groups[1].Value
$dll = Join-Path $packages "wpf-ui\$wpfUiVersion\lib\net472\Wpf.Ui.dll"

if (Test-Path $dll) {
    $symbolNames = [Enum]::GetNames([System.Reflection.Assembly]::LoadFile($dll).GetType('Wpf.Ui.Controls.SymbolRegular'))
    $symbols = foreach ($file in ($sources | Where-Object Extension -eq '.xaml')) {
        $text = Get-Content -Raw -Encoding UTF8 $file.FullName
        [regex]::Matches($text, 'SymbolIcon\s+([A-Za-z0-9]+)\}') | ForEach-Object { $_.Groups[1].Value }
        [regex]::Matches($text, 'Symbol="([A-Za-z0-9]+)"') | ForEach-Object { $_.Groups[1].Value }
        [regex]::Matches($text, 'Property="Symbol"\s+Value="([A-Za-z0-9]+)"') | ForEach-Object { $_.Groups[1].Value }
    }
    $symbols = $symbols | Sort-Object -Unique
    $symbols | Where-Object { $symbolNames -notcontains $_ } | ForEach-Object { $errors.Add("Unknown WPF-UI symbol: $_") }
} else {
    $errors.Add("Wpf.Ui.dll not found at $dll (run dotnet restore first)")
    $symbols = @()
}

# --- Result ---------------------------------------------------------------------------------
Write-Host ("Localization keys: {0} (en) / {1} (es), {2} used. Symbols used: {3}." -f $en.Count, $es.Count, $used.Count, $symbols.Count)
if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Host "ERROR: $_" -ForegroundColor Red }
    exit 1
}
Write-Host 'Resource checks passed.' -ForegroundColor Green
exit 0
