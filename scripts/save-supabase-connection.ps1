<#
Reads a full Supabase PostgreSQL URI from the Windows clipboard and saves a
sanitized Npgsql connection string as a user-scoped environment variable.
The URI and password are never written to the console or this repository.
#>
[CmdletBinding()]
param()

$rawConnectionUri = Get-Clipboard -Raw

if ([string]::IsNullOrWhiteSpace($rawConnectionUri) -or $rawConnectionUri -notmatch '^postgres(ql)?://') {
    throw 'Clipboard does not contain a full postgresql:// connection URI. In Supabase, click Shared pooler > Copy all, then run this script without copying anything else.'
}

try {
    $uri = [Uri]$rawConnectionUri.Trim()
    $credentials = $uri.UserInfo.Split(':', 2)

    if ($credentials.Count -ne 2 -or [string]::IsNullOrWhiteSpace($uri.Host)) {
        throw 'The copied URI is missing its host, username, or password.'
    }

    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder['Host'] = $uri.Host
    $builder['Port'] = $uri.Port
    $builder['Database'] = $uri.AbsolutePath.TrimStart('/')
    $builder['Username'] = [Uri]::UnescapeDataString($credentials[0])
    $builder['Password'] = [Uri]::UnescapeDataString($credentials[1])
    $builder['Ssl Mode'] = 'Require'
    $builder['Trust Server Certificate'] = 'true'
    $builder['Pooling'] = 'false'

    [Environment]::SetEnvironmentVariable('ConnectionStrings__DefaultConnection', $builder.ConnectionString, 'User')
    Write-Host 'Saved securely. The password was not displayed.'
}
finally {
    $rawConnectionUri = $null
    $credentials = $null
}
