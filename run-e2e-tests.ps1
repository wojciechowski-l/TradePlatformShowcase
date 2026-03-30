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

    # The API image does not have curl/wget so we cannot use a Docker healthcheck for it.
    # Poll the health endpoint from the host instead (port 8081 is exposed).
    Write-Information "Waiting for API readiness"
    $retryCount = 0
    $maxRetries = 45
    $apiReady = $false

    while (-not $apiReady -and $retryCount -lt $maxRetries) {
        $retryCount++
        try {
            $response = Invoke-WebRequest `
                -Uri "http://127.0.0.1:8081/health" `
                -Method Head `
                -ErrorAction Stop `
                -UseBasicParsing

            if ($response.StatusCode -eq 200) {
                $apiReady = $true
                Write-Information "API is online"
            }
        } catch {}

        if (-not $apiReady) {
            Write-Information "Waiting for API ($retryCount/$maxRetries)"
            Start-Sleep -Seconds 2
        }
    }

    if (-not $apiReady) {
        Write-Error "API startup timeout"
        docker logs trade-migrator-e2e --tail 20
        docker logs trade-api-e2e --tail 20
        exit 1
    }

    # Once the API is up, test-client will start and its healthcheck (wget-based) will pass.
    # test-playwright depends on test-client being healthy, so it starts automatically.
    Write-Information "Waiting for Playwright tests to complete..."
    do {
        Start-Sleep -Seconds 2
        $status = docker inspect -f '{{.State.Status}}' trade-playwright-e2e
    } until ($status -eq 'exited')

    $playwrightExit = docker inspect -f '{{.State.ExitCode}}' trade-playwright-e2e

    docker logs trade-playwright-e2e

    if ($playwrightExit -ne 0) {
        Write-Error "Playwright tests failed"
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