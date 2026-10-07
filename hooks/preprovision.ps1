[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:AZURE_SUBSCRIPTION_ID)) {
    throw 'AZURE_SUBSCRIPTION_ID is not set. Run azd env set AZURE_SUBSCRIPTION_ID <subscription-id>.'
}

$currentSubscriptionId = az account show --query id -o tsv 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($currentSubscriptionId)) {
    throw 'Unable to read the current Azure CLI account. Run az login and az account set --subscription <id>.'
}

if ($currentSubscriptionId.Trim() -ne $env:AZURE_SUBSCRIPTION_ID.Trim()) {
    Write-Host "Azure CLI subscription: $($currentSubscriptionId.Trim())"
    Write-Host "AZURE_SUBSCRIPTION_ID: $($env:AZURE_SUBSCRIPTION_ID.Trim())"
    throw 'Azure CLI subscription does not match AZURE_SUBSCRIPTION_ID. Run: az account set --subscription <id>'
}

if ([string]::IsNullOrWhiteSpace($env:AQR_MANAGEMENT_GROUP_IDS) -and [string]::IsNullOrWhiteSpace($env:AQR_SUBSCRIPTION_IDS)) {
    Write-Warning 'Neither AQR_MANAGEMENT_GROUP_IDS nor AQR_SUBSCRIPTION_IDS is set. AQR will deploy, but the sync refuses to run unscoped until one is configured.'
}
