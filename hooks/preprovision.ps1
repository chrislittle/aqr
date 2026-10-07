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

if ([string]::IsNullOrWhiteSpace($env:AQR_MANAGEMENT_GROUP_IDS)) {
    Write-Warning 'AQR_MANAGEMENT_GROUP_IDS is empty. AQR will deploy, but sync has no management group scope until this is configured.'
}
