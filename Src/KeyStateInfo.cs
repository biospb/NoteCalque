
using System.Windows.Forms;

namespace NoteCalque
{
  public struct KeyStateInfo
  {
    private Keys _key;
    private bool _isPressed;
    private bool _isToggled;

    public KeyStateInfo(Keys key, bool ispressed, bool istoggled)
    {
      this._key = key;
      this._isPressed = ispressed;
      this._isToggled = istoggled;
    }

    public static KeyStateInfo Default => new KeyStateInfo(Keys.None, false, false);

    public Keys Key => this._key;

    public bool IsPressed => this._isPressed;

    public bool IsToggled => this._isToggled;
  }
}
