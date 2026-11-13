$ErrorActionPreference = 'Stop'
$type = Add-Type -Path "$PSScriptRoot\..\Src\UiLanguage.cs" -PassThru
$originalCulture = [Threading.Thread]::CurrentThread.CurrentUICulture
try {
  foreach ($name in @('ru-RU', 'ru-BY', 'ru', 'en-US', 'de-DE', 'ar-SA', '')) {
    [Threading.Thread]::CurrentThread.CurrentUICulture = [Globalization.CultureInfo]::GetCultureInfo($name)
    $expected = if ($name.StartsWith('ru')) { 'ru' } else { 'en' }
    $actual = $type.GetProperty('Code').GetValue($null, $null)
    if ($actual -ne $expected) { throw "Culture '$name' selected '$actual', expected '$expected'" }
    $text = $type.GetMethod('Text').Invoke($null, @('Russian', 'English'))
    $expectedText = if ($expected -eq 'ru') { 'Russian' } else { 'English' }
    if ($text -ne $expectedText) { throw "Incorrect translation for '$name'" }
  }
} finally {
  [Threading.Thread]::CurrentThread.CurrentUICulture = $originalCulture
}
Write-Output 'UI language tests passed'
