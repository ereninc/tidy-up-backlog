Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class CozyUnityFocus {
    public delegate bool EnumWindowCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
}
'@
$unityShell = New-Object -ComObject WScript.Shell
$targetWindow = (Get-Process -Id 12072).MainWindowHandle
$currentThread = [CozyUnityFocus]::GetCurrentThreadId()
$foregroundPid = [uint32]0
$foregroundThread = [CozyUnityFocus]::GetWindowThreadProcessId([CozyUnityFocus]::GetForegroundWindow(), [ref]$foregroundPid)
$attached = [CozyUnityFocus]::AttachThreadInput($currentThread, $foregroundThread, $true)
try {
    [void][CozyUnityFocus]::ShowWindow($targetWindow, 9)
    [void][CozyUnityFocus]::SetForegroundWindow($targetWindow)
} finally {
    if ($attached) { [void][CozyUnityFocus]::AttachThreadInput($currentThread, $foregroundThread, $false) }
}
Start-Sleep -Milliseconds 500
$foregroundProcess = [uint32]0
[void][CozyUnityFocus]::GetWindowThreadProcessId([CozyUnityFocus]::GetForegroundWindow(), [ref]$foregroundProcess)
if ($foregroundProcess -ne 12072) { throw "Target Unity Editor is not foreground: $foregroundProcess" }
$unityShell.SendKeys('^r')
Write-Output 'Sent Assets/Refresh to the verified project Editor.'
