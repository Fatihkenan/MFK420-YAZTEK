[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RuntimeRoot = Join-Path $ProjectRoot '.yaztek-launcher'
$StatePath = Join-Path $RuntimeRoot 'state.json'
$script:HadError = $false

function Write-Ok([string]$Message) {
    Write-Host ('[  OK  ] ' + $Message) -ForegroundColor Green
}

function Write-Warn([string]$Message) {
    Write-Host ('[ UYARI] ' + $Message) -ForegroundColor Yellow
}

function Stop-VerifiedProcessTree([int]$RootProcessId) {
    $queue = New-Object System.Collections.Generic.Queue[int]
    $descendants = New-Object System.Collections.Generic.List[int]
    $queue.Enqueue($RootProcessId)
    while ($queue.Count -gt 0) {
        $parentId = $queue.Dequeue()
        try {
            $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $parentId" -ErrorAction Stop)
        }
        catch {
            # Kisıtlı ortamlarda CIM kapali olabilir. Dogrulanmis ana surec
            # yine durdurulur; normal Windows oturumunda alt surecler de bulunur.
            $children = @()
        }
        foreach ($child in $children) {
            $childId = [int]$child.ProcessId
            $descendants.Add($childId)
            $queue.Enqueue($childId)
        }
    }
    for ($index = $descendants.Count - 1; $index -ge 0; $index--) {
        Stop-Process -Id $descendants[$index] -Force -ErrorAction SilentlyContinue
    }
    Stop-Process -Id $RootProcessId -Force -ErrorAction Stop
}

function Stop-OwnedProcess($Entry) {
    $process = Get-Process -Id ([int]$Entry.Id) -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        Write-Ok "$($Entry.Service) zaten kapali."
        return $true
    }
    try {
        $actualTicks = $process.StartTime.ToUniversalTime().Ticks
        if ($process.ProcessName -ne [string]$Entry.Name -or $actualTicks -ne [long]$Entry.StartTimeUtcTicks) {
            Write-Warn "$($Entry.Service) PID'si baska bir surece ait; guvenlik icin dokunulmadi."
            return $true
        }
        Stop-VerifiedProcessTree -RootProcessId $process.Id
        Write-Ok "$($Entry.Service) durduruldu."
        return $true
    }
    catch {
        Write-Warn "$($Entry.Service) durdurulurken hata olustu: $($_.Exception.Message)"
        return $false
    }
}

try {
    if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) {
        Write-Warn 'Baslaticiya ait calisan surec kaydi bulunamadi. Mevcut servislere dokunulmadi.'
        exit 0
    }

    $state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
    $remaining = @()
    $orderedProcesses = @($state.Processes) | Sort-Object {
        if ($_.Service -eq 'Frontend') { 0 }
        elseif ($_.Service -eq 'Inference') { 1 }
        elseif ($_.Service -eq 'Backend') { 2 }
        else { 3 }
    }
    foreach ($entry in $orderedProcesses) {
        if (-not (Stop-OwnedProcess $entry)) {
            $remaining += $entry
            $script:HadError = $true
        }
    }

    $postgresStillOwned = $false
    if ($state.PostgreSqlStartedByLauncher -eq $true) {
        $docker = Get-Command docker.exe -ErrorAction SilentlyContinue
        if ($null -eq $docker) {
            Write-Warn "Docker bulunamadi; baslaticinin actigi yaztek-postgres durdurulamadi."
            $postgresStillOwned = $true
            $script:HadError = $true
        }
        else {
            $status = (& $docker.Source inspect --format '{{.State.Status}}' yaztek-postgres 2>$null) -join ''
            if ($LASTEXITCODE -eq 0 -and $status.Trim() -eq 'running') {
                & $docker.Source stop yaztek-postgres *> $null
                if ($LASTEXITCODE -eq 0) {
                    Write-Ok 'Baslatici tarafindan acilan yaztek-postgres durduruldu; container ve veriler silinmedi.'
                }
                else {
                    Write-Warn 'yaztek-postgres durdurulamadi.'
                    $postgresStillOwned = $true
                    $script:HadError = $true
                }
            }
            else {
                Write-Ok 'yaztek-postgres zaten kapali veya artik mevcut degil.'
            }
        }
    }
    else {
        Write-Ok 'PostgreSQL baslatici tarafindan acilmadigi icin calisma durumuna dokunulmadi.'
    }

    $dockerDesktopStillOwned = $false
    $dockerDesktopWasOwned = ($state.PSObject.Properties.Name -contains 'DockerDesktopStartedByLauncher') -and ($state.DockerDesktopStartedByLauncher -eq $true)
    if ($dockerDesktopWasOwned) {
        $docker = Get-Command docker.exe -ErrorAction SilentlyContinue
        if ($null -eq $docker) {
            Write-Warn 'Docker CLI bulunamadi; baslaticinin actigi Docker Desktop durdurulamadi.'
            $dockerDesktopStillOwned = $true
            $script:HadError = $true
        }
        else {
            $desktopStop = Start-Process -FilePath $docker.Source -ArgumentList @('desktop', 'stop') -WindowStyle Hidden -PassThru
            if (-not $desktopStop.WaitForExit(60000)) {
                Stop-Process -Id $desktopStop.Id -Force -ErrorAction SilentlyContinue
                Write-Warn 'Docker Desktop 60 saniye icinde durmadi.'
                $dockerDesktopStillOwned = $true
                $script:HadError = $true
            }
            elseif ($desktopStop.ExitCode -ne 0) {
                Write-Warn 'Docker Desktop durdurma komutu basarisiz oldu.'
                $dockerDesktopStillOwned = $true
                $script:HadError = $true
            }
            else {
                Write-Ok 'Baslatici tarafindan acilan Docker Desktop durduruldu.'
            }
        }
    }
    else {
        Write-Ok 'Docker Desktop baslatici tarafindan acilmadigi icin calisma durumuna dokunulmadi.'
    }

    if ($remaining.Count -eq 0 -and -not $postgresStillOwned -and -not $dockerDesktopStillOwned) {
        Remove-Item -LiteralPath $StatePath -Force
    }
    else {
        $state.Processes = $remaining
        $state.PostgreSqlStartedByLauncher = $postgresStillOwned
        $state.DockerDesktopStartedByLauncher = $dockerDesktopStillOwned
        $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $StatePath -Encoding UTF8
    }

    Write-Host ''
    Write-Host 'Durdurma tamamlandi. Daha once calisan harici servisler kapatilmadi.' -ForegroundColor Cyan
    if ($script:HadError) { exit 1 }
    exit 0
}
catch {
    Write-Host ('YAZTEK servisleri durdurulamadi: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
