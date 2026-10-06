// Настройки приложения.
//
// Провайдер подставлен атрибутом и кладёт файл по фиксированному пути
// %AppData%\NoteCalque\settings.xml. Почему не штатный — в заголовке
// PortableSettingsProvider.cs; коротко: штатный строит путь из версии сборки,
// имени компании и места запуска exe, и любое из этого, изменившись, выглядит
// для пользователя как сброс настроек.

using System.Configuration;
using System.Drawing;
using System.Windows.Forms;

namespace NoteCalque
{
  [SettingsProvider(typeof (PortableSettingsProvider))]
  internal sealed class Settings : ApplicationSettingsBase
  {
    private static readonly Settings instance =
      (Settings) ApplicationSettingsBase.Synchronized((ApplicationSettingsBase) new Settings());

    public static Settings Default
    {
      get { return Settings.instance; }
    }

    // Содержимое блокнота. Главное, чего не умел аналог из магазина.
    [UserScopedSetting]
    [DefaultSettingValue("")]
    public string Text
    {
      get { return (string) this["Text"]; }
      set { this["Text"] = value; }
    }

    [UserScopedSetting]
    [DefaultSettingValue("100, 100")]
    public Point WindowLocation
    {
      get { return (Point) this["WindowLocation"]; }
      set { this["WindowLocation"] = value; }
    }

    [UserScopedSetting]
    [DefaultSettingValue("760, 520")]
    public Size WindowSize
    {
      get { return (Size) this["WindowSize"]; }
      set { this["WindowSize"] = value; }
    }

    [UserScopedSetting]
    [DefaultSettingValue("Normal")]
    public FormWindowState WindowState
    {
      get { return (FormWindowState) this["WindowState"]; }
      set { this["WindowState"] = value; }
    }

    // «Сворачивать при запуске» — нужно для автозагрузки, чтобы окно не лезло
    // на экран при входе в систему.
    [UserScopedSetting]
    [DefaultSettingValue("False")]
    public bool StartMinimized
    {
      get { return (bool) this["StartMinimized"]; }
      set { this["StartMinimized"] = value; }
    }

    // «Активировать NumLock при вызове». Заодно включает починку рассинхрона
    // состояния клавиатуры: чинить имеет смысл только там, где NumLock и так
    // приводится в известное состояние.
    [UserScopedSetting]
    [DefaultSettingValue("True")]
    public bool NumLockActivate
    {
      get { return (bool) this["NumLockActivate"]; }
      set { this["NumLockActivate"] = value; }
    }

    [UserScopedSetting]
    [DefaultSettingValue("False")]
    public bool AlwaysOnTop
    {
      get { return (bool) this["AlwaysOnTop"]; }
      set { this["AlwaysOnTop"] = value; }
    }


    // "system", "light" или "dark". Системная — не отсутствие темы, а согласие
    // с настройкой Windows: страница следит за ней сама через prefers-color-scheme.
    [UserScopedSetting]
    [DefaultSettingValue("system")]
    public string Theme
    {
      get { return (string) this["Theme"]; }
      set { this["Theme"] = value; }
    }

    [UserScopedSetting]
    [DefaultSettingValue("Consolas")]
    public string FontFamily
    {
      get { return (string) this["FontFamily"]; }
      set { this["FontFamily"] = value; }
    }

    // В пунктах, как в диалоге выбора шрифта.
    [UserScopedSetting]
    [DefaultSettingValue("11")]
    public float FontSize
    {
      get { return (float) this["FontSize"]; }
      set { this["FontSize"] = value; }
    }

    // Начертание выбранного шрифта. Страница своей жирности не добавляет,
    // поэтому полужирным текст делается только отсюда.
    [UserScopedSetting]
    [DefaultSettingValue("False")]
    public bool FontBold
    {
      get { return (bool) this["FontBold"]; }
      set { this["FontBold"] = value; }
    }

    [UserScopedSetting]
    [DefaultSettingValue("False")]
    public bool FontItalic
    {
      get { return (bool) this["FontItalic"]; }
      set { this["FontItalic"] = value; }
    }

    // Значащих цифр в результате.
    [UserScopedSetting]
    [DefaultSettingValue("12")]
    public int Precision
    {
      get { return (int) this["Precision"]; }
      set { this["Precision"] = value; }
    }
  }
}
