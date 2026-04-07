$InformationPreference = "Continue"
$rootPath = Get-Location

Write-Information "STARTING E2E TEST SUITE"

try {
    Write-Information "Running backend tests"
    dotnet test TradePlatform.Tests/TradePlatform.Tests.csproj `
        --configuration Release `
        --nologo `
        --verbosity normal

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Backend tests failed"
        exit 1
    }

    Write-Information "Starting docker containers"
    docker compose -f docker-compose.test.yml up -d --build --remove-orphans

    function Wait-ForHttpEndpoint {
        param(
            [string]$Name,
            [string]$Uri,
            [int]$MaxRetries = 45,
            [int]$DelaySeconds = 2
        )

        Write-Information "Waiting for $Name readiness"

        $retryCount = 0
        while ($retryCount -lt $MaxRetries) {
            $retryCount++
            try {
                $response = Invoke-WebRequest `
                    -Uri $Uri `
                    -Method Get `
                    -ErrorAction Stop `
                    -UseBasicParsing

                if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                    Write-Information "$Name is online"
                    return
                }
            } catch {}

            Write-Information "Waiting for $Name ($retryCount/$MaxRetries)"
            Start-Sleep -Seconds $DelaySeconds
        }

        throw "$Name startup timeout"
    }

    function Wait-ForWorkerReady {
        param(
            [int]$MaxRetries = 60,
            [int]$DelaySeconds = 2
        )

        Write-Information "Waiting for Worker readiness"

        $retryCount = 0
        while ($retryCount -lt $MaxRetries) {
            $retryCount++

            $metricsReady = $false
            try {
                $response = Invoke-WebRequest `
                    -Uri "http://127.0.0.1:9091/metrics" `
                    -Method Get `
                    -ErrorAction Stop `
                    -UseBasicParsing

                if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                    $metricsReady = $true
                }
            } catch {}

            $workerLogs = docker logs trade-worker-e2e 2>&1 | Out-String
            $logReady = $workerLogs -match "Worker starting up"

            if ($metricsReady -or $logReady) {
                Write-Information "Worker is online"
                return
            }

            Write-Information "Waiting for Worker ($retryCount/$MaxRetries)"
            Start-Sleep -Seconds $DelaySeconds
        }

        throw "Worker startup timeout"
    }

    # The API image does not have curl/wget so we cannot use a Docker healthcheck for it.
    # Poll the host-exposed endpoints instead.
    Wait-ForHttpEndpoint -Name "API" -Uri "http://127.0.0.1:8081/health"
    Wait-ForWorkerReady -MaxRetries 60

    Write-Information "Waiting for Playwright tests to complete..."
    
    $playwrightExit = docker wait trade-playwright-e2e

    docker logs trade-playwright-e2e

    if ($playwrightExit -ne 0) {
        Write-Error "Playwright tests failed with exit code $playwrightExit"
        exit 1
    }

    Write-Information "ALL TESTS PASSED"
}
catch {
    Write-Error "FATAL ERROR: $_"
    Write-Error $_.Exception.Message
    exit 1
}
finally {
    Write-Information "Cleaning up"
    Set-Location $rootPath

    if ($LASTEXITCODE -ne 0) {
        Write-Information "--- MIGRATOR LOGS ---"
        docker logs trade-migrator-e2e --tail 50
        Write-Information "--- API LOGS ---"
        docker logs trade-api-e2e --tail 50
        Write-Information "--- WORKER LOGS ---"
        docker logs trade-worker-e2e --tail 50
    }

    docker compose -f docker-compose.test.yml down
}
