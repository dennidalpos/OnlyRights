param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "internal\common.ps1")

$context = Get-RepositoryContext -ScriptRoot $PSScriptRoot
Invoke-DotNetCommand -Arguments @("run", "--project", $context.AppProject, "-f", "net8.0-windows") -ErrorMessage "App start failed."
