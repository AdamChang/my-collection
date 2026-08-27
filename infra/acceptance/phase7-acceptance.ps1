#Requires -Version 7.0
<#
.SYNOPSIS
Phase 7 Production Acceptance —— API 層可自動化的部分（7-1 與 7-4）。

.DESCRIPTION
篩選、精選與 Share Link 的版面語意屬於 UI 行為（ADR-0006 / 0007 / 0009），
不在本腳本範圍，由 Phase 7 驗收紀錄的 UI 清單人工確認。

密碼不接受明文參數：改由 ACCEPTANCE_PASSWORD 環境變數帶入，或互動式輸入。
兩者都不會落檔，也不會出現在指令列。

.EXAMPLE
pwsh -File infra/acceptance/phase7-acceptance.ps1 -Email you@example.com
#>
[CmdletBinding()]
param(
    [string]$ApiBaseUrl = 'https://mycollection-api-cswrakuenq-de.a.run.app',
    [string]$Email = $env:ACCEPTANCE_EMAIL,
    # 2026-08-08 資料搬移時上傳的物件；現行 revision 建立於 08-24 之後，
    # 讀得到即為「圖片跨 revision 存活」的證據，不需要製造新資料。
    [string]$CrossRevisionMediaPath = '6a6b24e4d50d20e15ebec66a/6a6b537b6a72369f7f79bf24/6a6b55626a72369f7f79bf25-card.webp',
    [string]$MediaBucket = 'mycollection-504914-media',
    [string]$BackupBucket = 'mycollection-504914-backups'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$ApiBaseUrl = $ApiBaseUrl.TrimEnd('/')

if ([string]::IsNullOrWhiteSpace($Email)) {
    throw 'Email is required. Pass -Email or set ACCEPTANCE_EMAIL.'
}

if ([string]::IsNullOrWhiteSpace($env:ACCEPTANCE_PASSWORD)) {
    $securePassword = Read-Host -Prompt "Password for $Email" -AsSecureString
}
else {
    $securePassword = ConvertTo-SecureString -String $env:ACCEPTANCE_PASSWORD -AsPlainText -Force
}

$script:PassCount = 0
$script:FailCount = 0

function Get-Status {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [string]$Method = 'GET',
        [hashtable]$Headers,
        [string]$Body
    )
    # 這裡只關心狀態碼；回應主體可能是圖片，不需要落檔。
    $arguments = @{
        Uri                = $Uri
        Method             = $Method
        SkipHttpErrorCheck = $true
        ErrorAction        = 'Stop'
    }
    if ($Headers) { $arguments.Headers = $Headers }
    if ($PSBoundParameters.ContainsKey('Body')) {
        $arguments.Body = $Body
        $arguments.ContentType = 'application/json'
    }
    try {
        return (Invoke-WebRequest @arguments).StatusCode
    }
    catch {
        # DNS / TLS 之類連線層失敗沒有狀態碼，回 0 讓判定落到 FAIL。
        Write-Verbose "Request to $Uri failed: $($_.Exception.Message)"
        return 0
    }
}

function Invoke-Api {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Method = 'GET',
        [hashtable]$Headers,
        [string]$Body
    )
    $arguments = @{
        Uri         = "$ApiBaseUrl$Path"
        Method      = $Method
        ErrorAction = 'Stop'
    }
    if ($Headers) { $arguments.Headers = $Headers }
    if ($PSBoundParameters.ContainsKey('Body')) {
        $arguments.Body = $Body
        $arguments.ContentType = 'application/json'
    }
    return Invoke-RestMethod @arguments
}

function Assert-Status {
    param([string]$Label, [int]$Expected, [int]$Actual)
    if ($Actual -eq $Expected) {
        Write-Host ('PASS  {0,-52} {1}' -f $Label, $Actual) -ForegroundColor Green
        $script:PassCount++
    }
    else {
        Write-Host ('FAIL  {0,-52} got {1}, want {2}' -f $Label, $Actual, $Expected) -ForegroundColor Red
        $script:FailCount++
    }
}

# 「應該被拒絕」的項目關心的是有沒有擴權，而不是拒絕碼剛好是 401 還是 403。
# 硬釘單一數字會讓正確的行為因為換了拒絕語意而誤報成 FAIL。
function Assert-Rejected {
    param([string]$Label, [int]$Actual)
    if ($Actual -in 401, 403, 404) {
        Write-Host ('PASS  {0,-52} {1} (rejected)' -f $Label, $Actual) -ForegroundColor Green
        $script:PassCount++
    }
    else {
        Write-Host ('FAIL  {0,-52} got {1}, want a rejection' -f $Label, $Actual) -ForegroundColor Red
        $script:FailCount++
    }
}

function Assert-Created {
    param([string]$Label, [int]$Actual)
    if ($Actual -in 200, 201) {
        Write-Host ('PASS  {0,-52} {1}' -f $Label, $Actual) -ForegroundColor Green
        $script:PassCount++
    }
    else {
        Write-Host ('FAIL  {0,-52} got {1}, want 200 or 201' -f $Label, $Actual) -ForegroundColor Red
        $script:FailCount++
    }
}

$categoryId = $null
$itemId = $null
$shareId = $null
$authHeaders = $null
$showcasedItem = $null
$privateItem = $null

try {
    Write-Host '== 7-1 登入與 CRUD ==' -ForegroundColor Cyan
    Assert-Status 'health/live 匿名可讀' 200 (Get-Status -Uri "$ApiBaseUrl/health/live")
    Assert-Status 'health/startup 匿名可讀' 200 (Get-Status -Uri "$ApiBaseUrl/health/startup")

    # 密碼只在這個轉換裡短暫成為明文，之後不再被引用。
    $plainPassword = [System.Net.NetworkCredential]::new('', $securePassword).Password
    $loginBody = @{ email = $Email; password = $plainPassword } | ConvertTo-Json -Compress
    $plainPassword = $null

    $loginResponse = $null
    try {
        $loginResponse = Invoke-Api -Path '/auth/login' -Method 'POST' -Body $loginBody
        Assert-Status '登入取得 token' 200 200
    }
    catch {
        Assert-Status '登入取得 token' 200 0
        throw '登入失敗，後續項目無法進行。'
    }
    finally {
        $loginBody = $null
    }

    $authHeaders = @{ Authorization = "Bearer $($loginResponse.accessToken)" }
    Assert-Status '已認證 /auth/me' 200 (Get-Status -Uri "$ApiBaseUrl/auth/me" -Headers $authHeaders)

    $suffix = 'phase7-{0}' -f (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')

    $categoryBody = @{
        name               = $suffix
        icon               = 'figure'
        kind               = 'Physical'
        defaultDisplayMode = 'List'
        fields             = @()
    } | ConvertTo-Json -Compress
    $category = Invoke-Api -Path '/categories' -Method 'POST' -Headers $authHeaders -Body $categoryBody
    $categoryId = $category.id

    function New-Item-Fixture {
        param([string]$Name, [bool]$Showcased)
        $body = @{
            categoryId  = $categoryId
            name        = $Name
            description = $null
            tags        = @()
            isShowcased = $Showcased
            attributes  = @{}
            acquisition = $null
        } | ConvertTo-Json -Compress
        return Invoke-Api -Path '/items' -Method 'POST' -Headers $authHeaders -Body $body
    }

    $itemBody = @{
        categoryId  = $categoryId
        name        = "$suffix-item"
        description = $null
        tags        = @()
        isShowcased = $true
        attributes  = @{}
        acquisition = $null
    } | ConvertTo-Json -Compress
    Assert-Created '建立品項' (Get-Status -Uri "$ApiBaseUrl/items" -Method 'POST' -Headers $authHeaders -Body $itemBody)
    $item = Invoke-Api -Path '/items' -Method 'POST' -Headers $authHeaders -Body $itemBody
    $itemId = $item.id
    Assert-Status '讀取品項' 200 (Get-Status -Uri "$ApiBaseUrl/items/$itemId" -Headers $authHeaders)

    # Share scope 的測試必須自備 fixture。沿用既有品項的圖片會讓斷言取決於那個品項
    # 當下的精選狀態，而那是測試無法控制、也從未確認過的。
    $showcasedItem = New-Item-Fixture -Name "$suffix-showcased" -Showcased $true
    $privateItem = New-Item-Fixture -Name "$suffix-private" -Showcased $false

    $pixelPath = Join-Path ([System.IO.Path]::GetTempPath()) "$suffix-pixel.png"
    [System.IO.File]::WriteAllBytes($pixelPath, [System.Convert]::FromBase64String(
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII='))

    $showcasedImage = Invoke-RestMethod -Uri "$ApiBaseUrl/items/$($showcasedItem.id)/images" -Method 'POST' `
        -Headers $authHeaders -Form @{ file = Get-Item $pixelPath } -ErrorAction 'Stop'
    $privateImage = Invoke-RestMethod -Uri "$ApiBaseUrl/items/$($privateItem.id)/images" -Method 'POST' `
        -Headers $authHeaders -Form @{ file = Get-Item $pixelPath } -ErrorAction 'Stop'
    Remove-Item -LiteralPath $pixelPath -ErrorAction 'SilentlyContinue'

    Assert-Status '擁有者可讀非精選品項的圖片' 200 (Get-Status -Uri "$ApiBaseUrl/media/$($privateImage.cardPath)" -Headers $authHeaders)

    Write-Host ''
    Write-Host '== 7-4 圖片跨 revision 存活 ==' -ForegroundColor Cyan
    Assert-Status '現行 revision 讀取 08-08 上傳的圖片' 200 (Get-Status -Uri "$ApiBaseUrl/media/$CrossRevisionMediaPath" -Headers $authHeaders)

    Write-Host ''
    Write-Host '== 7-4 匿名授權沒有擴權 ==' -ForegroundColor Cyan
    Assert-Rejected '匿名讀 /media' (Get-Status -Uri "$ApiBaseUrl/media/$CrossRevisionMediaPath")
    Assert-Rejected '匿名讀 /items' (Get-Status -Uri "$ApiBaseUrl/items")
    Assert-Rejected '匿名 GCS 直讀 media 物件' (Get-Status -Uri "https://storage.googleapis.com/$MediaBucket/$CrossRevisionMediaPath")
    Assert-Rejected '匿名列舉 media bucket' (Get-Status -Uri "https://storage.googleapis.com/storage/v1/b/$MediaBucket/o")
    Assert-Rejected '匿名列舉 backup bucket' (Get-Status -Uri "https://storage.googleapis.com/storage/v1/b/$BackupBucket/o")
    Assert-Rejected '不存在的 share slug' (Get-Status -Uri "$ApiBaseUrl/public/zzzznotarealslugzzzz")

    Write-Host ''
    Write-Host '== 7-1 公開 Share Link ==' -ForegroundColor Cyan
    $shareBody = @{
        scope              = 'Showcase'
        includeCategoryIds = @()
        includePrice       = $false
        includeRating      = $false
        collageSlotCount   = 4
        expiresAt          = $null
    } | ConvertTo-Json -Compress
    $share = Invoke-Api -Path '/shares' -Method 'POST' -Headers $authHeaders -Body $shareBody
    $shareId = $share.id
    Assert-Status '匿名讀取 share 內容' 200 (Get-Status -Uri "$ApiBaseUrl/public/$($share.slug)")

    # 這兩項必須成對判讀。只驗拒絕的話，一個對所有路徑都回 404 的壞掉端點也會 PASS ——
    # 授權邊界要證明的是「該給的給、不該給的不給」，單向斷言證明不了任何事。
    Assert-Status 'share 範圍內的圖片（精選品項，應可讀）' 200 `
        (Get-Status -Uri "$ApiBaseUrl/public/$($share.slug)/media/$($showcasedImage.cardPath)")
    Assert-Rejected 'share 範圍外的圖片（非精選品項，應拒）' `
        (Get-Status -Uri "$ApiBaseUrl/public/$($share.slug)/media/$($privateImage.cardPath)")
}
finally {
    if ($authHeaders) {
        # 順序有意義：share 先撤、品項再刪、category 最後，否則會撞到相依性。
        $cleanupTargets = @()
        if ($shareId) { $cleanupTargets += "/shares/$shareId" }
        if ($showcasedItem) { $cleanupTargets += "/items/$($showcasedItem.id)" }
        if ($privateItem) { $cleanupTargets += "/items/$($privateItem.id)" }
        if ($itemId) { $cleanupTargets += "/items/$itemId" }
        if ($categoryId) { $cleanupTargets += "/categories/$categoryId" }

        foreach ($path in $cleanupTargets) {
            try {
                Invoke-WebRequest -Uri "$ApiBaseUrl$path" -Method 'DELETE' -Headers $authHeaders -SkipHttpErrorCheck -ErrorAction 'Stop' | Out-Null
            }
            catch {
                Write-Warning "清理 $path 失敗：$($_.Exception.Message)"
            }
        }
    }

    Write-Host ''
    Write-Host ('PASS={0} FAIL={1}' -f $script:PassCount, $script:FailCount)
}

if ($script:FailCount -gt 0) { exit 1 }
