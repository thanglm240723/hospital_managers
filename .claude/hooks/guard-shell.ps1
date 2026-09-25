# Hook PreToolUse chặn lệnh shell nguy hiểm cho Bash và PowerShell.
#
# Repo có thể được chạy ở chế độ bỏ qua hỏi quyền, nên thao tác hậu quả lớn phải bị chặn cứng ở đây
# thay vì trông vào hộp thoại xác nhận.
#
# Thiết kế:
#   * Xét TOÀN BỘ lệnh, không chỉ phần đầu.
#   * Tách lệnh thành đoạn theo dấu phân cách shell; mỗi đoạn được đánh giá theo FILE THỰC THI ĐẦU TIÊN.
#     Nhờ vậy `grep 'DROP TABLE' file.sql` vẫn được phép còn `psql -c "drop table x"` bị chặn: công cụ chỉ đọc
#     không trở nên nguy hiểm vì chuỗi nó đang tìm.
#   * Lệnh bọc (bash -c, powershell -Command, sudo, xargs, env, ...) được bóc ra và đánh giá lại phần bên trong.
#
# Luôn exit 0. Chặn bằng JSON permissionDecision=deny.

$ErrorActionPreference = 'Stop'

$ReadOnlyLeaders = @(
    'grep', 'egrep', 'fgrep', 'rg', 'ripgrep', 'ag', 'ack', 'findstr',
    'cat', 'bat', 'head', 'tail', 'less', 'more', 'sed', 'awk', 'cut', 'tr', 'sort', 'uniq', 'wc',
    'ls', 'dir', 'tree', 'stat', 'file', 'diff', 'cmp', 'md5sum', 'sha256sum',
    'echo', 'printf', 'jq', 'yq', 'which', 'where', 'type', 'basename', 'dirname', 'realpath',
    'select-string', 'get-content', 'get-childitem', 'get-item', 'write-host', 'write-output',
    'out-string', 'measure-object', 'sort-object', 'select-object', 'where-object', 'foreach-object',
    'convertfrom-json', 'convertto-json', 'resolve-path', 'test-path', 'join-path', 'split-path'
)

$WrapperLeaders = @(
    'bash', 'sh', 'zsh', 'dash', 'ksh', 'pwsh', 'powershell', 'powershell.exe', 'cmd', 'cmd.exe',
    'sudo', 'doas', 'env', 'nohup', 'time', 'timeout', 'xargs', 'nice', 'stdbuf', 'script',
    'eval', 'exec', 'invoke-expression', 'iex', 'start-process', 'wsl', 'winpty', 'ssh'
)

function Get-Segments {
    param([string]$Command)

    # Tách theo dấu phân cách, nhớ đoạn nào nhận dữ liệu từ pipe (quan trọng với `cat x.sql | psql`).
    # Cố ý đơn giản: dấu phân cách nằm trong chuỗi quote chỉ sinh THÊM đoạn, mà đoạn thừa vẫn bị đánh giá
    # theo file thực thi đầu của nó, nên không thể lọt lệnh nguy hiểm.
    $tokens = [regex]::Split($Command, '(\|\||&&|\||;|\n|\r)') | Where-Object { $_ -ne '' }

    $segments = New-Object System.Collections.Generic.List[object]
    $pipedInto = $false

    foreach ($token in $tokens) {
        if ($token -in @('||', '&&', '|', ';')) {
            $pipedInto = ($token -eq '|')
            continue
        }
        if ($token -match '^[\r\n]+$') { $pipedInto = $false; continue }

        $text = $token.Trim()
        if ($text.Length -eq 0) { continue }

        $segments.Add([pscustomobject]@{ Text = $text; PipedInto = $pipedInto })
        $pipedInto = $false
    }

    return $segments
}

function Get-Leader {
    param([string]$Segment)

    $text = $Segment.TrimStart('(', '{', ' ', "`t", '$')

    # Bỏ phép gán VAR=value (POSIX) và tiền tố PowerShell "$x =".
    while ($text -match '^[A-Za-z_][A-Za-z0-9_]*=[^\s]*\s+(.*)$') { $text = $Matches[1] }
    if ($text -match '^\$[A-Za-z_][A-Za-z0-9_]*\s*=\s*(.*)$') { $text = $Matches[1] }

    if ($text -notmatch '^([^\s]+)') { return '' }
    $token = $Matches[1].Trim('"', "'", '&')

    # Chỉ giữ tên file để nhận ra cả /usr/bin/psql và C:\tools\psql.exe.
    $token = ($token -split '[\\/]')[-1]
    return $token.ToLowerInvariant()
}

function Remove-Wrapper {
    param([string]$Segment)

    if ($Segment -notmatch '^\s*[^\s]+\s+(.*)$') { return '' }
    $rest = $Matches[1]

    # Bỏ các cờ của chính lệnh bọc, giữ lại payload sau -c/-Command.
    while ($rest -match '^\s*(-[A-Za-z-]+)\s+(.*)$') {
        $flag = $Matches[1].ToLowerInvariant()
        $rest = $Matches[2]
        if ($flag -in @('-c', '-command', '-e', '-encodedcommand', '-file')) { break }
    }

    return $rest.Trim().Trim('"', "'")
}

function Test-Psql {
    param([string]$Segment, [bool]$PipedInto)

    # File script hoặc stdin/redirect mang SQL mà hook không đọc được ⇒ coi như ghi dữ liệu.
    # `-c` inline được đánh giá theo nội dung SQL, nên điều tra chỉ đọc (`psql -c "select ..."`) vẫn chạy được.
    if ($Segment -match '(?i)(^|\s)(-f|--file)\b') {
        return 'Đã chặn: `psql -f` chạy file SQL chưa được review. Chuẩn bị file để người dùng tự chạy.'
    }
    if ($PipedInto -or $Segment -match '<\s*[^\s]') {
        return 'Đã chặn: `psql` đọc SQL từ stdin/redirect nên không review được. Chuẩn bị SQL để người dùng tự chạy.'
    }

    $sqlMutation = '(?is)(^|[;\s(''"])(insert|update|delete|merge|truncate|alter|drop|create|grant|revoke|comment\s+on|vacuum\s+full|reindex|refresh\s+materialized|call|do\s+\$|copy\s+[^\s]+\s+from)\b'
    if ($Segment -match $sqlMutation) {
        return 'Đã chặn: ghi/đổi schema PostgreSQL trực tiếp. Điều tra DB chỉ đọc; thay đổi schema đi qua EF migration, dữ liệu do người dùng tự chạy.'
    }
    return $null
}

function Test-Segment {
    param([string]$Segment, [int]$Depth, [bool]$PipedInto = $false)

    if ($Depth -gt 4) { return $null }

    $leader = Get-Leader $Segment

    if ($WrapperLeaders -contains $leader) {
        $inner = Remove-Wrapper $Segment
        if ([string]::IsNullOrWhiteSpace($inner)) { return $null }
        foreach ($innerSegment in Get-Segments $inner) {
            $reason = Test-Segment -Segment $innerSegment.Text -Depth ($Depth + 1) -PipedInto $innerSegment.PipedInto
            if ($reason) { return $reason }
        }
        return $null
    }

    # find chỉ đọc, TRỪ KHI được yêu cầu xóa hoặc chạy lệnh phá hủy.
    if ($leader -eq 'find') {
        if ($Segment -match '(?i)(^|\s)-delete(\s|$)' -or $Segment -match '(?i)-(exec|execdir|ok|okdir)\s+(rm|shred|Remove-Item)\b') {
            return 'Đã chặn: `find -delete` / `find -exec rm`. Xóa từng đường dẫn cụ thể.'
        }
        return $null
    }

    if ($ReadOnlyLeaders -contains $leader) { return $null }

    # ---- PostgreSQL: chỉ đọc -------------------------------------------------------------------
    if ($leader -in @('psql', 'psql.exe', 'pgcli')) {
        return Test-Psql -Segment $Segment -PipedInto $PipedInto
    }
    if ($leader -in @('pg_restore', 'pg_restore.exe', 'pg_dumpall', 'dropdb', 'dropdb.exe', 'createdb')) {
        return 'Đã chặn: lệnh có thể thay đổi/xóa database PostgreSQL. Chuẩn bị lệnh để người dùng tự chạy.'
    }

    # ---- Docker: không xóa volume dữ liệu, psql trong container vẫn chỉ đọc --------------------
    if ($leader -in @('docker', 'docker.exe', 'docker-compose')) {
        if ($Segment -match '(?i)\bdown\b.*(\s-v\b|--volumes)') {
            return 'Đã chặn: `docker compose down -v` xóa volume dữ liệu (PostgreSQL/Redis/Seq). Dùng `docker compose stop` hoặc để người dùng tự chạy.'
        }
        if ($Segment -match '(?i)\bvolume\s+(rm|prune)\b' -or $Segment -match '(?i)\bsystem\s+prune\b') {
            return 'Đã chặn: xóa volume/prune Docker làm mất dữ liệu dev. Để người dùng tự chạy.'
        }
        if ($Segment -match '(?i)\bexec\b.*\b(psql)\b(.*)$') {
            return Test-Psql -Segment ('psql' + $Matches[2]) -PipedInto $PipedInto
        }
        return $null
    }

    # ---- Xóa đệ quy ----------------------------------------------------------------------------
    if ($leader -in @('rm', 'rmdir', 'shred', 'unlink')) {
        if ($Segment -match '(?i)(^|\s)-[a-z]*r[a-z]*f|(^|\s)-[a-z]*f[a-z]*r|(^|\s)--recursive|(^|\s)--force') {
            return 'Đã chặn: xóa đệ quy/cưỡng bức (`rm -rf`). Xóa từng đường dẫn cụ thể, hoặc để người dùng tự chạy.'
        }
        return $null
    }

    if ($leader -eq 'remove-item') {
        $recurse = $Segment -match '(?i)(^|\s)-recurse\b'
        $force = $Segment -match '(?i)(^|\s)-force\b'
        $wildcard = $Segment -match '[*?]'
        if (($recurse -and $force) -or ($recurse -and $wildcard)) {
            return 'Đã chặn: `Remove-Item -Recurse -Force`. Xóa từng đường dẫn cụ thể, hoặc để người dùng tự chạy.'
        }
        return $null
    }

    if ($leader -in @('del', 'erase', 'rd')) {
        if ($Segment -match '(?i)(^|\s)/s\b') {
            return 'Đã chặn: xóa đệ quy. Xóa từng đường dẫn cụ thể, hoặc để người dùng tự chạy.'
        }
        return $null
    }

    # ---- Git: không đẩy lên remote, không viết lại lịch sử, không bỏ thay đổi của người dùng -------
    # commit/merge/cherry-pick/revert không bị chặn ở đây — settings.json đặt chúng ở mức "ask".
    if ($leader -in @('git', 'git.exe')) {
        $gitRules = @(
            @{ Pattern = '(?i)^\S+(\s+-[Cc]\s+\S+)*\s+push\b'; Reason = '`git push` (thay đổi remote)' },
            @{ Pattern = '(?i)\bbranch\b[^|;]*\s(-D|--delete\s+--force|-d\s+--force|--force\s+--delete)\b'; Reason = 'xóa branch cưỡng bức (`git branch -D`)' },
            @{ Pattern = '(?i)\brebase\b'; Reason = '`git rebase` (viết lại lịch sử)' },
            @{ Pattern = '(?i)\breset\s+--hard\b'; Reason = '`git reset --hard`' },
            @{ Pattern = '(?i)\bclean\s+-[a-z]*f'; Reason = '`git clean -f`' },
            @{ Pattern = '(?i)\brestore\b'; Reason = '`git restore` (bỏ thay đổi trong working tree)' },
            @{ Pattern = '(?i)\bcheckout\s+(-f\b|--force\b|--\s)'; Reason = '`git checkout --`/`-f` (bỏ thay đổi trong working tree)' },
            @{ Pattern = '(?i)\bstash\s+(drop|clear)\b'; Reason = '`git stash drop/clear`' },
            @{ Pattern = '(?i)\bcommit\b[^|;]*--amend\b'; Reason = '`git commit --amend` (sửa commit đã có)' },
            @{ Pattern = '(?i)\b(filter-branch|filter-repo)\b'; Reason = 'viết lại lịch sử' },
            @{ Pattern = '(?i)\breflog\s+(delete|expire)\b'; Reason = 'xóa reflog' },
            @{ Pattern = '(?i)\bupdate-ref\s+-d\b'; Reason = 'xóa ref' }
        )
        foreach ($rule in $gitRules) {
            if ($Segment -match $rule.Pattern) {
                return "Đã chặn: $($rule.Reason). Chuẩn bị thay đổi và để người dùng tự chạy lệnh git."
            }
        }
        return $null
    }

    # ---- EF Core: migration code-first được phép, xóa database thì không ------------------------
    if ($leader -in @('dotnet', 'dotnet.exe', 'dotnet-ef')) {
        if ($Segment -match '(?i)\bef\s+database\s+drop\b') {
            return 'Đã chặn: `dotnet ef database drop` xóa toàn bộ database. Để người dùng tự chạy nếu thật sự cần.'
        }
        if ($Segment -match '(?i)\bef\s+database\s+update\b' -and $Segment -match '(?i)--connection\b') {
            return 'Đã chặn: `dotnet ef database update --connection` có thể nhắm DB ngoài môi trường dev. Để người dùng tự chạy.'
        }
        if ($Segment -match '(?i)\buser-secrets\s+list\b') {
            return 'Đã chặn: `dotnet user-secrets list` in secret ra màn hình.'
        }
        return $null
    }

    # ---- Hạ tầng (phòng khi sau này có) ---------------------------------------------------------
    if ($leader -in @('kubectl', 'kubectl.exe', 'oc')) {
        if ($Segment -match '(?i)\b(apply|delete|patch|scale|replace|edit|set|drain|cordon|uncordon|taint|annotate|label)\b' -or
            $Segment -match '(?i)\brollout\s+(restart|undo|pause|resume)\b') {
            return 'Đã chặn: thay đổi cluster Kubernetes. Chuẩn bị manifest/lệnh để người dùng tự chạy.'
        }
        return $null
    }

    if ($leader -in @('helm', 'helm.exe')) {
        if ($Segment -match '(?i)\b(install|upgrade|uninstall|rollback|delete)\b') {
            return 'Đã chặn: thay đổi Helm release. Chuẩn bị lệnh để người dùng tự chạy.'
        }
        return $null
    }

    if ($leader -in @('terraform', 'terraform.exe', 'tofu')) {
        if ($Segment -match '(?i)\b(apply|destroy|import|taint|untaint)\b' -or
            $Segment -match '(?i)\bstate\s+(rm|mv|push|replace-provider)\b') {
            return 'Đã chặn: thay đổi state/hạ tầng Terraform. Chuẩn bị lệnh để người dùng tự chạy.'
        }
        return $null
    }

    return $null
}

# Đọc stdin dạng UTF-8 để lệnh có tiếng Việt không bị hỏng.
[Console]::InputEncoding = [Text.Encoding]::UTF8
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$inputJson = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($inputJson)) { exit 0 }

try { $call = $inputJson | ConvertFrom-Json } catch { exit 0 }
$command = [string]$call.tool_input.command
if ([string]::IsNullOrWhiteSpace($command)) { exit 0 }

$denyReason = $null
foreach ($segment in Get-Segments $command) {
    $denyReason = Test-Segment -Segment $segment.Text -Depth 0 -PipedInto $segment.PipedInto
    if ($denyReason) { break }
}

if ($denyReason) {
    @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'deny'
            permissionDecisionReason = $denyReason
        }
    } | ConvertTo-Json -Depth 5 -Compress
}

exit 0
