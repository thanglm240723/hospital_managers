# Kiểm tra build/test của HMS, in tóm tắt ngắn; log đầy đủ ghi ra .claude/work/logs/.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tooling/validate.ps1 -Mode Quick
#
#   Quick     build solution + unit test                       (không cần Docker)
#   Full      Quick + integration test (Testcontainers)         (cần Docker đang chạy)
#   Frontend  Jest (CI) + ESLint cho react-codebase
#   All       Full + Frontend
#
# Exit code 0 khi mọi bước PASS, 1 khi có bước FAIL.

param(
    [ValidateSet('Quick', 'Full', 'Frontend', 'All')]
    [string]$Mode = 'Quick'
)

$ErrorActionPreference = 'Continue'
$root     = Split-Path -Parent $PSScriptRoot
$backend  = Join-Path $root 'dotnet-clean-architecture-cqrs-starter'
$frontend = Join-Path $root 'react-codebase'
$logDir   = Join-Path $root '.claude\work\logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$results = New-Object System.Collections.Generic.List[object]

function Invoke-Step([string]$Name, [string]$Dir, [string]$Command, [string]$SummaryPattern) {
    $log = Join-Path $logDir "validate-$stamp-$Name.log"
    Write-Host "==> $Name" -ForegroundColor Cyan
    Push-Location $Dir
    try {
        cmd /c "$Command > `"$log`" 2>&1"
        $exit = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    $status = if ($exit -eq 0) { 'PASS' } else { 'FAIL' }
    $color = if ($exit -eq 0) { 'Green' } else { 'Red' }
    Get-Content $log | Select-String -Pattern $SummaryPattern | Where-Object { $_.Line -notmatch 'MSB3026' } |
        Select-Object -Last 15 | ForEach-Object { Write-Host "    $($_.Line.Trim())" }
    if ($exit -ne 0 -and (Select-String -Path $log -Pattern 'being used by another process' -Quiet)) {
        Write-Host '    Gợi ý: API/Gateway đang chạy và khóa file .exe — đóng cửa sổ run.ps1 rồi chạy lại.' -ForegroundColor Yellow
    }
    Write-Host "    $status  (log: $log)" -ForegroundColor $color
    $results.Add([pscustomobject]@{ Step = $Name; Status = $status; Log = $log })
}

$sln = 'dotnet-clean-architecture-cqrs-starter.sln'
$testSummary = '(Passed!|Failed!|error CS|\[FAIL\]|Total tests|Build FAILED)'

if ($Mode -in @('Quick', 'Full', 'All')) {
    Invoke-Step 'build' $backend "dotnet build $sln -v q --nologo" '(error |Build succeeded|Build FAILED|\d+ Error)'
    Invoke-Step 'unit-tests' $backend 'dotnet test tests/CleanArchCqrs.UnitTests --no-build -v q --nologo' $testSummary
}

if ($Mode -in @('Full', 'All')) {
    cmd /c "docker info >nul 2>&1"
    if ($LASTEXITCODE -ne 0) {
        Write-Host '==> integration-tests' -ForegroundColor Cyan
        Write-Host '    BLOCKED  Docker chưa chạy — Testcontainers cần Docker Desktop.' -ForegroundColor Yellow
        $results.Add([pscustomobject]@{ Step = 'integration-tests'; Status = 'BLOCKED'; Log = '' })
    } else {
        Invoke-Step 'integration-tests' $backend 'dotnet test tests/CleanArchCqrs.IntegrationTests --no-build -v q --nologo' $testSummary
    }
}

if ($Mode -in @('Frontend', 'All')) {
    if (-not (Test-Path (Join-Path $frontend 'node_modules'))) {
        Write-Host '==> frontend' -ForegroundColor Cyan
        Write-Host '    BLOCKED  Chưa có node_modules — chạy npm install trong react-codebase.' -ForegroundColor Yellow
        $results.Add([pscustomobject]@{ Step = 'frontend'; Status = 'BLOCKED'; Log = '' })
    } else {
        Invoke-Step 'frontend-tests' $frontend 'set CI=true&& npm test' '(Tests:|Test Suites:|✕|FAIL )'
        Invoke-Step 'frontend-lint' $frontend 'npx eslint src' '(\d+ problems?|\s(error|warning)\s)'
    }
}

Write-Host ''
Write-Host "Tóm tắt ($Mode):" -ForegroundColor Cyan
$results | ForEach-Object { Write-Host ('  {0,-18} {1}' -f $_.Step, $_.Status) }

if ($results | Where-Object { $_.Status -ne 'PASS' }) { exit 1 }
exit 0
