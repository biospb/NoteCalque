// Глобальный низкоуровневый клавиатурный хук (WH_KEYBOARD_LL).
//
// Работает в паре с RegisterHotKey, а не вместо него, и намеренно дублирует
// его: каждый из двух способов поодиночке иногда не срабатывает. Хук система
// молча снимает, если он не уложился в LowLevelHooksTimeout. Регистрацию
// хоткея может перехватить чужая программа, а в новых версиях Windows и сама
// система. Уцелевший делает работу за обоих; кто гасит повтор, когда сработали
// оба, смотри в mainForm.SummonRequested.
//
// Клавишу хук не глотает, хотя вернуть 1 и умеет. Проглатывание обходится
// дорого: событие ввода не достаётся никому, и процесс лишается права поднять
// своё окно на передний план — это право как раз выдаётся за нажатие хоткея.
// А ещё проглоченной клавишей нельзя починить рассинхрон состояния
// клавиатуры, который устраивает, например, клиент RDP: пользователь жмёт
// NumLock, а нажатие съедаем мы.
//
// Поэтому нажатие идёт насквозь, состояние честно переключается — а хук тут
// же досылает встречное переключение. Итог за одно нажатие: два переключения,
// состояние на месте. Окно, в котором цифровой блок отдаёт стрелки, сжимается
// с полусекунды прежней реализации до времени доставки одного события.

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoteCalque
{
  internal sealed class KeyboardHook : IDisposable
  {
    private const int WH_KEYBOARD_LL = 13;
    private const int HC_ACTION = 0;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_EXTENDED = 0x0001;
    private const uint LLKHF_INJECTED = 0x0010;

    // Скан-коды цифрового блока и виртуальные клавиши, в которые система
    // обязана их транслировать при включённом NumLock. Если пришло не то —
    // значит NumLock реально выключен, чего бы ни показывал индикатор.
    // У отдельного блока стрелок скан-коды те же, но с флагом расширенной
    // клавиши, поэтому проверять LLKHF_EXTENDED обязательно.
    private static readonly uint[] NumpadScanCodes =
      { 0x47, 0x48, 0x49, 0x4B, 0x4C, 0x4D, 0x4F, 0x50, 0x51, 0x52, 0x53 };

    private static readonly uint[] NumpadVirtualKeys =
      { 0x67, 0x68, 0x69, 0x64, 0x65, 0x66, 0x61, 0x62, 0x63, 0x60, 0x6E };

    // Что клавиша должна была напечатать. Разделитель — точка: в NoteCalc
    // здесь стояла запятая под его собственный разбор, math.js понимает
    // только точку.
    private static readonly string[] NumpadText =
      { "7", "8", "9", "4", "5", "6", "1", "2", "3", "0", "." };

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, ref KBDLLHOOKSTRUCT lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, ref KBDLLHOOKSTRUCT lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
      public uint vkCode;
      public uint scanCode;
      public uint flags;
      public uint time;
      public IntPtr dwExtraInfo;
    }

    private readonly uint vkCode;
    // Делегат нужно держать полем: хук хранит только неуправляемый указатель,
    // и если GC соберёт делегат, следующее нажатие уронит процесс.
    private readonly LowLevelKeyboardProc proc;
    private IntPtr hook;
    private bool isDown;

    // Спрашивается перед починкой рассинхрона: нужна ли она сейчас.
    public Func<bool> RepairWanted;

    // Куда положить символ вместо нажатия, приехавшего стрелкой.
    public Action<string> Substitute;

    // Отслеживаемая клавиша нажата вживую, состояние только что переключилось.
    // Обработчику остаётся дослать встречное переключение и показать окно.
    public event EventHandler Toggled;

    // Замечен рассинхрон: система транслирует цифровой блок в стрелки.
    public event EventHandler NumpadDesync;

    public KeyboardHook(Keys key)
    {
      this.vkCode = (uint) key;
      this.proc = new LowLevelKeyboardProc(this.HookProc);
      this.hook = KeyboardHook.SetWindowsHookEx(WH_KEYBOARD_LL, this.proc, KeyboardHook.GetModuleHandle(null), 0U);
    }

    ~KeyboardHook() => this.Dispose();

    public bool IsInstalled => this.hook != IntPtr.Zero;

    public void Dispose()
    {
      if (this.hook == IntPtr.Zero)
        return;
      KeyboardHook.UnhookWindowsHookEx(this.hook);
      this.hook = IntPtr.Zero;
      GC.SuppressFinalize((object) this);
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, ref KBDLLHOOKSTRUCT lParam)
    {
      // Чужое событие либо наша же эмуляция (LLKHF_INJECTED — так досылается
      // встречное переключение) — не наше дело, пропускаем.
      if (nCode != HC_ACTION || (lParam.flags & LLKHF_INJECTED) != 0U)
        return KeyboardHook.CallNextHookEx(this.hook, nCode, wParam, ref lParam);

      int msg = wParam.ToInt32();

      if (lParam.vkCode != this.vkCode)
        return this.CheckNumpad(nCode, wParam, ref lParam, msg);

      if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
      {
        // Автоповтор при удержании не должен переключать состояние по кругу.
        if (!this.isDown)
        {
          this.isDown = true;
          if (this.Toggled != null)
            this.Toggled((object) this, EventArgs.Empty);
        }
      }
      else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
        this.isDown = false;

      // Клавиша не глотается: её ждут и RegisterHotKey, и сама система.
      return KeyboardHook.CallNextHookEx(this.hook, nCode, wParam, ref lParam);
    }

    // Ловит рассинхрон состояния клавиатуры: индикатор NumLock горит, а
    // система всё равно транслирует цифровой блок в стрелки. Страховка на
    // случай, если состояние разъедется по причине, до которой мы не достаём.
    //
    // Определяем по факту, а не по индикатору: скан-код цифрового блока обязан
    // приехать с цифровой виртуальной клавишей. Приехал со стрелочной — значит
    // NumLock фактически выключен, чего бы ни показывал светодиод.
    private IntPtr CheckNumpad(int nCode, IntPtr wParam, ref KBDLLHOOKSTRUCT lParam, int msg)
    {
      if ((lParam.flags & LLKHF_EXTENDED) != 0U)
        return KeyboardHook.CallNextHookEx(this.hook, nCode, wParam, ref lParam);

      int i = Array.IndexOf<uint>(KeyboardHook.NumpadScanCodes, lParam.scanCode);
      if (i < 0 || lParam.vkCode == KeyboardHook.NumpadVirtualKeys[i])
        return KeyboardHook.CallNextHookEx(this.hook, nCode, wParam, ref lParam);

      if (this.Substitute == null || this.RepairWanted == null || !this.RepairWanted())
        return KeyboardHook.CallNextHookEx(this.hook, nCode, wParam, ref lParam);

      // Нажатие приехало стрелкой, но по скан-коду известно, что оно должно
      // было напечатать. Глотаем стрелку, вместо неё кладём символ прямо в
      // поле ввода и параллельно запускаем починку состояния, чтобы следующие
      // нажатия обошлись без подмены.
      //
      // Именно символ, а не клавишу: подстановка по виртуальному коду зависела
      // бы от раскладки и состояния Shift, а чинить мы беремся только когда
      // активно наше собственное окно — значит и класть есть куда напрямую.
      // Синхронно, без откладывания: иначе подставленный символ мог бы отстать
      // от следующего нажатия, если состояние починится между ними.
      if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
      {
        this.Substitute(KeyboardHook.NumpadText[i]);
        if (this.NumpadDesync != null)
          this.NumpadDesync((object) this, EventArgs.Empty);
      }
      return new IntPtr(1);
    }
  }
}
