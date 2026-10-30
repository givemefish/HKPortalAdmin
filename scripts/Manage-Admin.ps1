param(
    [ValidateSet('Build', 'Run', 'Publish')]
    [string]$Action = 'Run'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'HKPortalAdmin.csproj'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$candidates = @(
    (Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'),
    (Join-Path $projectRoot '.tools/dotnet/dotnet.exe'),
    $dotnetCommand.Source
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique

$dotnetPath = $null
foreach ($candidate in $candidates) {
    $installedSdks = & $candidate --list-sdks
    if ($LASTEXITCODE -eq 0 -and ($installedSdks | Where-Object { $_ -match '^10\.' })) {
        $dotnetPath = $candidate
        break
    }
}
if (-not $dotnetPath) { throw '.NET 10 SDK is required. Install it before running this script.' }

Write-Host "Using SDK host: $dotnetPath"
switch ($Action) {
    'Build' { & $dotnetPath build $projectPath -c Release }
    'Run' { & $dotnetPath run --project $projectPath -c Release --launch-profile HKPortalAdmin }
    'Publish' {
        $publishPath = Join-Path $projectRoot '.tools/publish/HKPortalAdmin'
        & $dotnetPath publish $projectPath -c Release -o $publishPath
    }
}
exit $LASTEXITCODE
