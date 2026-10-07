[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$mgIds = @()
if (-not [string]::IsNullOrWhiteSpace($env:AQR_MANAGEMENT_GROUP_IDS)) {
    $mgIds = $env:AQR_MANAGEMENT_GROUP_IDS.Split(',', [System.StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() } | Where-Object { $_ }
}

if ($mgIds.Count -gt 0) {
    if ([string]::IsNullOrWhiteSpace($env:UAMI_PRINCIPAL_ID)) {
        Write-Warning 'UAMI_PRINCIPAL_ID is not available; cannot print management-group RBAC commands.'
    }
    else {
        foreach ($mg in $mgIds) {
            $cmd = "az role assignment create --assignee-object-id $env:UAMI_PRINCIPAL_ID --assignee-principal-type ServicePrincipal --role Reader --scope /providers/Microsoft.Management/managementGroups/$mg"
            if ($env:AQR_ASSIGN_MG_RBAC -eq 'true') {
                Write-Host "Executing: $cmd"
                az role assignment create --assignee-object-id $env:UAMI_PRINCIPAL_ID --assignee-principal-type ServicePrincipal --role Reader --scope "/providers/Microsoft.Management/managementGroups/$mg" | Out-Host
            }
            else {
                Write-Host $cmd
            }
        }
    }
}
else {
    Write-Warning 'AQR_MANAGEMENT_GROUP_IDS is empty; no management-group Reader assignment commands to print.'
}

Write-Host 'Ensure Microsoft.Quota and Microsoft.Compute resource providers are registered in each member subscription.'
if (-not [string]::IsNullOrWhiteSpace($env:WEB_APP_URL)) {
    Write-Host "WEB_APP_URL: $env:WEB_APP_URL"
}
