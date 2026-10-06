
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoteCalque
{
  public class Win32
  {
    public const int INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const uint KEYEVENTF_SCANCODE = 0x0008;

        [DllImport("user32.dll")]
    private static extern bool SetWindowPlacement(IntPtr hWnd, [In] ref Win32.WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref Win32.WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", EntryPoint = "keybd_event", CharSet = CharSet.Auto)]
    public static extern void Keybd_event(byte vk, byte scan, uint flags, int dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);
 
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, [MarshalAs(UnmanagedType.LPArray)] INPUT[] pInput, int structSize);

    [DllImport("user32.dll")]
    static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    static extern bool SetKeyboardState(byte[] lpKeyState);


    private static void sendKey(int vCode)
    {
      INPUT[] input = new INPUT[2];
      input[0].type = INPUT_KEYBOARD;
      input[0].ki.vk = (ushort)vCode;
      input[0].ki.scanCode = 0; //(ushort)MapVirtualKey((uint)vCode, 0);
      input[0].ki.flags = KEYEVENTF_EXTENDEDKEY;
      input[0].ki.time = 0; 

      input[1].type = INPUT_KEYBOARD;
      input[1].ki.vk = (ushort)vCode;
      input[1].ki.scanCode = 0; //input[0].ki.scanCode;
      input[1].ki.flags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP;
      input[1].ki.time = 0;

      if (SendInput(2, input, Marshal.SizeOf(input[0])) != 2U )
        throw new Exception("Could not send key: " + (object)vCode);
    }

    public static void SetMaximizePosition(IntPtr hwnd, int X)
    {
      Win32.WINDOWPLACEMENT lpwndpl = new Win32.WINDOWPLACEMENT();
      lpwndpl.length = Marshal.SizeOf((object) lpwndpl);
      Win32.GetWindowPlacement(hwnd, ref lpwndpl);
      lpwndpl.ptMaxPosition.X = X;
      Win32.SetWindowPlacement(hwnd, ref lpwndpl);
    }

    public static void NumLock(bool State)
    {
      if (Console.NumberLock == State) return;
      //sendKey(0x90);
      Win32.Keybd_event(0x90, 0x45, Win32.KEYEVENTF_EXTENDEDKEY, 0); //  IntPtr.Zero);
      Win32.Keybd_event(0x90, 0x45, Win32.KEYEVENTF_EXTENDEDKEY | Win32.KEYEVENTF_KEYUP, 0); // IntPtr.Zero);
      //SendKeys.SendWait("{NUMLOCK}");
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private static IntPtr cachedWindow;
    private static string cachedProcess = "";

    // Имя процесса, которому принадлежит активное окно, в нижнем регистре.
    // Вызывается из обработчика клавиатурного хука, поэтому результат кешируется
    // по хэндлу окна: обращение к таблице процессов на каждое нажатие — верный
    // способ выйти за LowLevelHooksTimeout и лишиться хука.
    public static string ForegroundProcessName()
    {
      IntPtr hWnd = Win32.GetForegroundWindow();
      if (hWnd == Win32.cachedWindow)
        return Win32.cachedProcess;
      Win32.cachedWindow = hWnd;
      Win32.cachedProcess = "";
      try
      {
        uint pid;
        Win32.GetWindowThreadProcessId(hWnd, out pid);
        if (pid != 0U)
        {
          using (Process process = Process.GetProcessById((int) pid))
            Win32.cachedProcess = process.ProcessName.ToLowerInvariant();
        }
      }
      catch (Exception)
      {
        // Процесс мог закрыться между двумя вызовами — считаем имя неизвестным.
      }
      return Win32.cachedProcess;
    }

    // Безусловное переключение NumLock. В отличие от NumLock(bool) не смотрит
    // на Console.NumberLock: при рассинхроне тот читает тот же бит, что зажигает
    // индикатор, и уверенно сообщает «включён» ровно тогда, когда чинить и надо.
    public static void ToggleNumLock()
    {
      Win32.Keybd_event(0x90, 0x45, Win32.KEYEVENTF_EXTENDEDKEY, 0);
      Win32.Keybd_event(0x90, 0x45, Win32.KEYEVENTF_EXTENDEDKEY | Win32.KEYEVENTF_KEYUP, 0);
    }

    public const int WM_SYSCOMMAND = 0x0112;
    public const int WM_INITMENU = 0x0116;

    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_UNCHECKED = 0x0000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int CheckMenuItem(IntPtr hMenu, int uIDCheckItem, uint uCheck);

    // Своя команда в системном меню окна — том, что открывается по значку
    // в левом углу заголовка. Идентификатор обязан быть кратен шестнадцати:
    // младшие четыре бита wParam в WM_SYSCOMMAND система использует сама.
    public static void AddSystemMenuItem(IntPtr hWnd, int id, string text)
    {
      IntPtr menu = Win32.GetSystemMenu(hWnd, false);
      if (menu == IntPtr.Zero)
        return;
      Win32.AppendMenu(menu, MF_SEPARATOR, 0, (string) null);
      Win32.AppendMenu(menu, MF_STRING, id, text);
    }

    public static void CheckSystemMenuItem(IntPtr hWnd, int id, bool state)
    {
      IntPtr menu = Win32.GetSystemMenu(hWnd, false);
      if (menu == IntPtr.Zero)
        return;
      Win32.CheckMenuItem(menu, id, state ? MF_CHECKED : MF_UNCHECKED);
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private const uint SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000;
    private const uint SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfo")]
    private static extern bool SystemParametersInfoSet(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    // Поднимает окно на передний план из фонового процесса.
    //
    // Обычного Form.Activate() (то есть SetForegroundWindow) здесь мало.
    // Windows разрешает менять активное окно только процессу с правом на
    // передний план: он либо сам сейчас на переднем плане, либо запущен тем,
    // кто на переднем плане, либо получил последнее событие ввода. Пока вызов
    // шёл через RegisterHotKey, право давало само нажатие хоткея. Хук же
    // нажатие проглатывает, событие ввода не достаётся никому, и права нет —
    // SetForegroundWindow молча ничего не делает, только мигает кнопкой
    // в панели задач.
    //
    // Право даёт ещё одно условие: истёкший таймаут ForegroundLockTimeout.
    // Его и обнуляем на время вызова, возвращая прежнее значение сразу после.
    //
    // Расхожий способ добиться того же — AttachThreadInput, но он сращивает
    // очереди ввода двух потоков вместе с состоянием клавиатуры, и трогать это
    // состояние приложению, которое само всплывает по нажатию клавиши, лишний
    // раз незачем. Здесь очереди ввода не трогаются вообще.
    public static void ForceForeground(IntPtr hWnd)
    {
      if (Win32.GetForegroundWindow() == hWnd)
        return;
      if (Win32.IsIconic(hWnd))
        Win32.ShowWindow(hWnd, Win32.SW_RESTORE);
      Win32.BringWindowToTop(hWnd);

      // Обычно право уже есть: его даёт нажатие хоткея, ради которого
      // RegisterHotKey и оставлен. Тогда обходимся без вмешательства в
      // общесистемный параметр.
      if (Win32.SetForegroundWindow(hWnd))
        return;

      uint saved = 0;
      bool lifted = Win32.SystemParametersInfo(SPI_GETFOREGROUNDLOCKTIMEOUT, 0, ref saved, 0) && saved != 0;
      if (!lifted)
        return;
      Win32.SystemParametersInfoSet(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, IntPtr.Zero, 0);
      try
      {
        Win32.SetForegroundWindow(hWnd);
      }
      finally
      {
        Win32.SystemParametersInfoSet(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, new IntPtr((int) saved), 0);
      }
    }


    internal struct KEYBOARDINPUT
    {
      public ushort vk;
      public ushort scanCode;
      public uint flags;
      public uint time;
      public uint extrainfo;
    }

    internal struct MOUSEINPUT
    {
      public uint dx;
      public uint dy;
      public uint mouseData;
      public uint dwFlags;
      public uint time;
      public IntPtr dwExtraInfo;
    }

    internal struct HARDWAREINPUT
    {
      public int uMsg;
      public short wParamL;
      public short wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUT
    {
      [FieldOffset(0)]
      public int type;
      [FieldOffset(4)]
      public Win32.MOUSEINPUT mi;
      [FieldOffset(4)]
      public Win32.KEYBOARDINPUT ki;
      [FieldOffset(4)]
      public Win32.HARDWAREINPUT hi;
    }

    private struct WINDOWPLACEMENT
    {
      public int length;
      public int flags;
      public int showCmd;
      public Point ptMinPosition;
      public Point ptMaxPosition;
      public Rectangle rcNormalPosition;
    }
  }
}
