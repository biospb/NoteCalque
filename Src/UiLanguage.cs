using System.Globalization;

namespace NoteCalque
{
  internal static class UiLanguage
  {
    public static string Code
    {
      get { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en"; }
    }

    public static string Text(string russian, string english)
    {
      return Code == "ru" ? russian : english;
    }
  }
}
