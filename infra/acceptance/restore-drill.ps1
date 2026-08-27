#Requires -Version 7.0
<#
.SYNOPSIS
Phase 7-5 / Phase 5 Gate：production 備份的受控還原演練。

.DESCRIPTION
依 docs/deployment/mongodb-backup-restore-runbook.md 執行，但有兩處刻意偏離，
兩者都記在輸出的演練紀錄裡：

1. 執行環境為本機 Docker，非 runbook 所寫的 secured environment。
   production URI 由 stdin 進入容器的 tmpfs，不落本機磁碟、不進 argv、不進環境變數。

2. 丟棄暫時庫（runbook 步驟 6）不由腳本執行。備份 image 內沒有 mongosh，而 mongosh
   沒有 --config，連線字串只能進 argv 或環境變數 —— 兩者 runbook 都明文禁止。
   腳本會印出確切的暫時庫名，由操作者在 Atlas UI 手動丟棄。

production 連線字串釘住 mycollection，因此無法用同一份 config 讀取還原庫
（tools 會拒絕 URI 與 --db 指向不同資料庫）。連帶兩項調整：

- runbook 步驟 2 的「目標庫不存在」前置檢查做不了，所以 --drop 一併拿掉。
  目標庫是全新隨機命名，--drop 本就沒有必要，移除它等於破壞性旗標完全消失。
- 還原庫側的 counts 與 index 建立情形取自 mongorestore 自己的輸出，而非事後再讀一次。
  index 的「定義」只從 production 側取得；還原庫側能證明的是 index 確實被建立。

counts 比對為「並列顯示 + 標示漂移」而非等值判定：archive 是快照，production 之後
仍在寫入，counts 有差是正確行為。

.EXAMPLE
pwsh -File infra/acceptance/restore-drill.ps1
#>
[CmdletBinding()]
param(
    [string]$Project = 'mycollection-504914',
    [string]$BackupBucket = 'mycollection-504914-backups',
    [string]$BackupPrefix = 'mycollection-prod/',
    [string]$SecretName = 'mongo-connection-string',
    [string]$BackupImage = 'asia-east1-docker.pkg.dev/mycollection-504914/mycollection-backup/mongo-backup@sha256:258a4badd7020b4a6baa3faf59034c7eaaf2a328cefa812ea164561e38571d18',
    [string]$WorkRoot = (Join-Path ([System.IO.Path]::GetTempPath()) 'mycollection-restore-drill')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# gcloud 的輸出經過 PowerShell 管線時預設會帶 BOM，容器端的 tr 只吃得掉換行。
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$scriptDirectory = Split-Path -Parent $PSCommandPath
$containerScript = Join-Path $scriptDirectory 'restore-drill-container.sh'
if (-not (Test-Path $containerScript)) {
    throw "Missing container script: $containerScript"
}

function Write-Step { param([string]$Message) Write-Host "==> $Message" -ForegroundColor Cyan }

# --- 步驟 1：選出最新的非空 archive，記錄其識別資訊 -------------------------
Write-Step '選擇最新備份 archive'
$token = (& gcloud auth print-access-token).Trim()
if ([string]::IsNullOrWhiteSpace($token)) { throw 'Could not obtain a gcloud access token.' }

$listUri = "https://storage.googleapis.com/storage/v1/b/$BackupBucket/o" +
           "?prefix=$([uri]::EscapeDataString($BackupPrefix))" +
           '&fields=items(name,generation,size,timeCreated,md5Hash)'
$listing = Invoke-RestMethod -Uri $listUri -Headers @{ Authorization = "Bearer $token" } -ErrorAction Stop

$selected = $listing.items |
    Where-Object { [long]$_.size -gt 0 } |
    Sort-Object { [datetime]$_.timeCreated } |
    Select-Object -Last 1
if (-not $selected) { throw 'No non-empty archive found in the backup bucket.' }

Write-Host "    object     : $($selected.name)"
Write-Host "    generation : $($selected.generation)"
Write-Host "    size       : $($selected.size) bytes"
Write-Host "    created    : $($selected.timeCreated)"

# --- 步驟 2：產生並驗證目標庫名 ---------------------------------------------
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$suffix = -join ((0..3) | ForEach-Object { '{0:x2}' -f (Get-Random -Minimum 0 -Maximum 256) })
$targetDatabase = "mc-r-$stamp-$suffix"

if ($targetDatabase -notmatch '^mc-r-\d{8}T\d{6}Z-[0-9a-f]{8}$') {
    throw "Generated target name failed its own format check: $targetDatabase"
}
if ($targetDatabase -eq 'mycollection') { throw 'Generated target name collided with production.' }
if ($targetDatabase.Length -ge 38) { throw "Generated target name is too long: $targetDatabase" }
Write-Host "    target db  : $targetDatabase"

# --- 步驟 3：下載 archive 到受控目錄 ----------------------------------------
Write-Step '下載 archive'
$workDirectory = Join-Path $WorkRoot $stamp
$null = New-Item -ItemType Directory -Force -Path $workDirectory
$archivePath = Join-Path $workDirectory 'selected.archive.gz'

& gcloud storage cp "gs://$BackupBucket/$($selected.name)" $archivePath --quiet
if ($LASTEXITCODE -ne 0) { throw 'Archive download failed.' }

$archiveItem = Get-Item $archivePath
if ($archiveItem.Length -ne [long]$selected.size) {
    throw "Downloaded size $($archiveItem.Length) does not match GCS size $($selected.size)."
}
Write-Host "    downloaded : $($archiveItem.Length) bytes (matches GCS)"

# --- 步驟 4/5：在容器內還原並收集比對資料 -----------------------------------
Write-Step '還原到暫時資料庫'
$containerWorkPath = ($workDirectory -replace '\\', '/')
$containerScriptPath = ($containerScript -replace '\\', '/')

# URI 走 stdin 進容器的 tmpfs；--tmpfs 保證它不會落在任何 mount 的磁碟上。
& gcloud secrets versions access latest --secret=$SecretName --project=$Project |
    & docker run --rm -i `
        --entrypoint sh `
        --env TARGET_DB=$targetDatabase `
        --volume "${containerWorkPath}:/work" `
        --volume "${containerScriptPath}:/drill.sh:ro" `
        --tmpfs /secure:rw,mode=0700,uid=999,gid=999,size=1m `
        $BackupImage /drill.sh

if ($LASTEXITCODE -ne 0) {
    Write-Warning "還原失敗。暫時庫 $targetDatabase 與本機檔案一律保留供調查（runbook 步驟 6）。"
    Write-Warning "工作目錄：$workDirectory"
    exit 1
}

# --- 步驟 5：比對 ------------------------------------------------------------
# 還原已經完成，暫時庫已經存在。從這裡開始不論發生什麼事都必須印出庫名，
# 否則比對程式碼自己的錯誤會讓暫時庫變成沒人知道要清的孤兒（2026-08-27 發生過一次）。
trap {
    Write-Warning "比對階段發生未預期的錯誤：$($_.Exception.Message)"
    Write-Warning "還原本身已完成。暫時庫仍需手動丟棄：$targetDatabase"
    Write-Warning "工作目錄：$workDirectory"
    exit 1
}

Write-Step '比對 index 定義與 document counts'
$verifyDirectory = Join-Path $workDirectory 'verify'

function Get-Counts {
    param([string]$Path)
    $result = @{}
    if (-not (Test-Path $Path)) { return $result }
    foreach ($line in Get-Content -LiteralPath $Path) {
        $parts = $line -split '\s+', 2
        if ($parts.Count -eq 2) { $result[$parts[0]] = [int]$parts[1] }
    }
    return $result
}

function Get-Indexes {
    param([string]$Path)
    $result = @{}
    if (-not (Test-Path $Path)) { return $result }
    foreach ($file in Get-ChildItem -LiteralPath $Path -Recurse -Filter '*.metadata.json') {
        $collection = $file.Name -replace '\.metadata\.json$', ''
        $metadata = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $indexes = @()
        # StrictMode 之下讀取不存在的屬性會拋例外，而 unique / indexes 都是選用的。
        $indexList = if ($metadata.PSObject.Properties['indexes']) { @($metadata.indexes) } else { @() }
        foreach ($index in $indexList) {
            # 比對 key pattern、名稱與唯一性；其餘欄位（v、ns）會隨環境變動。
            # metadata 是 Extended JSON，方向值會是 {"$numberInt":"1"} 這種包裝物件，
            # 直接內插會印成 @{$numberInt=1}。拆掉包裝，比對字串才是可讀且穩定的。
            $keys = ($index.key.PSObject.Properties | ForEach-Object {
                    $value = $_.Value
                    if ($value -is [psobject]) {
                        $wrapper = @($value.PSObject.Properties)[0]
                        if ($wrapper -and $wrapper.Name -like '$number*') { $value = $wrapper.Value }
                    }
                    "$($_.Name):$value"
                }) -join ','
            $unique = [bool]($index.PSObject.Properties['unique'] -and $index.unique)
            $indexes += ('{0}|{1}|{2}' -f $index.name, $keys, $unique)
        }
        $result[$collection] = ($indexes | Sort-Object) -join ' ;; '
    }
    return $result
}

$prodCounts = Get-Counts (Join-Path $verifyDirectory 'counts-prod.txt')
$restoredCounts = Get-Counts (Join-Path $verifyDirectory 'counts-restored.txt')
$prodIndexes = Get-Indexes (Join-Path $verifyDirectory 'prod')

$indexesRestoredPath = Join-Path $verifyDirectory 'indexes-restored.txt'
$indexesRestored = if (Test-Path $indexesRestoredPath) {
    @(Get-Content -LiteralPath $indexesRestoredPath | Where-Object { $_ })
}
else { @() }

$collections = @($prodCounts.Keys + $restoredCounts.Keys | Sort-Object -Unique)
$failures = 0

Write-Host ''
Write-Host ('{0,-22} {1,10} {2,10}  {3,-10} {4}' -f 'collection', 'prod', 'restored', 'restored', 'prod indexes')
Write-Host ('{0,-22} {1,10} {2,10}  {3,-10} {4}' -f '', '(live)', '(archive)', 'indexes', '')
foreach ($collection in $collections) {
    $prodCount = if ($prodCounts.ContainsKey($collection)) { $prodCounts[$collection] } else { '-' }
    $restoredCount = if ($restoredCounts.ContainsKey($collection)) { $restoredCounts[$collection] } else { '-' }

    # 還原庫必須涵蓋 production 的每一個 collection；反向多出來的也要現形。
    $missing = -not $restoredCounts.ContainsKey($collection) -or -not $prodCounts.ContainsKey($collection)
    $indexBuilt = $indexesRestored -contains $collection
    $prodIndexCount = if ($prodIndexes.ContainsKey($collection)) {
        @($prodIndexes[$collection] -split ' ;; ').Count
    }
    else { 0 }

    if ($missing) {
        $verdict = 'MISSING'
        $colour = 'Red'
        $failures++
    }
    elseif (-not $indexBuilt -and $prodIndexCount -gt 1) {
        # _id 之外還有索引卻沒看到 index 還原紀錄，代表還原不完整。
        $verdict = 'NO INDEX'
        $colour = 'Red'
        $failures++
    }
    else {
        $verdict = if ($indexBuilt) { 'built' } else { 'none' }
        $colour = 'Green'
    }

    Write-Host ('{0,-22} {1,10} {2,10}  {3,-10} {4}' -f `
            $collection, $prodCount, $restoredCount, $verdict, $prodIndexCount) -ForegroundColor $colour
}

# counts 差異是預期的：archive 是快照，production 之後仍在寫入。
$drifted = @($collections | Where-Object {
    $prodCounts.ContainsKey($_) -and $restoredCounts.ContainsKey($_) -and
    $prodCounts[$_] -ne $restoredCounts[$_]
})
Write-Host ''
if ($drifted.Count -gt 0) {
    Write-Host "counts 漂移（預期行為，archive 快照時間為 $($selected.timeCreated)）：$($drifted -join ', ')" -ForegroundColor Yellow
}
else {
    Write-Host 'counts 與 production 完全一致（表示快照後 production 未再寫入）。'
}

$restoreSecondsPath = Join-Path $verifyDirectory 'restore-seconds.txt'
if (Test-Path $restoreSecondsPath) {
    Write-Host "還原耗時：$((Get-Content -LiteralPath $restoreSecondsPath -Raw).Trim()) 秒（Gate 上限 4 小時）"
}

Write-Host ''
if ($failures -gt 0) {
    Write-Warning "有 $failures 個 collection 未通過。暫時庫與本機檔案一律保留供調查。"
    Write-Warning "暫時庫：$targetDatabase｜工作目錄：$workDirectory"
    exit 1
}

Write-Host 'collection 涵蓋完整、index 均已建立，還原演練通過。' -ForegroundColor Green
Write-Host ''
Write-Host '剩下一個手動步驟（見本檔案 .DESCRIPTION 的偏離 2）：' -ForegroundColor Yellow
Write-Host "  在 Atlas UI 丟棄暫時資料庫：$targetDatabase" -ForegroundColor Yellow
Write-Host '  確認名稱與上面完全相同，且不是 mycollection，再執行丟棄。' -ForegroundColor Yellow
Write-Host ''
Write-Host "丟棄後移除本機工作目錄：$workDirectory"
