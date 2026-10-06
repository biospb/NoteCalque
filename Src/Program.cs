// Точка входа.
//
// Здесь же собрано всё, что делает программу одним файлом: сборки WebView2
// лежат внутри exe ресурсами и подставляются по запросу среды, а нативный
// загрузчик раскладывается на диск и подгружается вручную.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace NoteCalque
{
  internal static class Program
  {
    private const string LibraryPrefix = "NoteCalque.Lib.";
    private const string NativePrefix = "NoteCalque.Native.";
    private const string MutexName = "NoteCalque.SingleInstance";
    private const string SummonEventName = "NoteCalque.Summon";

    // Второй экземпляр вреден не тем, что лишний, а тем, что дерётся за
    // NumLock: RegisterHotKey достанется кому-то одному, а хуков встанет два,
    // и встречное переключение уйдёт дважды — состояние вернётся не туда.
    private static Mutex single;

    [STAThread]
    private static void Main()
    {
      // Обработчик ставится до того, как среда впервые попробует найти сборки
      // WebView2. Поэтому настоящий запуск вынесен в отдельный метод: типы
      // загружаются при компиляции метода, и упомяни мы MainForm прямо здесь,
      // поиск начался бы раньше, чем выполнилась эта строка.
      AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(Program.ResolveEmbedded);
      Program.Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
      bool created;
      Program.single = new Mutex(true, MutexName, out created);
      if (!created)
      {
        Program.SummonExisting();
        return;
      }

      Program.LoadNativeLoader();

      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);

      MainForm form = new MainForm();
      using (EventWaitHandle summon =
        new EventWaitHandle(false, EventResetMode.AutoReset, SummonEventName))
      {
        // Ждём сигнала в потоке пула: очередь сообщений окна должна оставаться
        // свободной, её занятость и есть то, ради чего всё это делается.
        RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(
          (WaitHandle) summon,
          new WaitOrTimerCallback(Program.OnSummonRequested),
          (object) form, -1, false);
        Application.Run((Form) form);
        registration.Unregister((WaitHandle) summon);
      }

      GC.KeepAlive((object) Program.single);
    }

    private static void OnSummonRequested(object state, bool timedOut)
    {
      if (timedOut)
        return;
      ((MainForm) state).SummonFromAnotherInstance();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    private const int ASFW_ANY = -1;

    // Вторая копия не работает, а показывает первую и уходит. Молча закрыться
    // тоже можно, но со стороны это выглядит как будто программа не запустилась.
    private static void SummonExisting()
    {
      try
      {
        EventWaitHandle summon;
        if (!EventWaitHandle.TryOpenExisting(SummonEventName, out summon))
          return;
        using (summon)
        {
          // Право поднять окно на передний план сейчас у нас: нас только что
          // запустили. Передаём его тому, кто уже работает, иначе его окно
          // всего лишь мигнёт кнопкой в панели задач.
          Program.AllowSetForegroundWindow(ASFW_ANY);
          summon.Set();
        }
      }
      catch (Exception)
      {
        // Первая копия могла закрыться прямо сейчас — показывать нечего.
      }
    }

    // Управляемые сборки WebView2 подставляются прямо из ресурсов, на диск
    // не попадая: Assembly.Load из массива байтов не требует файла.
    private static Assembly ResolveEmbedded(object sender, ResolveEventArgs args)
    {
      string name = new AssemblyName(args.Name).Name;
      byte[] bytes = Program.ReadResource(LibraryPrefix + name + ".dll");
      return bytes == null ? (Assembly) null : Assembly.Load(bytes);
    }

    private static byte[] ReadResource(string name)
    {
      using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
      {
        if (source == null)
          return (byte[]) null;
        using (MemoryStream buffer = new MemoryStream())
        {
          source.CopyTo((Stream) buffer);
          return buffer.ToArray();
        }
      }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string fileName);

    // WebView2Loader.dll — нативная, из ресурса её в процесс не загрузить.
    // Раскладываем рядом с настройками и подгружаем по полному пути: дальше
    // Microsoft.Web.WebView2.Core обращается к ней обычным P/Invoke по имени,
    // а Windows отдаёт уже загруженный модуль и по диску не ищет.
    //
    // arm64 не кладём: .NET Framework под arm64 не существует, на таких
    // машинах процесс всё равно идёт в эмуляции x64.
    private static void LoadNativeLoader()
    {
      string architecture = IntPtr.Size == 8 ? "x64" : "x86";
      byte[] bytes = Program.ReadResource(NativePrefix + architecture + ".WebView2Loader.dll");
      if (bytes == null)
        return;
      try
      {
        string folder = Path.Combine(PortableSettingsProvider.Folder, "native", architecture);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "WebView2Loader.dll");

        // Перезаписываем только изменившееся: файл может быть занят прошлым
        // запуском, да и трогать диск на каждый пуск незачем.
        FileInfo file = new FileInfo(path);
        if (!file.Exists || file.Length != bytes.Length)
          File.WriteAllBytes(path, bytes);

        Program.LoadLibrary(path);
      }
      catch (Exception)
      {
        // Не разложился — WebView2 не поднимется и скажет об этом сам,
        // с понятным пользователю сообщением.
      }
    }
  }
}
