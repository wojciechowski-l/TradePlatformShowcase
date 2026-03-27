param(
    [string]$ApiBaseUrl = "http://localhost:8080",
    [Parameter(Mandatory = $true)]
    [string]$BearerToken
)

$uri = "$ApiBaseUrl/api/maintenance/projections/account-activity/rebuild"

Invoke-RestMethod `
    -Method Post `
    -Uri $uri `
    -Headers @{
        Authorization = "Bearer $BearerToken"
    }
