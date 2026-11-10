param([string]$AssemblyPath = "$PSScriptRoot\..\bin\Release\NoteCalque.exe")
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFile((Resolve-Path $AssemblyPath).Path)
$hookType = $assembly.GetType('NoteCalque.KeyboardHook', $true)
$hook = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($hookType)
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$seen = $hookType.GetField('hasObservedNumLock', $flags)
$time = $hookType.GetField('lastNumLockTime', $flags)
$observed = $hookType.GetMethod('HasObservedNumLock')

function Assert-Observed([uint32]$MessageTime, [bool]$Expected) {
  $actual = $observed.Invoke($hook, @($MessageTime))
  if ($actual -ne $Expected) {
    throw "HasObservedNumLock($MessageTime) returned $actual, expected $Expected"
  }
}

Assert-Observed 1000 $false # Hotkey fallback before the hook has seen input.
$seen.SetValue($hook, $true)
$time.SetValue($hook, [uint32]1500)
Assert-Observed 1000 $true # Delayed physical WM_HOTKEY.
Assert-Observed 1500 $true # Injected WM_HOTKEY, independent of processing delay.
Assert-Observed 1501 $false # New input when the hook stopped receiving events.
$time.SetValue($hook, [uint32]10)
Assert-Observed ([uint32]::MaxValue - 10) $true # Windows timestamp wraparound.
Assert-Observed 11 $false
# Avoid invoking the finalizer on an object created without its constructor.
[GC]::SuppressFinalize($hook)
Write-Output 'Hotkey routing tests passed'
