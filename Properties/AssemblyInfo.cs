using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("NoteCalque")]
[assembly: AssemblyDescription("Калькулятор-блокнот: выражения слева, результаты справа")]
[assembly: AssemblyProduct("NoteCalque")]
[assembly: AssemblyCopyright("NoteCalc: © 2009 Vladimir Potapov, Karluk, www.keleg.info; NoteCalque: © 2026 contributors. GPL-3.0-only.")]
[assembly: ComVisible(false)]

// AssemblyVersion держится зафиксированной, номер сборки двигается только в
// AssemblyFileVersion. Версия сборки входит в имена, по которым .NET ищет
// чужие настройки и привязки, и её движение ломает больше, чем сообщает.
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]
