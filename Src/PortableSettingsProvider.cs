// Хранилище настроек по фиксированному пути.
//
// Штатный LocalFileSettingsProvider складывает user.config в
// %LocalAppData%\<AssemblyCompany>\<имя exe>_<хеш пути к exe>\<AssemblyVersion>\.
// То есть путь зависит сразу от четырёх вещей, и любая из них, изменившись,
// уводит настройки в пустую папку — для пользователя это неотличимо от сброса.
// За одну сессию сюда наступили трижды: подняли версию, поправили строку
// компании, запустили exe из другой папки.
//
// Здесь путь один и тот же всегда: %AppData%\NoteCalque\settings.xml.
//
// Сам класс настроек и весь код, который к нему обращается, не меняются:
// провайдер подставляется атрибутом SettingsProvider и отвечает только за то,
// где лежит файл и как он выглядит.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Text;
using System.Xml;

namespace NoteCalque
{
  internal sealed class PortableSettingsProvider : SettingsProvider, IApplicationSettingsProvider
  {
    private const string AppFolder = "NoteCalque";
    private const string FileName = "settings.xml";
    private const string RootName = "settings";
    private const string ItemName = "setting";
    private const string NameAttribute = "name";

    public override string ApplicationName { get; set; }

    public override void Initialize(string name, NameValueCollection config)
    {
      base.Initialize(string.IsNullOrEmpty(name) ? typeof (PortableSettingsProvider).Name : name, config);
    }

    public static string Folder
    {
      get
      {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder);
      }
    }

    public static string FilePath
    {
      get { return Path.Combine(PortableSettingsProvider.Folder, FileName); }
    }

    public override SettingsPropertyValueCollection GetPropertyValues(
      SettingsContext context,
      SettingsPropertyCollection collection)
    {
      Dictionary<string, string> stored = PortableSettingsProvider.Read(PortableSettingsProvider.FilePath);
      SettingsPropertyValueCollection values = new SettingsPropertyValueCollection();
      foreach (SettingsProperty property in collection)
      {
        SettingsPropertyValue value = new SettingsPropertyValue(property);
        string text;
        // Чего нет в файле — берём из DefaultSettingValue, как и штатный провайдер.
        value.SerializedValue = stored.TryGetValue(property.Name, out text) ? (object) text : property.DefaultValue;
        value.IsDirty = false;
        values.Add(value);
      }
      return values;
    }

    public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection)
    {
      Dictionary<string, string> stored = PortableSettingsProvider.Read(PortableSettingsProvider.FilePath);
      foreach (SettingsPropertyValue value in collection)
      {
        object serialized = value.SerializedValue;
        stored[value.Name] = serialized == null ? "" : serialized.ToString();
      }
      PortableSettingsProvider.Write(PortableSettingsProvider.FilePath, stored);
    }

    // Переносить неоткуда и незачем: путь один на все сборки. Метод есть
    // только потому, что его требует IApplicationSettingsProvider.
    public void Upgrade(SettingsContext context, SettingsPropertyCollection properties)
    {
    }

    public void Reset(SettingsContext context)
    {
      try
      {
        if (File.Exists(PortableSettingsProvider.FilePath))
          File.Delete(PortableSettingsProvider.FilePath);
      }
      catch (Exception)
      {
      }
    }

    public SettingsPropertyValue GetPreviousVersion(SettingsContext context, SettingsProperty property)
    {
      // Версий у нашего хранилища нет — путь один на все сборки.
      return new SettingsPropertyValue(property) { PropertyValue = (object) null };
    }

    private static Dictionary<string, string> Read(string path)
    {
      Dictionary<string, string> values = new Dictionary<string, string>();
      if (!File.Exists(path))
        return values;
      try
      {
        XmlDocument document = new XmlDocument();
        document.Load(path);
        if (document.DocumentElement == null)
          return values;
        foreach (XmlNode node in document.DocumentElement.ChildNodes)
        {
          XmlElement element = node as XmlElement;
          if (element != null && element.HasAttribute(NameAttribute))
            values[element.GetAttribute(NameAttribute)] = element.InnerText;
        }
      }
      catch (Exception)
      {
        // Файл испорчен — уходим на значения по умолчанию, а не падаем.
        values.Clear();
      }
      return values;
    }

    private static void Write(string path, Dictionary<string, string> values)
    {
      try
      {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        XmlWriterSettings settings = new XmlWriterSettings();
        settings.Indent = true;
        settings.Encoding = (Encoding) new UTF8Encoding(false);
        // Иначе возврат каретки в многострочном тексте нормализуется при
        // чтении и переносы строк в поле ввода поедут.
        settings.NewLineHandling = NewLineHandling.Entitize;
        using (XmlWriter writer = XmlWriter.Create(path, settings))
        {
          writer.WriteStartDocument();
          writer.WriteStartElement(RootName);
          foreach (KeyValuePair<string, string> pair in values)
          {
            writer.WriteStartElement(ItemName);
            writer.WriteAttributeString(NameAttribute, pair.Key);
            writer.WriteString(pair.Value);
            writer.WriteEndElement();
          }
          writer.WriteEndElement();
          writer.WriteEndDocument();
        }
      }
      catch (Exception)
      {
        // Нет прав на запись или диск занят — потеря настроек не повод падать.
      }
    }
  }
}
