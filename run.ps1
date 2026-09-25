# Chạy toàn bộ dự án bằng một lệnh (từ thư mục gốc repo):  .\run.ps1
#   1. Docker: Postgres (5433), Redis (6379), Seq (5341) — nhóm "hospital-management" trên Docker Desktop
#   2. API (5289), Gateway (5100), Frontend (9000) — mỗi cái một cửa sổ PowerShell riêng; đóng cửa sổ là dừng.
# Thành phần nào đang chạy sẵn (cổng đã được lắng nghe) thì bỏ qua, không mở lần hai.
# Dừng container:  docker compose -f dotnet-clean-architecture-cqrs-starter\docker-compose.yml stop

$root     = $PSScriptRoot
$backend  = Join-Path $root 'dotnet-clean-architecture-cqrs-starter'
$frontend = Join-Path $root 'react-codebase'
$compose  = Join-Path $backend 'docker-compose.yml'

function Test-Port([int]$Port) {
    [bool](Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

function Start-Window([string]$Title, [string]$Dir, [string]$Command) {
    Start-Process powershell -WorkingDirectory $Dir -ArgumentList '-NoExit', '-Command',
        "`$Host.UI.RawUI.WindowTitle = '$Title'; $Command"
}

function Fail([string]$Message) {
    Write-Host $Message -ForegroundColor Red
    exit 1
}

# --- 1. Docker ---------------------------------------------------------------
cmd /c "docker info >nul 2>&1"
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Docker chưa chạy — đang mở Docker Desktop...' -ForegroundColor Yellow
    Start-Process "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe"
    $deadline = (Get-Date).AddMinutes(3)
    do {
        Start-Sleep -Seconds 3
        cmd /c "docker info >nul 2>&1"
    } until ($LASTEXITCODE -eq 0 -or (Get-Date) -gt $deadline)
    if ($LASTEXITCODE -ne 0) { Fail 'Docker Desktop không khởi động được sau 3 phút.' }
}

Write-Host 'Khởi động Postgres, Redis, Seq...' -ForegroundColor Cyan
docker compose -f $compose up -d --wait
if ($LASTEXITCODE -ne 0) { Fail 'docker compose up thất bại (xem lỗi ở trên).' }

# --- 2. Backend --------------------------------------------------------------
# Build ở cửa sổ này trước (lỗi hiện ngay tại đây), rồi mới mở cửa sổ chạy với --no-build.
$services = @(
    @{ Name = 'API';     Port = 5289; Project = 'src\CleanArchCqrs.API' },
    @{ Name = 'Gateway'; Port = 5100; Project = 'src\CleanArchCqrs.Gateway' }
)
foreach ($s in $services) {
    if (Test-Port $s.Port) {
        Write-Host "$($s.Name) đang chạy sẵn ở cổng $($s.Port) — bỏ qua." -ForegroundColor DarkGray
        continue
    }
    Write-Host "Build $($s.Name)..." -ForegroundColor Cyan
    dotnet build (Join-Path $backend $s.Project) -v q --nologo
    if ($LASTEXITCODE -ne 0) { Fail "Build $($s.Name) thất bại." }
    Start-Window "HMS $($s.Name)" $backend "dotnet run --no-build --project $($s.Project) --launch-profile http"
}

# --- 3. Frontend -------------------------------------------------------------
if (Test-Port 9000) {
    Write-Host 'Frontend đang chạy sẵn ở cổng 9000 — bỏ qua.' -ForegroundColor DarkGray
} else {
    if (-not (Test-Path (Join-Path $frontend 'node_modules'))) {
        Write-Host 'Cài npm packages cho frontend...' -ForegroundColor Cyan
        Push-Location $frontend
        npm install
        $npmExit = $LASTEXITCODE
        Pop-Location
        if ($npmExit -ne 0) { Fail 'npm install thất bại.' }
    }
    Start-Window 'HMS Frontend' $frontend 'npm start'
}

Write-Host ''
Write-Host 'Xong. Các địa chỉ:' -ForegroundColor Green
Write-Host '  Frontend  http://localhost:9000'
Write-Host '  Gateway   http://localhost:5100'
Write-Host '  API       http://localhost:5289'
Write-Host '  Seq (log) http://localhost:5341'
