# Copy to office-pilot-settings.ps1 beside LAC.Api.exe. Do not commit the copy.
# Prefer protected machine environment variables for secrets; this template remains intentionally blank.
$env:ConnectionStrings__DefaultConnection = ''
$env:Storage__DocumentRoot = 'D:\Software Data\Documents'
$env:Storage__ExtractionRoot = 'D:\Software Data\Extraction'
$env:Storage__BackupRoot = 'D:\Software Data\DatabaseBackups'
$env:OnlyOffice__Enabled = 'true'
$env:OnlyOffice__BrowserUrl = 'http://<LAPTOP_LAN_IP>:8082'
$env:OnlyOffice__AppExternalUrl = 'http://<OFFICE_SERVER_LAN_IP>:5088'
$env:OnlyOffice__DocumentServerUrl = 'http://<LAPTOP_LAN_IP>:8082'
$env:OnlyOffice__JwtSecret = ''
