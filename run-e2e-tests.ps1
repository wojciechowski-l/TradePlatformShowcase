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
        docker logs trade-api-e2e --tail 20
        exit 1
    }

    Write-Information "Applying database migrations"
    Set-Location TradePlatform.Api

    $connectionString = "Server=127.0.0.1,1435;Database=TradePlatformDb;User Id=sa;Password=Password123!;TrustServerCertificate=True;"
    dotnet ef database update --connection "$connectionString"

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Database migration failed"
        exit 1
    }

    Write-Information "Running Cypress tests"
    Set-Location "$rootPath/Client"
    $env:CYPRESS_baseUrl = "http://localhost:3001"

    npx cypress run --spec "cypress/e2e/trade_flow.cy.ts"

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Cypress tests failed"
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
        docker logs trade-api-e2e --tail 50
    }

    docker compose -f docker-compose.test.yml down
}
