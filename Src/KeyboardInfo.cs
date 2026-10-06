
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoteCalque
{
  public class KeyboardInfo
  {
    private KeyboardInfo()
    {
    }

    [DllImport("user32")]
    private static extern short GetKeyState(int vKey);

    public static KeyStateInfo GetKeyState(Keys key)
    {
      byte[] bytes = BitConverter.GetBytes(KeyboardInfo.GetKeyState((int) key));
      bool istoggled = bytes[0] == (byte) 1;
      bool ispressed = bytes[1] == (byte) 1;
      return new KeyStateInfo(key, ispressed, istoggled);
    }

    [DllImport("user32")]
    private static extern short GetAsyncKeyState(int vKey);

    public static KeyStateInfo GetAsyncKeyState(Keys key)
    {
      byte[] bytes = BitConverter.GetBytes(KeyboardInfo.GetAsyncKeyState((int) key));
      bool istoggled = bytes[0] == (byte) 1;
      bool ispressed = bytes[1] == (byte) 1;
      return new KeyStateInfo(key, ispressed, istoggled);
    }
  }
}
