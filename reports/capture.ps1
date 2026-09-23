param(
    [string]$OutFile = "reports\ui-demo.png",
    [int]$CropX = 0, [int]$CropY = 0, [int]$CropW = 0, [int]$CropH = 0,
    [switch]$NoCrop
)
Add-Type -AssemblyName System.Drawing
$proc = Get-Process DBI.Controller.Studio -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output "NO PROCESS"; exit 1 }

if (-not ('W32Shot' -as [type])) {
Add-Type '
using System;
using System.Runtime.InteropServices;
public class W32Shot {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
'
}
$rect = New-Object W32Shot+RECT
[W32Shot]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
$full = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($full)
$hdc = $g.GetHdc()
[W32Shot]::PrintWindow($proc.MainWindowHandle, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc)

if (-not $NoCrop -and $CropW -gt 0 -and $CropH -gt 0) {
    $crop = New-Object System.Drawing.Bitmap $CropW, $CropH
    $gc = [System.Drawing.Graphics]::FromImage($crop)
    $destRect = New-Object System.Drawing.Rectangle 0, 0, $CropW, $CropH
    $srcRect = New-Object System.Drawing.Rectangle $CropX, $CropY, $CropW, $CropH
    $gc.DrawImage($full, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $crop.Save($OutFile)
} else {
    $full.Save($OutFile)
}
Write-Output "saved $OutFile (window ${w}x${h})"
