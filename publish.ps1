$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$proj = Join-Path $root 'src\GrXviewer.csproj'
$stage = Join-Path $root 'build'
$dist = Join-Path $root 'dist'

Get-Process GrXviewer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}

dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $stage
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $root 'OrganizeOutput.ps1') -Root $stage

New-Item -ItemType Directory -Force -Path $dist | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $dist 'runtime') | Out-Null

# Drop leftover AI packages only — keep ReShade / dxgi / addon files the user added.
$staleDirs = @(
    (Join-Path $dist 'models')
)
foreach ($dir in $staleDirs) {
    if (Test-Path -LiteralPath $dir) {
        Remove-Item -LiteralPath $dir -Recurse -Force
        Write-Host "Removed $(Split-Path $dir -Leaf)"
    }
}
Get-ChildItem -LiteralPath (Join-Path $dist 'runtime') -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match 'onnx|OnnxRuntime|DirectML' } |
    ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Force
        Write-Host "Removed runtime\$($_.Name)"
    }

Get-ChildItem -LiteralPath $stage -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $dist $_.Name) -Force
}

$stageRuntime = Join-Path $stage 'runtime'
if (Test-Path -LiteralPath $stageRuntime) {
    Get-ChildItem -LiteralPath $stageRuntime -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $dist "runtime\$($_.Name)") -Force
    }
}

Remove-Item -LiteralPath $stage -Recurse -Force

$shadersSrc = Join-Path $root 'shaders'
$shadersDst = Join-Path $dist 'shaders'
New-Item -ItemType Directory -Force -Path $shadersDst | Out-Null
if (Test-Path -LiteralPath $shadersSrc) {
    Get-ChildItem -LiteralPath $shadersSrc -Filter *.hlsl -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $shadersDst $_.Name) -Force
    }
}

Write-Host "Published to $dist"
Write-Host "Run $($dist)\GrXviewer.exe"
