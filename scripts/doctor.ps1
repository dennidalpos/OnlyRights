param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "helpers\common.ps1")

$context = Get-RepositoryContext -ScriptRoot $PSScriptRoot
Assert-RepositoryPrerequisites -Context $context

$requiredSdkVersion = Get-RequiredSdkVersion -Context $context
$sdkInstalled = Test-SdkFeatureBandInstalled -RequiredVersion $requiredSdkVersion
$currentDotnetVersion = (& dotnet --version).Trim()

Write-Host "[NtfsAudit] Doctor summary" -ForegroundColor Cyan
Write-Host ("  OS: Windows")
Write-Host ("  dotnet --version: {0}" -f $currentDotnetVersion)
Write-Host ("  global.json sdk.version: {0}" -f $requiredSdkVersion)
Write-Host ("  SDK feature band available: {0}" -f $sdkInstalled)
Write-Host ("  WiX local toolchain: {0}" -f (Test-Path $context.WixTools))
Write-Host ("  NSSM fallback toolchain: {0}" -f (Test-Path $context.NssmTools))

if (-not $sdkInstalled) {
    throw ("Required SDK feature band not installed for global.json version {0}." -f $requiredSdkVersion)
}
