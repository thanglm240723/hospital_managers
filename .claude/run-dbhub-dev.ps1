# Khởi động MCP DBHub (stdio) đọc PostgreSQL dev theo .claude/dbhub-dev.toml.
# Đăng ký:  claude mcp add hms-db -- powershell -NoProfile -ExecutionPolicy Bypass -File .claude/run-dbhub-dev.ps1
$ErrorActionPreference = "Stop"

# Không cho DBHub kế thừa proxy/CA của môi trường (proxy công ty, router model...) vì kết nối DB là local.
$varsToClear = @(
    "NODE_EXTRA_CA_CERTS",
    "NODE_OPTIONS",
    "HTTP_PROXY",
    "HTTPS_PROXY",
    "ALL_PROXY",
    "http_proxy",
    "https_proxy",
    "all_proxy",
    "NPM_CONFIG_PROXY",
    "NPM_CONFIG_HTTPS_PROXY"
)
foreach ($name in $varsToClear) {
    Remove-Item "Env:$name" -ErrorAction SilentlyContinue
}

$config = Join-Path $PSScriptRoot "dbhub-dev.toml"
if (-not (Test-Path $config)) {
    throw "Chưa có $config — sao chép từ dbhub-dev.example.toml và điền mật khẩu."
}

# Ưu tiên bản DBHub cài cố định (pin phiên bản); không có thì chạy qua npx với phiên bản ghim.
$dbhubVersion = "1.2.3"
$pinned = Join-Path $HOME ".claude-tools\dbhub-$dbhubVersion\node_modules\.bin\dbhub.cmd"

if (Test-Path $pinned) {
    & $pinned --config $config --transport stdio
} else {
    & npx -y "@bytebase/dbhub@$dbhubVersion" --config $config --transport stdio
}

exit $LASTEXITCODE
