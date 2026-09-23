Add-Type '
using System;
using System.Runtime.InteropServices;
public class W32q {
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
'
$proc = Get-Process DBI.Controller.Studio | Select-Object -First 1
[W32q]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null   # SW_RESTORE
Write-Output 'restored window'
