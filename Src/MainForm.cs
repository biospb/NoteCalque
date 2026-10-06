// Окно приложения.
//
// Всё, что связано с клавиатурой и окном, живёт здесь и в четырёх файлах,
// перенесённых из NoteCalc без изменений: Win32, HotKey, KeyboardHook,
// PortableSettingsProvider. Счётная часть и вёрстка — внутри WebView2,
// в Web\index.html и Web\calc.js.

using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NoteCalque
{
  internal sealed class MainForm : Form
  {
    // Страница отдаётся не по file://, а с собственного origin: под file://
    // движок считает страницу и её же скрипты разными источниками и запрещает
    // половину обычных вещей.
    private const string VirtualHost = "notecalque.local";

    // Одно нажатие NumLock приезжает сюда до трёх раз: от хука, от
    // RegisterHotKey и ещё раз от компенсирующей эмуляции. Дублирование
    // механизмов сделано нарочно (см. заголовок KeyboardHook.cs), а вот
    // показывать окно нужно один раз.
    private const int SummonGuardMs = 200;

    // Сколько окно должно пробыть спрятанным, чтобы при следующем вызове
    // начать с новой строки. Считает страница сразу по набору, поэтому окно
    // часто прячут, не переведя строку, а вернувшись — дописывают к чужому
    // ответу, не заметив, что курсор стоит в конце прошлой записи.
    private const int NewLineAfterHiddenMs = 60000;

    // Процессы RDP-клиентов, в окнах которых NumLock остаётся обычным NumLock:
    // иначе рассинхрон состояний двух сеансов нечем лечить.
    private static readonly string[] RemoteDesktopClients = { "mstsc", "msrdc" };

    private readonly WebView2 web = new WebView2();
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly Timer numLockRepairTimer = new Timer();
    private readonly Timer saveTimer = new Timer();

    private HotKey numLockKey;
    private KeyboardHook numLockHook;

    private ToolStripMenuItem startMinimizedItem;
    private ToolStripMenuItem numLockActivateItem;
    private ToolStripMenuItem alwaysOnTopItem;
    private ToolStripMenuItem themeSystemItem;
    private ToolStripMenuItem themeLightItem;
    private ToolStripMenuItem themeDarkItem;

    // Копия содержимого блокнота на стороне приложения: страница присылает её
    // при каждом изменении, отсюда она попадает в настройки. Спрашивать текст
    // у страницы в момент закрытия поздно — ответ приходит асинхронно.
    private string text = "";

    private bool webReady;
    private bool exiting;
    private FormWindowState savedState = FormWindowState.Normal;
    private int lastSummonTick;

    // Момент, когда окно спряталось. Отсчёт начинается с запуска: текст там
    // лежит с прошлого сеанса, то есть заведомо не свежий.
    private int hideTick = Environment.TickCount;

    public MainForm()
    {
      this.Text = "NoteCalque";
      this.Icon = MainForm.AppIcon();
      this.MinimumSize = new Size(320, 200);
      this.StartPosition = FormStartPosition.Manual;
      this.RestoreWindowPlacement();
      this.TopMost = Settings.Default.AlwaysOnTop;

      this.web.Dock = DockStyle.Fill;
      this.ApplyTheme();
      this.Controls.Add((Control) this.web);

      this.numLockRepairTimer.Interval = 100;
      this.numLockRepairTimer.Tick += new EventHandler(this.NumLockRepairTimer_Tick);

      // Настройки пишутся не только при выходе: выключение по кнопке питания
      // или падение не должны уносить набранное.
      this.saveTimer.Interval = 2000;
      this.saveTimer.Tick += new EventHandler(this.SaveTimer_Tick);

      this.BuildTray();
      this.Activated += new EventHandler(this.MainForm_Activated);
    }

    // --- окно ---------------------------------------------------------------

    private static Icon AppIcon()
    {
      try
      {
        return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
      }
      catch (Exception)
      {
        return SystemIcons.Application;
      }
    }

    // Сохранённое место окна годится, только если оно всё ещё на экране:
    // монитор могли отключить, а разрешение поменять.
    private void RestoreWindowPlacement()
    {
      Settings s = Settings.Default;
      Rectangle bounds = new Rectangle(s.WindowLocation, s.WindowSize);
      if (bounds.Width < this.MinimumSize.Width || bounds.Height < this.MinimumSize.Height ||
          !MainForm.OnAnyScreen(bounds))
      {
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Size = new Size(760, 520);
      }
      else
      {
        this.Bounds = bounds;
      }
      this.savedState = s.WindowState == FormWindowState.Minimized
        ? FormWindowState.Normal
        : s.WindowState;
      this.WindowState = this.savedState;
    }

    private static bool OnAnyScreen(Rectangle bounds)
    {
      foreach (Screen screen in Screen.AllScreens)
      {
        if (screen.WorkingArea.IntersectsWith(bounds))
          return true;
      }
      return false;
    }

    protected override void OnResize(EventArgs e)
    {
      base.OnResize(e);
      if (this.WindowState == FormWindowState.Minimized)
      {
        // Свёрнутое окно уходит в трей, а не на панель задач: программа живёт
        // фоном и вызывается по NumLock.
        if (this.Visible)
          this.hideTick = Environment.TickCount;
        this.Visible = false;
      }
      else
      {
        this.savedState = this.WindowState;
      }
    }

    protected override void OnShown(EventArgs e)
    {
      base.OnShown(e);
      // «Сворачивать при запуске» — уже показанному окну, чтобы отработал
      // OnResize и программа спряталась в трей, а не на панель задач.
      if (Settings.Default.StartMinimized)
        this.WindowState = FormWindowState.Minimized;
    }

    protected override void OnLoad(EventArgs e)
    {
      base.OnLoad(e);
      this.InstallHotKeys();
      this.InitWebView();
    }

    // Крестик прячет окно, а не закрывает программу: иначе вместе с окном
    // исчезли бы и хук, и хоткей, то есть весь способ вызова. Выход — из меню
    // в трее.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
      if (e.CloseReason == CloseReason.UserClosing && !this.exiting)
      {
        e.Cancel = true;
        this.WindowState = FormWindowState.Minimized;
        return;
      }
      this.SaveSettings();
      base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
      this.tray.Visible = false;
      if (this.numLockKey != null)
        this.numLockKey.Dispose();
      if (this.numLockHook != null)
        this.numLockHook.Dispose();
      base.OnFormClosed(e);
    }

    // --- трей ---------------------------------------------------------------

    private void BuildTray()
    {
      ContextMenuStrip menu = new ContextMenuStrip();

      ToolStripMenuItem open = new ToolStripMenuItem("Открыть");
      open.Font = new Font(menu.Font, FontStyle.Bold);
      open.Click += delegate { this.MinimizeMaximize(); };
      menu.Items.Add((ToolStripItem) open);
      menu.Items.Add((ToolStripItem) new ToolStripSeparator());

      this.numLockActivateItem = new ToolStripMenuItem("Активировать NumLock при вызове");
      this.numLockActivateItem.CheckOnClick = true;
      this.numLockActivateItem.Checked = Settings.Default.NumLockActivate;
      this.numLockActivateItem.CheckedChanged += delegate
      {
        Settings.Default.NumLockActivate = this.numLockActivateItem.Checked;
      };
      menu.Items.Add((ToolStripItem) this.numLockActivateItem);

      this.startMinimizedItem = new ToolStripMenuItem("Сворачивать при запуске");
      this.startMinimizedItem.CheckOnClick = true;
      this.startMinimizedItem.Checked = Settings.Default.StartMinimized;
      this.startMinimizedItem.CheckedChanged += delegate
      {
        Settings.Default.StartMinimized = this.startMinimizedItem.Checked;
      };
      menu.Items.Add((ToolStripItem) this.startMinimizedItem);

      this.alwaysOnTopItem = new ToolStripMenuItem("Поверх всех окон");
      this.alwaysOnTopItem.CheckOnClick = true;
      this.alwaysOnTopItem.Checked = Settings.Default.AlwaysOnTop;
      this.alwaysOnTopItem.CheckedChanged += delegate
      {
        this.TopMost = this.alwaysOnTopItem.Checked;
        Settings.Default.AlwaysOnTop = this.alwaysOnTopItem.Checked;
      };
      menu.Items.Add((ToolStripItem) this.alwaysOnTopItem);

      ToolStripMenuItem theme = new ToolStripMenuItem("Тема");
      this.themeSystemItem = this.ThemeItem(theme, "Системная", "system");
      this.themeLightItem = this.ThemeItem(theme, "Светлая", "light");
      this.themeDarkItem = this.ThemeItem(theme, "Тёмная", "dark");
      menu.Items.Add((ToolStripItem) theme);

      ToolStripMenuItem font = new ToolStripMenuItem("Шрифт…");
      font.Click += delegate { this.ChooseFont(); };
      menu.Items.Add((ToolStripItem) font);

      ToolStripMenuItem precision = new ToolStripMenuItem("Точность");
      foreach (int digits in MainForm.PrecisionChoices)
        this.PrecisionItem(precision, digits);
      menu.Items.Add((ToolStripItem) precision);

      menu.Items.Add((ToolStripItem) new ToolStripSeparator());

      ToolStripMenuItem folder = new ToolStripMenuItem("Папка настроек");
      folder.Click += delegate { this.OpenSettingsFolder(); };
      menu.Items.Add((ToolStripItem) folder);

      ToolStripMenuItem exit = new ToolStripMenuItem("Выход");
      exit.Click += delegate
      {
        this.exiting = true;
        this.Close();
      };
      menu.Items.Add((ToolStripItem) exit);

      this.tray.Icon = this.Icon;
      this.tray.Text = "NoteCalque";
      this.tray.ContextMenuStrip = menu;
      this.tray.Visible = true;
      this.tray.MouseDoubleClick += new MouseEventHandler(this.Tray_MouseDoubleClick);
    }

    private void Tray_MouseDoubleClick(object sender, MouseEventArgs e)
    {
      if (e.Button == MouseButtons.Left)
        this.MinimizeMaximize();
    }

    private void OpenSettingsFolder()
    {
      try
      {
        Directory.CreateDirectory(PortableSettingsProvider.Folder);
        System.Diagnostics.Process.Start(PortableSettingsProvider.Folder);
      }
      catch (Exception)
      {
        // Проводник мог быть недоступен — не повод падать.
      }
    }

    // Три взаимоисключающих пункта. RadioCheck — только вид галочки; следить
    // за тем, чтобы отмечен был ровно один, всё равно приходится самим.
    private ToolStripMenuItem ThemeItem(ToolStripMenuItem parent, string title, string value)
    {
      ToolStripMenuItem item = new ToolStripMenuItem(title);
      item.Tag = (object) value;
      item.CheckOnClick = false;
      item.Checked = Settings.Default.Theme == value;
      item.Click += delegate { this.SetTheme(value); };
      parent.DropDownItems.Add((ToolStripItem) item);
      return item;
    }

    private void SetTheme(string value)
    {
      Settings.Default.Theme = value;
      this.themeSystemItem.Checked = value == "system";
      this.themeLightItem.Checked = value == "light";
      this.themeDarkItem.Checked = value == "dark";
      this.ApplyTheme();
      this.PostConfig();
    }

    // Значащих цифр в результате. Список короткий нарочно: значение из этого
    // ряда выбирается щелчком, а поле ввода ради шести вариантов не нужно.
    private static readonly int[] PrecisionChoices = { 4, 6, 8, 10, 12, 14 };

    private void PrecisionItem(ToolStripMenuItem parent, int digits)
    {
      ToolStripMenuItem item = new ToolStripMenuItem(digits.ToString());
      item.Checked = Settings.Default.Precision == digits;
      item.Click += delegate
      {
        Settings.Default.Precision = digits;
        foreach (ToolStripItem other in parent.DropDownItems)
        {
          ToolStripMenuItem entry = other as ToolStripMenuItem;
          if (entry != null)
            entry.Checked = entry == item;
        }
        this.PostConfig();
      };
      parent.DropDownItems.Add((ToolStripItem) item);
    }

    private void ChooseFont()
    {
      using (FontDialog dialog = new FontDialog())
      {
        dialog.ShowEffects = false;
        dialog.FontMustExist = true;
        try
        {
          dialog.Font = new Font(Settings.Default.FontFamily, Settings.Default.FontSize,
            MainForm.StyleOf(Settings.Default.FontBold, Settings.Default.FontItalic));
        }
        catch (Exception)
        {
          // Шрифт мог исчезнуть из системы — диалог откроется со своим.
        }
        if (dialog.ShowDialog() != DialogResult.OK)
          return;
        Settings.Default.FontFamily = dialog.Font.Name;
        Settings.Default.FontSize = dialog.Font.SizeInPoints;
        Settings.Default.FontBold = dialog.Font.Bold;
        Settings.Default.FontItalic = dialog.Font.Italic;
        this.PostConfig();
      }
    }

    private static FontStyle StyleOf(bool bold, bool italic)
    {
      FontStyle style = FontStyle.Regular;
      if (bold)
        style |= FontStyle.Bold;
      if (italic)
        style |= FontStyle.Italic;
      return style;
    }

    // Настройки уезжают на страницу одной строкой:
    // тема|шрифт|размер|точность|полужирный|курсив.
    // Разделитель — вертикальная черта, в именах шрифтов её не бывает.
    private void PostConfig()
    {
      Settings s = Settings.Default;
      System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
      this.Post("C" + s.Theme + "|" + s.FontFamily + "|" +
                s.FontSize.ToString(invariant) + "|" +
                s.Precision.ToString(invariant) + "|" +
                (s.FontBold ? "1" : "0") + "|" +
                (s.FontItalic ? "1" : "0"));
    }

    // Цвет подложки WebView2 виден мгновение до первой отрисовки страницы.
    // В тёмной теме белая вспышка при каждом вызове окна заметна и неприятна.
    private void ApplyTheme()
    {
      this.web.DefaultBackgroundColor = MainForm.DarkWanted()
        ? Color.FromArgb(30, 31, 34)
        : Color.White;
    }

    private static bool DarkWanted()
    {
      string theme = Settings.Default.Theme;
      if (theme == "dark")
        return true;
      if (theme == "light")
        return false;
      return MainForm.SystemUsesDarkTheme();
    }

    // У Windows нет управляемого способа спросить про тему приложений —
    // только этот ключ реестра. Нет ключа, значит светлая.
    private static bool SystemUsesDarkTheme()
    {
      try
      {
        object value = Microsoft.Win32.Registry.GetValue(
          "HKEY_CURRENT_USER" + @"\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
          "AppsUseLightTheme", (object) 1);
        return value is int && (int) value == 0;
      }
      catch (Exception)
      {
        return false;
      }
    }

    // --- WebView2 -----------------------------------------------------------

    private static string WebFolder()
    {
      return Path.Combine(PortableSettingsProvider.Folder, "web");
    }

    // Страница и math.js лежат внутри exe и раскладываются рядом с настройками.
    // Рядом с exe их держать нельзя: каталог с программой может оказаться
    // недоступным на запись, а путь к нему — каким угодно.
    // Перезаписываем только изменившееся, чтобы не трогать диск на каждый пуск.
    private static void ExtractWebAssets(string folder)
    {
      Directory.CreateDirectory(folder);
      Assembly asm = Assembly.GetExecutingAssembly();
      const string prefix = "NoteCalque.Web.";
      foreach (string name in asm.GetManifestResourceNames())
      {
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
          continue;
        using (Stream source = asm.GetManifestResourceStream(name))
        {
          if (source == null)
            continue;
          string path = Path.Combine(folder, name.Substring(prefix.Length));
          FileInfo file = new FileInfo(path);
          if (file.Exists && file.Length == source.Length)
            continue;
          using (FileStream target = File.Create(path))
            source.CopyTo((Stream) target);
        }
      }
    }

    private async void InitWebView()
    {
      try
      {
        string folder = MainForm.WebFolder();
        MainForm.ExtractWebAssets(folder);

        // Своё хранилище WebView2: по умолчанию оно ложится рядом с exe, а тот
        // может лежать где угодно, в том числе там, куда нет записи.
        string userData = Path.Combine(PortableSettingsProvider.Folder, "WebView2");
        Directory.CreateDirectory(userData);

        CoreWebView2Environment environment =
          await CoreWebView2Environment.CreateAsync((string) null, userData);
        await this.web.EnsureCoreWebView2Async(environment);

        CoreWebView2 core = this.web.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping(
          VirtualHost, folder, CoreWebView2HostResourceAccessKind.Allow);

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;

        core.WebMessageReceived +=
          new EventHandler<CoreWebView2WebMessageReceivedEventArgs>(this.OnWebMessage);
        core.NewWindowRequested +=
          new EventHandler<CoreWebView2NewWindowRequestedEventArgs>(this.OnNewWindowRequested);

        this.web.Source = new Uri("https://" + VirtualHost + "/index.html");
      }
      catch (Exception ex)
      {
        MessageBox.Show(
          "Не удалось запустить WebView2." + Environment.NewLine + Environment.NewLine +
          ex.Message + Environment.NewLine + Environment.NewLine +
          "Нужен установленный WebView2 Runtime (Microsoft Edge WebView2).",
          "NoteCalque", MessageBoxButtons.OK, MessageBoxIcon.Error);
        this.exiting = true;
        this.Close();
      }
    }

    private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
      // Страница своя и ссылок наружу не содержит; отдельных окон ей тем более
      // открывать незачем.
      e.Handled = true;
    }

    // Протокол односимвольный и строковый: R — страница готова, T — новый текст.
    // Обратно: I — начальный текст, S — подставить символ, F — вернуть фокус
    // (F1 — вдобавок увести курсор в конец и начать новую строку).
    // Разбирать тут нечего: в сообщении может быть любой многострочный текст.
    private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
      string message;
      try
      {
        message = e.TryGetWebMessageAsString();
      }
      catch (ArgumentException)
      {
        return;
      }
      if (string.IsNullOrEmpty(message))
        return;

      if (message[0] == 'R')
      {
        this.webReady = true;
        this.PostConfig();
        this.text = Settings.Default.Text ?? "";
        this.Post("I" + this.text);
      }
      else if (message[0] == 'T')
      {
        this.text = message.Substring(1);
        this.saveTimer.Stop();
        this.saveTimer.Start();
      }
    }

    private void Post(string message)
    {
      if (!this.webReady || this.web.CoreWebView2 == null)
        return;
      try
      {
        this.web.CoreWebView2.PostWebMessageAsString(message);
      }
      catch (Exception)
      {
        // Страница могла перезагрузиться — сообщение не критично.
      }
    }

    // --- настройки ----------------------------------------------------------

    private void SaveTimer_Tick(object sender, EventArgs e)
    {
      this.saveTimer.Stop();
      this.SaveSettings();
    }

    private void SaveSettings()
    {
      Settings s = Settings.Default;
      s.Text = this.text;
      Rectangle bounds = this.WindowState == FormWindowState.Normal && this.Visible
        ? this.Bounds
        : this.RestoreBounds;
      if (bounds.Width > 0 && bounds.Height > 0)
      {
        s.WindowLocation = bounds.Location;
        s.WindowSize = bounds.Size;
      }
      s.WindowState = this.savedState;
      s.AlwaysOnTop = this.TopMost;
      s.Save();
    }

    // --- вызов по NumLock ---------------------------------------------------

    private void InstallHotKeys()
    {
      // Оба способа поднимаются вместе и намеренно дублируют друг друга,
      // см. заголовок KeyboardHook.cs. Отказ любого из них не смертелен,
      // поэтому неудачу здесь молча переживаем.
      try
      {
        this.numLockKey = new HotKey(Keys.NumLock, HotKey.KeyModifiers.None,
          new EventHandler(this.OnHotKeyPressed));
      }
      catch (Exception)
      {
        this.numLockKey = (HotKey) null;
      }

      this.numLockHook = new KeyboardHook(Keys.NumLock);
      // Чинить состояние клавиатуры только пока активны мы: в чужие окна,
      // включая сеанс RDP, не вмешиваемся.
      this.numLockHook.RepairWanted =
        () => Settings.Default.NumLockActivate && Win32.GetForegroundWindow() == this.Handle;
      this.numLockHook.Substitute = new Action<string>(this.Substitute);
      this.numLockHook.Toggled += new EventHandler(this.OnNumLockToggled);
      this.numLockHook.NumpadDesync += new EventHandler(this.OnNumpadDesync);
    }

    private bool SummonRequested()
    {
      int now = Environment.TickCount;
      if (unchecked (now - this.lastSummonTick) < MainForm.SummonGuardMs)
        return false;
      this.lastSummonTick = now;
      return true;
    }

    // Вызывается в том числе из клавиатурного хука. Затягивать обработчик хука
    // нельзя: система молча снимет его по LowLevelHooksTimeout (по умолчанию
    // 300 мс), поэтому работу откладываем в очередь сообщений.
    private void OnHotKeyPressed(object sender, EventArgs e)
    {
      if (!this.SummonRequested())
        return;
      if (this.IsHandleCreated)
        this.BeginInvoke((Delegate) new MethodInvoker(this.MinimizeMaximize));
      else
        this.MinimizeMaximize();
    }

    // Хук увидел живое нажатие NumLock. Состояние уже переключилось — досылаем
    // встречное переключение немедленно, не дожидаясь отпускания клавиши:
    // именно эта задержка в прежней реализации и давала полсекунды стрелок.
    //
    // В окне RDP-клиента не компенсируем: там NumLock должен работать как
    // обычный NumLock, иначе рассинхрон состояний двух сеансов нечем лечить.
    private void OnNumLockToggled(object sender, EventArgs e)
    {
      if (!MainForm.ForegroundIsRemoteDesktop())
        Win32.ToggleNumLock();
      this.OnHotKeyPressed(sender, e);
    }

    private static bool ForegroundIsRemoteDesktop()
    {
      return Array.IndexOf<string>(MainForm.RemoteDesktopClients, Win32.ForegroundProcessName()) >= 0;
    }

    // Прячем только то окно, которое сейчас и свёрнуть-то есть куда: видимое
    // и активное. Видимое, но перекрытое чужим окном — поднимаем, а не прячем;
    // Form.Activate() из фонового процесса молча ничего не делает, подробности
    // в Win32.ForceForeground.
    private void MinimizeMaximize()
    {
      if (this.Visible && this.WindowState != FormWindowState.Minimized &&
          Win32.GetForegroundWindow() == this.Handle)
        this.WindowState = FormWindowState.Minimized;
      else
        this.Summon();
    }

    private void Summon()
    {
      bool wasHidden = this.WindowState == FormWindowState.Minimized || !this.Visible;
      if (wasHidden)
      {
        this.Visible = true;
        this.WindowState = this.savedState;
      }
      Win32.ForceForeground(this.Handle);
      this.web.Focus();
      // Новую строку просим только после долгого перерыва: окно, поднятое
      // из-под чужого, никуда не пропадало, и текст в нём свой и свежий.
      bool stale = wasHidden &&
        unchecked (Environment.TickCount - this.hideTick) >= MainForm.NewLineAfterHiddenMs;
      this.Post(stale ? "F1" : "F0");
      this.ActivateNumLock();
    }

    // Запустили вторую копию: она не работает, а просит показать эту.
    // Сигнал приходит из потока пула, поэтому работа уходит в очередь окна.
    public void SummonFromAnotherInstance()
    {
      if (!this.IsHandleCreated)
        return;
      try
      {
        this.BeginInvoke((Delegate) new MethodInvoker(this.Summon));
      }
      catch (Exception)
      {
        // Окно закрывается прямо сейчас — показывать нечего.
      }
    }

    // Настройка «Активировать NumLock при вызове». Win32.NumLock эмулирует
    // нажатие; хук помечен как пропускающий синтетические события
    // (LLKHF_INJECTED), поэтому снимать его на время не нужно.
    private void ActivateNumLock()
    {
      if (!Settings.Default.NumLockActivate)
        return;
      Win32.NumLock(true);
    }

    private void MainForm_Activated(object sender, EventArgs e)
    {
      this.ActivateNumLock();
    }

    // Хук проглотил стрелку, приехавшую вместо цифры, и сообщил, что она
    // должна была напечатать. Кладём символ прямо в поле ввода: любой путь
    // через клавиатуру прошёл бы через ту же трансляцию, которая и сломалась.
    private void Substitute(string character)
    {
      this.Post("S" + character);
    }

    // Хук заметил, что цифровой блок транслируется в стрелки. Переключаем
    // NumLock безусловно: проверять текущее состояние нечем, врут оба чтения.
    //
    // Через таймер, а не сразу: переключение, отправленное в момент, когда
    // клавиша ещё удерживается, не срабатывает. Заодно обработчик хука не
    // задерживается и не рискует выйти за LowLevelHooksTimeout.
    private void OnNumpadDesync(object sender, EventArgs e)
    {
      if (this.numLockRepairTimer.Enabled)
        return;
      this.numLockRepairTimer.Enabled = true;
    }

    private void NumLockRepairTimer_Tick(object sender, EventArgs e)
    {
      this.numLockRepairTimer.Enabled = false;
      // Пока клавиша не отпущена, переключение не возьмётся — ждём следующего тика.
      if (KeyboardInfo.GetAsyncKeyState(Keys.NumLock).IsPressed)
        return;
      Win32.ToggleNumLock();
    }
  }
}
