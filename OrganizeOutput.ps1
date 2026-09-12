param(
    [Parameter(Mandatory = $true)]
    [string] $Root
)

$Root = $Root.Trim().Trim('"')
$Root = [System.IO.Path]::GetFullPath($Root)
if (-not (Test-Path -LiteralPath $Root)) { return }

$runtime = Join-Path $Root 'runtime'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null

$keep = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
@(
    'GrXviewer.exe'
    'GrXviewer.dll'
    'GrXviewer.deps.json'
    'GrXviewer.runtimeconfig.json'
    'hostfxr.dll'
    'hostpolicy.dll'
    'coreclr.dll'
    'clrjit.dll'
    'clrgc.dll'
    'clretwrc.dll'
    'mscorrc.dll'
    'System.Private.CoreLib.dll'
    'System.Runtime.dll'
    'System.Runtime.InteropServices.dll'
) | ForEach-Object { [void]$keep.Add($_) }

Get-ChildItem -LiteralPath $Root -File | ForEach-Object {
    if ($keep.Contains($_.Name)) { return }
    if ($_.Extension -in '.png', '.jpg', '.jpeg', '.webp', '.txt') { return }
    Move-Item -LiteralPath $_.FullName -Destination (Join-Path $runtime $_.Name) -Force
}
