[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [switch]$SkipBrowser
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RuntimeRoot = Join-Path $ProjectRoot '.yaztek-launcher'
$LogRoot = Join-Path $RuntimeRoot 'logs'
$StatePath = Join-Path $RuntimeRoot 'state.json'
$CredentialPath = Join-Path $RuntimeRoot 'db-password.clixml'
$BackendProject = Join-Path $ProjectRoot 'backend\src\YAZTEK.Api\YAZTEK.Api.csproj'
$InferencePython = Join-Path $ProjectRoot 'inference\.venv\Scripts\python.exe'
$FrontendPackage = Join-Path $ProjectRoot 'frontend\package.json'
$script:State = $null
$script:PostgresUser = 'postgres'
$script:PostgresDatabase = 'yaztek'

function Write-Step([string]$Message) { Write-Host ('[YAZTEK] ' + $Message) -ForegroundColor Cyan }
function Write-Ok([string]$Message) { Write-Host ('[  OK  ] ' + $Message) -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host ('[ UYARI] ' + $Message) -ForegroundColor Yellow }
function Fail([string]$Message) { throw $Message }

function Require-File([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Fail "$Description bulunamadi: $Path" }
}

function Require-Command([string]$Name, [string]$InstallHint) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) { Fail "$Name bulunamadi. $InstallHint" }
    return $command.Source
}

function New-EmptyState {
    return [pscustomobject]@{
        Version = 1
        StartedAtUtc = [DateTime]::UtcNow.ToString('o')
        DockerDesktopStartedByLauncher = $false
        PostgreSqlStartedByLauncher = $false
        Processes = @()
    }
}

function Load-State {
    if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) { return New-EmptyState }
    try {
        $loaded = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
        if ($null -eq $loaded.Processes) { $loaded | Add-Member -NotePropertyName Processes -NotePropertyValue @() -Force }
        if ($null -eq $loaded.DockerDesktopStartedByLauncher) { $loaded | Add-Member -NotePropertyName DockerDesktopStartedByLauncher -NotePropertyValue $false -Force }
        if ($null -eq $loaded.PostgreSqlStartedByLauncher) { $loaded | Add-Member -NotePropertyName PostgreSqlStartedByLauncher -NotePropertyValue $false -Force }
        return $loaded
    }
    catch {
        Write-Warn 'Onceki calisma durum dosyasi okunamadi; yeni durum dosyasi olusturulacak.'
        return New-EmptyState
    }
}

function Save-State {
    if (-not (Test-Path -LiteralPath $RuntimeRoot)) { New-Item -ItemType Directory -Path $RuntimeRoot -Force | Out-Null }
    $script:State | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $StatePath -Encoding UTF8
}

function Add-OwnedProcess([string]$Service, [System.Diagnostics.Process]$Process) {
    $entry = [pscustomobject]@{
        Service = $Service
        Id = $Process.Id
        Name = $Process.ProcessName
        StartTimeUtcTicks = $Process.StartTime.ToUniversalTime().Ticks
    }
    $script:State.Processes = @($script:State.Processes | Where-Object { $_.Service -ne $Service }) + $entry
    Save-State
}

function Start-OwnedProcess {
    param(
        [string]$Service,
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WorkingDirectory,
        [hashtable]$Environment = @{}
    )
    $savedEnvironment = @{}
    foreach ($key in $Environment.Keys) {
        $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, [string]$Environment[$key], 'Process')
    }
    $safeName = $Service.ToLowerInvariant() -replace '[^a-z0-9-]', '-'
    $stdout = Join-Path $LogRoot ($safeName + '.out.log')
    $stderr = Join-Path $LogRoot ($safeName + '.err.log')
    try {
        $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    }
    finally {
        foreach ($key in $Environment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process')
        }
    }
    Add-OwnedProcess -Service $Service -Process $process
    return $process
}

function Test-TcpPort([string]$HostName, [int]$Port, [int]$TimeoutMilliseconds = 800) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $result = $client.BeginConnect($HostName, $Port, $null, $null)
        if (-not $result.AsyncWaitHandle.WaitOne($TimeoutMilliseconds, $false)) { return $false }
        $client.EndConnect($result)
        return $true
    }
    catch { return $false }
    finally { $client.Dispose() }
}

function Get-Json([string]$Uri, [int]$TimeoutSeconds = 3) {
    try { return Invoke-RestMethod -Uri $Uri -Method Get -TimeoutSec $TimeoutSeconds -UseBasicParsing }
    catch { return $null }
}

function Wait-ForCondition {
    param(
        [scriptblock]$Condition,
        [int]$TimeoutSeconds,
        [string]$Description,
        [System.Diagnostics.Process]$Process = $null
    )
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $Condition) { return }
        if ($null -ne $Process) {
            $Process.Refresh()
            if ($Process.HasExited) { Fail "$Description hazir olmadan kapandi (cikis kodu: $($Process.ExitCode)). Loglari kontrol edin: $LogRoot" }
        }
        Start-Sleep -Seconds 2
    }
    Fail "$Description $TimeoutSeconds saniye icinde hazir olmadi. Loglari kontrol edin: $LogRoot"
}

function Test-DockerReady([string]$DockerExe) {
    $process = $null
    try {
        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = $DockerExe
        $startInfo.Arguments = 'info'
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $process = New-Object System.Diagnostics.Process
        $process.StartInfo = $startInfo
        $process.EnableRaisingEvents = $false
        if (-not $process.Start()) { return $false }
        $process.BeginOutputReadLine()
        $process.BeginErrorReadLine()
        if (-not $process.WaitForExit(5000)) {
            try { $process.Kill() } catch {}
            return $false
        }
        return ($process.ExitCode -eq 0)
    }
    catch { return $false }
    finally {
        if ($null -ne $process) { $process.Dispose() }
    }
}

function Find-DockerDesktop {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) { $candidates += (Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe') }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates += (Join-Path $env:LOCALAPPDATA 'Docker\Docker Desktop.exe')
        $candidates += (Join-Path $env:LOCALAPPDATA 'Programs\DockerDesktop\Docker Desktop.exe')
        $candidates += (Join-Path $env:LOCALAPPDATA 'Programs\Docker\Docker\Docker Desktop.exe')
    }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    return $null
}

function Get-ContainerEnvironmentValue([string]$DockerExe, [string]$VariableName) {
    $lines = & $DockerExe inspect --format '{{range .Config.Env}}{{println .}}{{end}}' yaztek-postgres 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    $prefix = $VariableName + '='
    foreach ($line in @($lines)) {
        $text = [string]$line
        if ($text.StartsWith($prefix, [StringComparison]::Ordinal)) {
            return $text.Substring($prefix.Length)
        }
    }
    return $null
}

function Ensure-PostgreSql([string]$DockerExe) {
    Write-Step 'PostgreSQL ve Docker kontrol ediliyor...'
    if (-not (Test-DockerReady $DockerExe)) {
        $desktop = Find-DockerDesktop
        Write-Step 'Docker Desktop baslatiliyor...'
        $script:State.DockerDesktopStartedByLauncher = $true
        Save-State
        $desktopCli = Start-OwnedProcess -Service 'DockerDesktopCli' -FilePath $DockerExe -ArgumentList @('desktop', 'start') -WorkingDirectory $ProjectRoot
        Start-Sleep -Seconds 3
        $desktopCli.Refresh()
        if ($desktopCli.HasExited -and $desktopCli.ExitCode -ne 0) {
            if ($null -eq $desktop) { Fail 'Docker motoruna ulasilamadi ve Docker Desktop baslatma komutu basarisiz oldu. Docker Desktop uygulamasini kurun veya elle baslatin.' }
            Start-Process -FilePath $desktop -WindowStyle Hidden | Out-Null
        }
        Wait-ForCondition -Condition { Test-DockerReady $DockerExe } -TimeoutSeconds 120 -Description 'Docker Desktop'
    }
    $status = (& $DockerExe inspect --format '{{.State.Status}}' yaztek-postgres 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($status -join ''))) {
        Fail "Mevcut 'yaztek-postgres' container'i bulunamadi. Veri kaybini onlemek icin baslatici yeni container olusturmaz. Once PostgreSQL container'ini olusturun."
    }
    $statusText = (($status -join '')).Trim()
    if ($statusText -ne 'running') {
        Write-Step "yaztek-postgres container'i baslatiliyor..."
        & $DockerExe start yaztek-postgres | Out-Null
        if ($LASTEXITCODE -ne 0) { Fail "yaztek-postgres container'i baslatilamadi." }
        $script:State.PostgreSqlStartedByLauncher = $true
        Save-State
    }
    $detectedUser = Get-ContainerEnvironmentValue $DockerExe 'POSTGRES_USER'
    $detectedDatabase = Get-ContainerEnvironmentValue $DockerExe 'POSTGRES_DB'
    if (-not [string]::IsNullOrWhiteSpace($detectedUser)) { $script:PostgresUser = $detectedUser }
    if (-not [string]::IsNullOrWhiteSpace($detectedDatabase)) { $script:PostgresDatabase = $detectedDatabase }
    Wait-ForCondition -Condition {
        & $DockerExe exec yaztek-postgres pg_isready -U $script:PostgresUser -d $script:PostgresDatabase *> $null
        return ($LASTEXITCODE -eq 0)
    } -TimeoutSeconds 60 -Description 'PostgreSQL'
    $portMapping = (& $DockerExe port yaztek-postgres 5432/tcp 2>$null) -join ' '
    if ($LASTEXITCODE -ne 0 -or $portMapping -notmatch ':5432\b') {
        Fail "yaztek-postgres calisiyor ancak container'in 5432 portu ana makinenin 5432 portuna bagli degil. Mevcut container'i silmeden port ayarini duzeltin."
    }
    if (-not (Test-TcpPort '127.0.0.1' 5432)) { Fail 'PostgreSQL hazir gorunuyor ancak 127.0.0.1:5432 portuna ulasilamiyor.' }
    Write-Ok 'PostgreSQL hazir; mevcut container ve veriler korunuyor.'
}

function Get-OllamaHealth {
    $result = Get-Json 'http://127.0.0.1:11434/api/tags' 3
    if ($null -eq $result -or $null -eq $result.models) { return $null }
    return $result
}

function Ensure-Ollama([string]$OllamaExe) {
    Write-Step 'Ollama kontrol ediliyor...'
    $health = Get-OllamaHealth
    $process = $null
    if ($null -eq $health) {
        if (Test-TcpPort '127.0.0.1' 11434) { Fail '11434 portu kullanimda fakat Ollama API yaniti alinamadi. Portu kullanan uygulamayi kendiniz kontrol edin; baslatici otomatik kapatmaz.' }
        Write-Step 'Ollama servisi baslatiliyor...'
        $process = Start-OwnedProcess -Service 'Ollama' -FilePath $OllamaExe -ArgumentList @('serve') -WorkingDirectory $ProjectRoot
        Wait-ForCondition -Condition { $null -ne (Get-OllamaHealth) } -TimeoutSeconds 60 -Description 'Ollama' -Process $process
        $health = Get-OllamaHealth
    }
    $modelFound = $false
    foreach ($model in @($health.models)) {
        if ($model.name -eq 'qwen3:4b' -or $model.model -eq 'qwen3:4b') { $modelFound = $true }
    }
    if (-not $modelFound) { Fail "Ollama hazir ancak qwen3:4b modeli yuklu degil. Baslatici buyuk model indirmez. Bir kez 'ollama pull qwen3:4b' komutunu elle calistirin." }
    Write-Ok 'Ollama ve qwen3:4b modeli hazir.'
}

function ConvertTo-PlainText([Security.SecureString]$SecureValue) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Get-BackendConnectionString {
    $configured = [Environment]::GetEnvironmentVariable('ConnectionStrings__PostgreSql', 'Process')
    if (-not [string]::IsNullOrWhiteSpace($configured)) {
        Write-Ok 'PostgreSQL baglanti bilgisi mevcut ortam degiskeninden alindi.'
        return $configured
    }
    $securePassword = $null
    $environmentPassword = [Environment]::GetEnvironmentVariable('YAZTEK_DB_PASSWORD', 'Process')
    if (-not [string]::IsNullOrWhiteSpace($environmentPassword)) {
        $securePassword = ConvertTo-SecureString $environmentPassword -AsPlainText -Force
        $environmentPassword = $null
        Write-Ok 'PostgreSQL parolasi YAZTEK_DB_PASSWORD ortam degiskeninden alindi.'
    }
    elseif (Test-Path -LiteralPath $CredentialPath -PathType Leaf) {
        try {
            $securePassword = Import-Clixml -LiteralPath $CredentialPath
            Write-Ok 'PostgreSQL parolasi Windows kullanicisina bagli sifreli kasadan alindi.'
        }
        catch { Fail "Sifreli parola dosyasi okunamadi. $CredentialPath dosyasini silip baslaticiyi yeniden calistirin." }
    }
    else {
        Write-Host 'PostgreSQL parolasi ilk kez alinacak. Yazdiklariniz ekranda gorunmez.' -ForegroundColor Yellow
        $securePassword = Read-Host ("PostgreSQL {0} kullanicisinin parolasi" -f $script:PostgresUser) -AsSecureString
        if ($securePassword.Length -eq 0) { Fail 'PostgreSQL parolasi bos olamaz.' }
        $securePassword | Export-Clixml -LiteralPath $CredentialPath
        Write-Ok 'Parola DPAPI ile sifrelendi; yalnizca bu Windows kullanicisi bu bilgisayarda acabilir.'
    }
    $plainPassword = ConvertTo-PlainText $securePassword
    try {
        $escaped = $plainPassword.Replace('"', '""')
        return "Host=localhost;Port=5432;Database=$($script:PostgresDatabase);Username=$($script:PostgresUser);Password=`"$escaped`""
    }
    finally { $plainPassword = $null }
}

function Get-YaztekApiHealth {
    $result = Get-Json 'http://127.0.0.1:5080/api/health' 3
    if ($null -eq $result -or $result.application -ne 'YAZTEK' -or $result.status -ne 'ok') { return $null }
    return $result
}

function Ensure-Backend([string]$DotnetExe) {
    Write-Step 'ASP.NET Core backend kontrol ediliyor...'
    if ($null -ne (Get-YaztekApiHealth)) {
        Write-Ok 'Backend zaten calisiyor ve /api/health hazir.'
        return
    }
    if (Test-TcpPort '127.0.0.1' 5080) { Fail '5080 portu kullanimda fakat YAZTEK /api/health yaniti alinamadi. Portu kullanan uygulama otomatik kapatilmadi.' }
    $connectionString = Get-BackendConnectionString
    $environment = @{
        'ConnectionStrings__PostgreSql' = $connectionString
        'ASPNETCORE_ENVIRONMENT' = 'Development'
        'SpeechInference__AutoStartLocal' = 'false'
        'ComprehensionAi__Provider' = 'Ollama'
        'ComprehensionAi__BaseUrl' = 'http://127.0.0.1:11434/'
        'ComprehensionAi__Model' = 'qwen3:4b'
    }
    Write-Step 'Backend baslatiliyor...'
    $process = Start-OwnedProcess -Service 'Backend' -FilePath $DotnetExe -ArgumentList @('run', '--project', 'src\YAZTEK.Api\YAZTEK.Api.csproj', '--no-launch-profile', '--urls', 'http://127.0.0.1:5080') -WorkingDirectory (Join-Path $ProjectRoot 'backend') -Environment $environment
    $connectionString = $null
    $environment['ConnectionStrings__PostgreSql'] = $null
    Wait-ForCondition -Condition { $null -ne (Get-YaztekApiHealth) } -TimeoutSeconds 120 -Description 'ASP.NET Core backend' -Process $process
    Write-Ok 'Backend http://127.0.0.1:5080/api/health uzerinden hazir.'
}

function Get-InferenceHealth {
    $result = Get-Json 'http://127.0.0.1:8090/health' 5
    if ($null -eq $result) { return $null }
    return $result
}

function Ensure-Inference {
    Write-Step 'Python ses analizi servisi kontrol ediliyor...'
    $health = Get-InferenceHealth
    $process = $null
    if ($null -ne $health -and $health.ready -eq $true) {
        Write-Ok "Ses analizi zaten hazir (model: $($health.modelId))."
        return
    }
    if (Test-TcpPort '127.0.0.1' 8090) {
        if ($null -ne $health) {
            $detail = $health.modelError
            if ([string]::IsNullOrWhiteSpace($detail)) { $detail = 'model hazir degil' }
            Fail "8090 portundaki ses servisi calisiyor ancak hazir degil: $detail"
        }
        Fail '8090 portu kullanimda fakat YAZTEK ses servisi yaniti alinamadi. Portu kullanan uygulama otomatik kapatilmadi.'
    }
    try {
        & $InferencePython -c 'import uvicorn, fastapi, torch, transformers' *> $null
        if ($LASTEXITCODE -ne 0) { Fail 'Python ses analizi bagimliliklari eksik.' }
    }
    catch { Fail "Python ses analizi bagimliliklari eksik. 'inference\.venv\Scripts\python.exe -m pip install -r inference\requirements.txt' komutunu elle calistirin." }
    $environment = @{
        'YAZTEK_MODEL_LOCAL_ONLY' = 'true'
        'HF_HUB_OFFLINE' = '1'
        'TRANSFORMERS_OFFLINE' = '1'
        'PYTHONUNBUFFERED' = '1'
        'YAZTEK_TEMP_DIR' = (Join-Path $ProjectRoot 'inference\.runtime-temp')
    }
    Write-Step 'Ses analizi ve yerel model baslatiliyor; modelin bellege yuklenmesi zaman alabilir...'
    $process = Start-OwnedProcess -Service 'Inference' -FilePath $InferencePython -ArgumentList @('-m', 'uvicorn', 'app.main:app', '--host', '127.0.0.1', '--port', '8090') -WorkingDirectory (Join-Path $ProjectRoot 'inference') -Environment $environment
    Wait-ForCondition -Condition {
        $candidate = Get-InferenceHealth
        return ($null -ne $candidate -and $candidate.ready -eq $true)
    } -TimeoutSeconds 300 -Description 'Python ses analizi ve ASR modeli' -Process $process
    $health = Get-InferenceHealth
    Write-Ok "Ses analizi hazir (model: $($health.modelId), cihaz: $($health.device))."
}

function Test-YaztekFrontend {
    try {
        $response = Invoke-WebRequest -Uri 'http://127.0.0.1:5173' -TimeoutSec 3 -UseBasicParsing
        return ($response.StatusCode -eq 200 -and $response.Content -match 'YAZTEK')
    }
    catch { return $false }
}

function Ensure-Frontend([string]$NpmCommand) {
    Write-Step 'React frontend kontrol ediliyor...'
    if (Test-YaztekFrontend) {
        Write-Ok 'Frontend zaten http://127.0.0.1:5173 adresinde calisiyor.'
        return
    }
    if (Test-TcpPort '127.0.0.1' 5173) { Fail '5173 portu kullanimda fakat YAZTEK frontend yaniti alinamadi. Portu kullanan uygulama otomatik kapatilmadi.' }
    if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot 'frontend\node_modules') -PathType Container)) {
        Fail "Frontend bagimliliklari eksik. Once frontend klasorunde 'npm install' komutunu elle calistirin."
    }
    Write-Step 'React frontend baslatiliyor...'
    $process = Start-OwnedProcess -Service 'Frontend' -FilePath $NpmCommand -ArgumentList @('run', 'dev', '--', '--host', '127.0.0.1', '--port', '5173', '--strictPort') -WorkingDirectory (Join-Path $ProjectRoot 'frontend')
    Wait-ForCondition -Condition { Test-YaztekFrontend } -TimeoutSeconds 60 -Description 'React frontend' -Process $process
    Write-Ok 'Frontend http://127.0.0.1:5173 adresinde hazir.'
}

function Open-YaztekInChrome {
    if ($SkipBrowser) { return }
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) { $candidates += (Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe') }
    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) { $candidates += (Join-Path $programFilesX86 'Google\Chrome\Application\chrome.exe') }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { $candidates += (Join-Path $env:LOCALAPPDATA 'Google\Chrome\Application\chrome.exe') }
    $chrome = Get-Command chrome.exe -ErrorAction SilentlyContinue
    if ($null -ne $chrome) { $chromePath = $chrome.Source } else { $chromePath = $null }
    if ($null -eq $chromePath) {
        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $chromePath = $candidate; break }
        }
    }
    if ($null -eq $chromePath) {
        Write-Warn 'Google Chrome bulunamadi. Uygulamayi elle acin: http://localhost:5173'
        return
    }
    Start-Process -FilePath $chromePath -ArgumentList @('http://localhost:5173') | Out-Null
    Write-Ok 'YAZTEK Google Chrome ile acildi.'
}

try {
    Require-File $BackendProject 'Backend projesi'
    Require-File $InferencePython 'Python sanal ortami'
    Require-File $FrontendPackage 'Frontend package.json'
    if (-not (Test-Path -LiteralPath $RuntimeRoot)) { New-Item -ItemType Directory -Path $RuntimeRoot -Force | Out-Null }
    if (-not (Test-Path -LiteralPath $LogRoot)) { New-Item -ItemType Directory -Path $LogRoot -Force | Out-Null }
    $script:State = Load-State
    $docker = Require-Command 'docker.exe' 'Docker Desktop kurulumunu kontrol edin.'
    $ollama = Require-Command 'ollama.exe' 'Ollama kurulumunu kontrol edin.'
    $dotnet = Require-Command 'dotnet.exe' '.NET 10 SDK kurulumunu kontrol edin.'
    $npm = Require-Command 'npm.cmd' 'Node.js ve npm kurulumunu kontrol edin.'
    if ($ValidateOnly) {
        Write-Ok 'Proje yollari ve temel calistiricilar bulundu.'
        if (Test-DockerReady $docker) { Write-Ok 'Docker motoru erisilebilir.' } else { Write-Warn 'Docker motoru su anda calismiyor.' }
        if ($null -ne (Get-OllamaHealth)) { Write-Ok 'Ollama API erisilebilir.' } else { Write-Warn 'Ollama API su anda erisilebilir degil.' }
        exit 0
    }
    Ensure-PostgreSql $docker
    Ensure-Ollama $ollama
    Ensure-Backend $dotnet
    Ensure-Inference
    Ensure-Frontend $npm
    Open-YaztekInChrome
    Write-Host ''
    Write-Host 'Tum YAZTEK servisleri hazir.' -ForegroundColor Green
    Write-Host "Durdurmak icin YAZTEK-DURDUR.bat dosyasina cift tiklayin. Loglar: $LogRoot"
    exit 0
}
catch {
    Write-Host ''
    Write-Host ('YAZTEK baslatilamadi: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host "Calisan servisler zorla kapatilmadi. Baslaticinin actiklarini durdurmak icin YAZTEK-DURDUR.bat kullanabilirsiniz."
    exit 1
}
