$base    = [System.IO.Path]::GetDirectoryName($MyInvocation.MyCommand.Path)
. (Join-Path $base "..\Shared\BuildTpm.Common.ps1")

Build-AbrTpm -Base $base -TpmName "DemLoader" `
    -DllPath "bin\Debug\net48\Abr.DemLoader.dll" `
    -PluginFiles @("DemLoader.plugin", "t_dem_loader_tab.plugin") `
    -NeedsSetupExe
