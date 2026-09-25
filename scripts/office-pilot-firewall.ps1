[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ApprovedSubnet,
    [ValidateSet('LacHost', 'OnlyOfficeLaptop')][string]$Role = 'LacHost'
)

$ErrorActionPreference = 'Stop'
$port = if ($Role -eq 'LacHost') { 5088 } else { 8082 }
$name = "LAC Office Pilot $Role TCP $port"
Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow -Protocol TCP -LocalPort $port `
    -RemoteAddress $ApprovedSubnet -Profile Private | Out-Null
Write-Host "Created Private-profile inbound rule for TCP $port from $ApprovedSubnet. PostgreSQL 5432 is not opened."
