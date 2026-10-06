
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoteCalque
{
  internal class HotKey : IMessageFilter
  {
    private const int WM_HOTKEY = 786;
    private const int id = 100;
    private IntPtr handle;
    private bool IsInit;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(
      IntPtr hWnd,
      int id,
      HotKey.KeyModifiers fsModifiers,
      Keys vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public IntPtr Handle
    {
      get => this.handle;
      set => this.handle = value;
    }

    private event EventHandler HotKeyPressed;

    public HotKey(Keys key, HotKey.KeyModifiers modifier, EventHandler hotKeyPressed)
    {
      this.HotKeyPressed = hotKeyPressed;
      this.RegisterHotKey(key, modifier);
      Application.AddMessageFilter((IMessageFilter) this);
      this.IsInit = true;
    }

    ~HotKey() => this.Dispose();

    public void Dispose()
    {
      if (!this.IsInit)
        return;
      Application.RemoveMessageFilter((IMessageFilter) this);
      HotKey.UnregisterHotKey(this.handle, 100);
      this.IsInit = false;
    }

    private void RegisterHotKey(Keys key, HotKey.KeyModifiers modifier)
    {
      if (key == Keys.None)
        return;
      HotKey.RegisterHotKey(this.handle, 100, modifier, key);
    }

    public bool PreFilterMessage(ref Message m)
    {
      if (m.Msg != 786)
        return false;
      this.HotKeyPressed((object) this, new EventArgs());
      return true;
    }

    public enum KeyModifiers
    {
      None = 0,
      Alt = 1,
      Control = 2,
      Shift = 4,
      Windows = 8,
    }
  }
}
