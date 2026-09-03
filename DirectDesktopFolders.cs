using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;
#if WINDOWS_COMPOSITION
using System.Numerics;
using Windows.UI.Composition;
#endif

[assembly: AssemblyTitle("Desktop Folders v6")]
[assembly: AssemblyDescription("Virtual iPhone-style collections for Windows Desktop")]
[assembly: AssemblyProduct("Desktop Folders")]
[assembly: AssemblyVersion("6.0.0.0")]
[assembly: AssemblyFileVersion("6.0.0.0")]

namespace DesktopFoldersDirect
{
    internal sealed class DesktopItem
    {
        public string Name;
        public string Path;
        public Rectangle Bounds;
        public int Index;
        public bool IsGroup;
        public bool IsShortcut;
    }

    internal sealed class AppSettings
    {
        public bool StartWithWindows;
        public bool ReduceMotion;
        public bool DissolveSingleAppGroup = true;
        public int FolderHoverDelay = 280;
    }

    internal sealed class VirtualMember
    {
        public string Path;
        public int OriginalAttributes;
    }

    internal sealed class VirtualGroup
    {
        public string Id;
        public string Name;
        public string TilePath;
        public string IconPath;
        public List<VirtualMember> Members = new List<VirtualMember>();
        public List<string> Pinned = new List<string>();
    }

    internal sealed class VirtualLayout
    {
        public int Version = 2;
        public List<VirtualGroup> Groups = new List<VirtualGroup>();
    }

    internal static class DataStore
    {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        internal static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopFolders");
        internal static readonly string SettingsPath = Path.Combine(DataDirectory, "settings.json");
        internal static readonly string VirtualLayoutPath = Path.Combine(DataDirectory, "virtual-layout.json");
        internal static readonly string DragDiagnosticPath = Path.Combine(DataDirectory, "drag-diagnostic.log");
        internal static readonly string OpenRequestDirectory = Path.Combine(DataDirectory, "open-requests");

        internal static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    AppSettings loaded = Json.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                    if (loaded != null) { loaded.FolderHoverDelay = Math.Max(120, Math.Min(800, loaded.FolderHoverDelay)); return loaded; }
                }
            }
            catch { }
            return new AppSettings();
        }

        internal static void SaveSettings(AppSettings settings)
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(SettingsPath, Json.Serialize(settings));
        }

        internal static VirtualLayout LoadVirtualLayout()
        {
            try
            {
                if (File.Exists(VirtualLayoutPath))
                {
                    VirtualLayout layout = Json.Deserialize<VirtualLayout>(File.ReadAllText(VirtualLayoutPath));
                    if (layout != null && layout.Groups != null) return layout;
                }
            }
            catch { }
            return new VirtualLayout();
        }

        internal static void SaveVirtualLayout(VirtualLayout layout)
        {
            Directory.CreateDirectory(DataDirectory);
            string temporary = VirtualLayoutPath + ".tmp";
            File.WriteAllText(temporary, Json.Serialize(layout));
            if (File.Exists(VirtualLayoutPath)) File.Replace(temporary, VirtualLayoutPath, null); else File.Move(temporary, VirtualLayoutPath);
        }

        internal static VirtualGroup FindVirtualGroupByTile(string tilePath)
        {
            return LoadVirtualLayout().Groups.FirstOrDefault(g => !String.IsNullOrEmpty(g.TilePath) && g.TilePath.Equals(tilePath, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsVirtualMember(string path)
        {
            return LoadVirtualLayout().Groups.Any(g => g.Members.Any(m => m.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));
        }

        internal static string SerializeVirtualLayout(VirtualLayout layout) { return Json.Serialize(layout); }
        internal static VirtualLayout DeserializeVirtualLayout(string content) { return Json.Deserialize<VirtualLayout>(content); }

        [Conditional("TRACE_DRAG")]
        internal static void LogDrag(string message)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.AppendAllText(DragDiagnosticPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch { }
        }

        internal static void SaveOpenRequest(string groupId)
        {
            try { Directory.CreateDirectory(OpenRequestDirectory); File.WriteAllText(Path.Combine(OpenRequestDirectory, Guid.NewGuid().ToString("N") + ".request"), groupId ?? ""); } catch { }
        }

        internal static string[] TakeOpenRequests()
        {
            List<string> requests = new List<string>();
            try
            {
                if (!Directory.Exists(OpenRequestDirectory)) return requests.ToArray();
                foreach (string file in Directory.GetFiles(OpenRequestDirectory, "*.request"))
                {
                    try { string value = File.ReadAllText(file).Trim(); File.Delete(file); if (!String.IsNullOrEmpty(value)) requests.Add(value); } catch { }
                }
            }
            catch { }
            return requests.ToArray();
        }
    }

    internal sealed class SingleInstanceCommandWindow : NativeWindow, IDisposable
    {
        internal const string WindowCaption = "DesktopFolders.Direct.Command.v6";
        readonly Action<string> receiver;

        internal SingleInstanceCommandWindow(Action<string> commandReceiver)
        {
            receiver = commandReceiver;
            CreateParams parameters = new CreateParams { Caption = WindowCaption, ExStyle = Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE };
            CreateHandle(parameters);
            DataStore.LogDrag("COMMAND window=" + Handle);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Native.WM_COPYDATA)
            {
                try
                {
                    Native.COPYDATASTRUCT data = (Native.COPYDATASTRUCT)Marshal.PtrToStructure(message.LParam, typeof(Native.COPYDATASTRUCT));
                    string command = data.lpData == IntPtr.Zero ? null : Marshal.PtrToStringUni(data.lpData, Math.Max(0, data.cbData / 2)).TrimEnd('\0');
                    DataStore.LogDrag("COMMAND received=" + (command ?? "<null>"));
                    if (!String.IsNullOrWhiteSpace(command))
                    {
                        receiver(command.Trim()); message.Result = (IntPtr)1; return;
                    }
                }
                catch (Exception error) { DataStore.LogDrag("COMMAND receive failed=" + error.Message); }
            }
            base.WndProc(ref message);
        }

        internal static bool SendOpenGroup(string groupId)
        {
            if (String.IsNullOrWhiteSpace(groupId)) return false;
            IntPtr target = IntPtr.Zero;
            for (int attempt = 0; attempt < 30 && target == IntPtr.Zero; attempt++)
            {
                target = Native.FindWindow(null, WindowCaption);
                if (target == IntPtr.Zero) Thread.Sleep(60);
            }
            if (target == IntPtr.Zero) { DataStore.LogDrag("COMMAND window not found after retry"); return false; }
            IntPtr text = IntPtr.Zero, packet = IntPtr.Zero;
            try
            {
                text = Marshal.StringToHGlobalUni(groupId);
                Native.COPYDATASTRUCT data = new Native.COPYDATASTRUCT { dwData = (IntPtr)1, cbData = (groupId.Length + 1) * 2, lpData = text };
                packet = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.COPYDATASTRUCT)));
                Marshal.StructureToPtr(data, packet, false);
                bool accepted = Native.SendMessage(target, Native.WM_COPYDATA, IntPtr.Zero, packet) != IntPtr.Zero;
                DataStore.LogDrag("COMMAND sent=" + groupId + " accepted=" + accepted);
                return accepted;
            }
            catch (Exception error) { DataStore.LogDrag("COMMAND send failed=" + error.Message); return false; }
            finally { if (packet != IntPtr.Zero) Marshal.FreeHGlobal(packet); if (text != IntPtr.Zero) Marshal.FreeHGlobal(text); }
        }

        public void Dispose() { try { DestroyHandle(); } catch { } }
    }

    internal static class Native
    {
        internal const int WH_MOUSE_LL = 14;
        internal const int WM_LBUTTONDOWN = 0x0201;
        internal const int WM_LBUTTONUP = 0x0202;
        internal const int WM_MOUSEMOVE = 0x0200;
        internal const int WM_RBUTTONDOWN = 0x0204;
        internal const int WM_MBUTTONDOWN = 0x0207;
        internal const int WM_CANCELMODE = 0x001F;
        internal const int WM_COPYDATA = 0x004A;
        internal const int WS_EX_TRANSPARENT = 0x20;
        internal const int WS_EX_TOOLWINDOW = 0x80;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal const int VK_LBUTTON = 0x01;
        internal const byte VK_ESCAPE = 0x1B;
        internal const uint KEYEVENTF_KEYUP = 0x0002;
        internal const uint SHCNE_ASSOCCHANGED = 0x08000000;
        internal const uint SHCNE_UPDATEITEM = 0x00002000;
        internal const uint SHCNE_ATTRIBUTES = 0x00000800;
        internal const uint SHCNF_IDLIST = 0x0000;
        internal const uint SHCNF_PATHW = 0x0005;
        internal const uint GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int left; public int top; public int right; public int bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct MSLLHOOKSTRUCT { public POINT pt; public int mouseData; public int flags; public int time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] internal struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }
        internal delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
        internal delegate bool EnumWindowsDelegate(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int idHook, MouseHook callback, IntPtr module, uint thread);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] internal static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsDelegate callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int virtualKey);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] internal static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out RECT rectangle);
        [DllImport("user32.dll")] internal static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] internal static extern IntPtr GetModuleHandle(string moduleName);
        [DllImport("psapi.dll")] internal static extern bool EmptyWorkingSet(IntPtr process);
        [DllImport("shell32.dll")] internal static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
#if WINDOWS_COMPOSITION
        [StructLayout(LayoutKind.Sequential)] internal struct DispatcherQueueOptions { internal int dwSize; internal int threadType; internal int apartmentType; }
        [DllImport("coremessaging.dll", EntryPoint = "CreateDispatcherQueueController", CharSet = CharSet.Unicode)] internal static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, [MarshalAs(UnmanagedType.IUnknown)] out object controller);
#endif
    }

    internal static class ExplorerDesktop
    {
        internal static readonly string Desktop = FindUserDesktop();
        internal static readonly string[] DesktopRoots = FindDesktopRoots();
        internal static string ScanStatus = "Chưa quét Desktop";
        static IntPtr cachedListView;
        internal static IntPtr CachedListView { get { return cachedListView; } }

        internal static bool IsDesktopForeground()
        {
            IntPtr list = cachedListView == IntPtr.Zero ? GetListView() : cachedListView;
            if (list == IntPtr.Zero) return false;
            IntPtr root = Native.GetAncestor(list, Native.GA_ROOT);
            return Native.GetForegroundWindow() == root;
        }

        internal static bool IsPointOnDesktop(Point point)
        {
            IntPtr list = cachedListView == IntPtr.Zero ? GetListView() : cachedListView;
            Native.RECT rectangle;
            return list != IntPtr.Zero && Native.GetWindowRect(list, out rectangle) && new Rectangle(rectangle.left, rectangle.top, rectangle.right - rectangle.left, rectangle.bottom - rectangle.top).Contains(point);
        }

        internal static bool IsPointOnDesktopSurface(Point point)
        {
            IntPtr list = cachedListView == IntPtr.Zero ? GetListView() : cachedListView;
            if (list == IntPtr.Zero) return false;
            Native.POINT nativePoint = new Native.POINT { x = point.X, y = point.Y };
            IntPtr underPointer = Native.WindowFromPoint(nativePoint);
            return underPointer == list || Native.IsChild(list, underPointer);
        }

        internal static bool IsAnyIconAtPoint(Point point)
        {
            try
            {
                IntPtr listHandle = cachedListView == IntPtr.Zero ? GetListView() : cachedListView;
                if (listHandle == IntPtr.Zero) return false;
                AutomationElement list = AutomationElement.FromHandle(listHandle);
                AutomationElementCollection children = list.FindAll(TreeScope.Children, Condition.TrueCondition);
                for (int i = 0; i < children.Count; i++)
                {
                    System.Windows.Rect r = children[i].Current.BoundingRectangle;
                    if (Rectangle.FromLTRB((int)r.Left, (int)r.Top, (int)r.Right, (int)r.Bottom).Contains(point)) return true;
                }
            }
            catch { }
            return false;
        }

        internal static bool TryGetIconBounds(string path, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            try
            {
                IntPtr listHandle = cachedListView == IntPtr.Zero ? GetListView() : cachedListView; if (listHandle == IntPtr.Zero) return false;
                cachedListView = listHandle; string expectedStem = Path.GetFileNameWithoutExtension(path ?? ""), expectedFile = Path.GetFileName(path ?? "");
                AutomationElement list = AutomationElement.FromHandle(listHandle); AutomationElementCollection children = list.FindAll(TreeScope.Children, Condition.TrueCondition);
                for (int i = 0; i < children.Count; i++)
                {
                    string name = children[i].Current.Name;
                    if (!String.Equals(name, expectedStem, StringComparison.OrdinalIgnoreCase) && !String.Equals(name, expectedFile, StringComparison.OrdinalIgnoreCase)) continue;
                    System.Windows.Rect rectangle = children[i].Current.BoundingRectangle; bounds = Rectangle.FromLTRB((int)rectangle.Left, (int)rectangle.Top, (int)rectangle.Right, (int)rectangle.Bottom); return bounds.Width > 0 && bounds.Height > 0;
                }
            }
            catch { }
            return false;
        }

        internal static void NotifyPathChanged(string path)
        {
            IntPtr item = IntPtr.Zero;
            try
            {
                if (!String.IsNullOrEmpty(path))
                {
                    item = Marshal.StringToHGlobalUni(path);
                    Native.SHChangeNotify(Native.SHCNE_ATTRIBUTES, Native.SHCNF_PATHW, item, IntPtr.Zero);
                    Native.SHChangeNotify(Native.SHCNE_UPDATEITEM, Native.SHCNF_PATHW, item, IntPtr.Zero);
                }
            }
            catch { }
            finally { if (item != IntPtr.Zero) Marshal.FreeHGlobal(item); }
        }

        internal static IntPtr GetListView()
        {
            IntPtr progman = Native.FindWindow("Progman", null);
            IntPtr defView = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
            {
                Native.EnumWindows(delegate(IntPtr window, IntPtr ignored) {
                    IntPtr candidate = Native.FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (candidate != IntPtr.Zero) defView = candidate;
                    return defView == IntPtr.Zero;
                }, IntPtr.Zero);
            }
            return defView == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        }

        static string FindUserDesktop()
        {
            string path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!String.IsNullOrWhiteSpace(path)) return path;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!String.IsNullOrWhiteSpace(oneDrive) && Directory.Exists(Path.Combine(oneDrive, "Desktop"))) return Path.Combine(oneDrive, "Desktop");
            return Path.Combine(profile, "Desktop");
        }

        static string[] FindDesktopRoots()
        {
            List<string> roots = new List<string>();
            string user = FindUserDesktop();
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!String.IsNullOrWhiteSpace(user)) roots.Add(user);
            if (!String.IsNullOrWhiteSpace(common) && !roots.Contains(common, StringComparer.OrdinalIgnoreCase)) roots.Add(common);
            return roots.ToArray();
        }

        internal static DesktopItem[] Scan()
        {
            List<DesktopItem> result = new List<DesktopItem>();
            try
            {
                IntPtr listHandle = GetListView();
                if (listHandle == IntPtr.Zero) { ScanStatus = "Không tìm thấy SysListView32 của Desktop Explorer"; return result.ToArray(); }
                cachedListView = listHandle;
                AutomationElement list = AutomationElement.FromHandle(listHandle);
                AutomationElementCollection children = list.FindAll(TreeScope.Children, Condition.TrueCondition);
                List<string> unsupported = new List<string>();
                VirtualLayout virtualLayout = DataStore.LoadVirtualLayout();
                HashSet<string> hiddenMembers = new HashSet<string>(virtualLayout.Groups.SelectMany(g => g.Members).Select(m => m.Path), StringComparer.OrdinalIgnoreCase);
                HashSet<string> groupTiles = new HashSet<string>(virtualLayout.Groups.Where(g => !String.IsNullOrEmpty(g.TilePath)).Select(g => g.TilePath), StringComparer.OrdinalIgnoreCase);
                Dictionary<string, string> pathIndex = BuildPathIndex();
                for (int i = 0; i < children.Count; i++)
                {
                    string name = children[i].Current.Name;
                    string path; pathIndex.TryGetValue((name ?? "").Trim(), out path);
                    if (String.IsNullOrEmpty(path)) { if (unsupported.Count < 6) unsupported.Add(name); continue; }
                    bool isGroup = File.Exists(path) && groupTiles.Contains(path);
                    bool isMergeable = File.Exists(path) && !isGroup && !hiddenMembers.Contains(path);
                    if (!isGroup && !isMergeable) { if (unsupported.Count < 6) unsupported.Add(name); continue; }
                    System.Windows.Rect r = children[i].Current.BoundingRectangle;
                    result.Add(new DesktopItem {
                        Name = name, Path = path,
                        Index = i,
                        Bounds = Rectangle.FromLTRB((int)r.Left, (int)r.Top, (int)r.Right, (int)r.Bottom),
                        IsGroup = isGroup,
                        IsShortcut = isMergeable
                    });
                }
                ScanStatus = "Explorer: " + children.Count + " icon | Nhận diện: " + result.Count + " | Chưa hỗ trợ: " + (children.Count - result.Count) +
                    "\r\nDesktop roots: " + String.Join(" ; ", DesktopRoots) +
                    (unsupported.Count == 0 ? "" : "\r\nVí dụ chưa hỗ trợ: " + String.Join(", ", unsupported.ToArray()));
            }
            catch (Exception error) { ScanStatus = "Lỗi quét Desktop: " + error.Message; }
            return result.ToArray();
        }

        internal static DesktopItem HitTest(DesktopItem[] snapshot, Point point, string excludedPath)
        {
            if (snapshot == null) return null;
            for (int i = snapshot.Length - 1; i >= 0; i--)
                if ((excludedPath == null || !snapshot[i].Path.Equals(excludedPath, StringComparison.OrdinalIgnoreCase)) && Rectangle.Inflate(snapshot[i].Bounds, 8, 8).Contains(point) && (snapshot[i].IsShortcut || snapshot[i].IsGroup)) return snapshot[i];
            return null;
        }

        static Dictionary<string, string> BuildPathIndex()
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string root in DesktopRoots)
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string candidate in Directory.GetFileSystemEntries(root))
                    {
                        string fileName = Path.GetFileName(candidate);
                        string stem = Path.GetFileNameWithoutExtension(candidate);
                        if (!String.IsNullOrEmpty(fileName) && !result.ContainsKey(fileName)) result[fileName] = candidate;
                        if (File.Exists(candidate) && !String.IsNullOrEmpty(stem) && !result.ContainsKey(stem)) result[stem] = candidate;
                    }
                }
            }
            catch { }
            return result;
        }

        internal static string NewGroupName()
        {
            string name = "New Folder"; int number = 2;
            while (File.Exists(Path.Combine(Desktop, name + ".lnk")) || File.Exists(Path.Combine(Desktop, name + ".desktopgroup")) || Directory.Exists(Path.Combine(Desktop, name))) name = "New Folder (" + number++ + ")";
            return name;
        }
    }

    internal static class IconLoader
    {
        static readonly object CacheLock = new object();
        static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        internal static Image ForPath(string path)
        {
            lock (CacheLock)
            {
                Bitmap cached;
                if (Cache.TryGetValue(path ?? "", out cached)) return new Bitmap(cached);
                Bitmap loaded = null;
                try { using (Icon icon = Icon.ExtractAssociatedIcon(path)) if (icon != null) loaded = icon.ToBitmap(); }
                catch { }
                if (loaded == null) loaded = SystemIcons.Application.ToBitmap();
                if (Cache.Count >= 256) { foreach (Bitmap image in Cache.Values) image.Dispose(); Cache.Clear(); }
                Cache[path ?? ""] = loaded;
                return new Bitmap(loaded);
            }
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkObject { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    internal static class GroupTileFactory
    {
        internal static void CreateOrUpdate(VirtualGroup group)
        {
            if (String.IsNullOrEmpty(group.IconPath)) group.IconPath = Path.Combine(DataStore.DataDirectory, "icons", group.Id + ".ico");
            Directory.CreateDirectory(Path.GetDirectoryName(group.IconPath));
            CreateCompositeIcon(group, group.IconPath);
            IShellLinkW link = (IShellLinkW)new ShellLinkObject();
            link.SetPath(Application.ExecutablePath);
            link.SetArguments("--open-group \"" + group.Id + "\"");
            link.SetDescription("Desktop Folders collection: " + group.Name);
            link.SetWorkingDirectory(Path.GetDirectoryName(Application.ExecutablePath));
            link.SetIconLocation(group.IconPath, 0);
            ((IPersistFile)link).Save(group.TilePath, false);
            ExplorerDesktop.NotifyPathChanged(group.TilePath);
        }

        static void CreateCompositeIcon(VirtualGroup group, string destination)
        {
            using (Bitmap bitmap = new Bitmap(256, 256))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                Rectangle body = new Rectangle(10, 10, 236, 236);
                using (System.Drawing.Drawing2D.GraphicsPath shape = Rounded(body, 46))
                // ICO alpha masks on some Explorer paths discard semi-transparent
                // background pixels. Keep the collection body opaque; only the area
                // outside the rounded shape remains transparent.
                using (Brush glass = new System.Drawing.Drawing2D.LinearGradientBrush(body, CollectionTheme.Control, CollectionTheme.ControlActive, 45f))
                    graphics.FillPath(glass, shape);
                using (Pen edge = new Pen(CollectionTheme.MutedText, 5))
                using (System.Drawing.Drawing2D.GraphicsPath shape = Rounded(body, 46)) graphics.DrawPath(edge, shape);

                VirtualMember[] members = group.Members.Where(m => File.Exists(m.Path))
                    .OrderByDescending(m => group.Pinned.Contains(m.Path, StringComparer.OrdinalIgnoreCase)).Take(9).ToArray();
                const int iconSize = 56, gap = 13, start = 31;
                for (int i = 0; i < members.Length; i++)
                {
                    using (Image icon = IconLoader.ForPath(members[i].Path))
                    {
                        int x = start + (i % 3) * (iconSize + gap), y = start + (i / 3) * (iconSize + gap);
                        graphics.DrawImage(icon, new Rectangle(x, y, iconSize, iconSize));
                    }
                }
                SavePngIcon(bitmap, destination);
            }
        }

        static void SavePngIcon(Bitmap bitmap, string destination)
        {
            // Icon.FromHandle(...).Save(...) can quantize the gradient and lose alpha.
            // A PNG-backed 256x256 ICO is supported by modern Windows Explorer and
            // preserves the tile exactly as rendered.
            using (MemoryStream png = new MemoryStream())
            {
                bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                byte[] payload = png.ToArray();
                using (BinaryWriter writer = new BinaryWriter(new FileStream(destination, FileMode.Create, FileAccess.Write)))
                {
                    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)0);
                    writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write(payload.Length); writer.Write(22); writer.Write(payload);
                }
            }
        }

        static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle rectangle, int radius)
        {
            int diameter = radius * 2; System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90); path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
        }
    }

    internal sealed class FolderPreviewOverlay : Form
    {
        DesktopItem target;
        Rectangle finalBounds;
        readonly System.Windows.Forms.Timer animation;
        int animationFrame;
        internal FolderPreviewOverlay()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; BackColor = Color.Magenta; TransparencyKey = Color.Magenta;
            animation = new System.Windows.Forms.Timer { Interval = 15 };
            animation.Tick += delegate { AnimateArmed(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE; return p; }
        }
        internal void Arm(DesktopItem item, bool reduceMotion)
        {
            target = item; finalBounds = Rectangle.Inflate(item.Bounds, 5, 5); animation.Stop(); animationFrame = reduceMotion ? 8 : 0;
            if (reduceMotion) Bounds = finalBounds;
            else
            {
                Rectangle start = Rectangle.Inflate(item.Bounds, -8, -8);
                Bounds = start.Width > 0 && start.Height > 0 ? start : item.Bounds;
            }
            Opacity = reduceMotion ? .96 : .22; Invalidate(); if (!Visible) Show();
            if (!reduceMotion) animation.Start();
        }
        internal void HideAnimated()
        {
            animation.Stop(); Hide(); target = null;
        }
        void AnimateArmed()
        {
            animationFrame++; double progress = Math.Min(1.0, animationFrame / 8.0);
            double eased = 1.0 - Math.Pow(1.0 - progress, 3.0);
            Rectangle start = target == null ? finalBounds : Rectangle.Inflate(target.Bounds, -8, -8);
            Bounds = new Rectangle(
                start.X + (int)Math.Round((finalBounds.X - start.X) * eased),
                start.Y + (int)Math.Round((finalBounds.Y - start.Y) * eased),
                start.Width + (int)Math.Round((finalBounds.Width - start.Width) * eased),
                start.Height + (int)Math.Round((finalBounds.Height - start.Height) * eased));
            Opacity = Math.Min(.96, .22 + (.74 * eased)); Invalidate();
            if (progress >= 1.0) animation.Stop();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(5, 5, Width - 11, Height - 11);
            using (Brush fill = new SolidBrush(Color.FromArgb(175, 52, 78, 112))) e.Graphics.FillRoundedRectangle(fill, r, 18);
            using (Pen edge = new Pen(Color.FromArgb(220, 170, 220, 255), 3)) e.Graphics.DrawRoundedRectangle(edge, r, 18);
            int tile = Math.Max(12, Math.Min(22, (r.Width - 30) / 2));
            for (int i = 0; i < 4; i++)
            {
                Rectangle mini = new Rectangle(r.X + 12 + (i % 2) * (tile + 7), r.Y + 12 + (i / 2) * (tile + 7), tile, tile);
                using (Brush app = new SolidBrush(i == 0 ? Color.FromArgb(220, 61, 160, 255) : Color.FromArgb(180, 122, 92, 246))) e.Graphics.FillRoundedRectangle(app, mini, 6);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellFolderNative
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr bindContext, [MarshalAs(UnmanagedType.LPWStr)] string displayName, ref uint eaten, out IntPtr itemIdList, ref uint attributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, uint flags, out IntPtr enumIdList);
        [PreserveSig] int BindToObject(IntPtr itemIdList, IntPtr bindContext, ref Guid interfaceId, out IntPtr result);
        [PreserveSig] int BindToStorage(IntPtr itemIdList, IntPtr bindContext, ref Guid interfaceId, out IntPtr result);
        [PreserveSig] int CompareIDs(IntPtr parameter, IntPtr first, IntPtr second);
        [PreserveSig] int CreateViewObject(IntPtr hwnd, ref Guid interfaceId, out IntPtr result);
        [PreserveSig] int GetAttributesOf(uint count, IntPtr[] itemIdLists, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(IntPtr hwnd, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] itemIdLists, ref Guid interfaceId, IntPtr reserved, [MarshalAs(UnmanagedType.Interface)] out object result);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContextMenuNative
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint firstCommand, uint lastCommand, uint flags);
        [PreserveSig] int InvokeCommand(ref ShellContextMenu.CommandInfo command);
        [PreserveSig] int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, IntPtr name, uint nameLength);
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContextMenu2Native
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint firstCommand, uint lastCommand, uint flags);
        [PreserveSig] int InvokeCommand(ref ShellContextMenu.CommandInfo command);
        [PreserveSig] int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, IntPtr name, uint nameLength);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContextMenu3Native
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint firstCommand, uint lastCommand, uint flags);
        [PreserveSig] int InvokeCommand(ref ShellContextMenu.CommandInfo command);
        [PreserveSig] int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, IntPtr name, uint nameLength);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    internal static class ShellContextMenu
    {
        const uint PinCommand = 1, MoveOutCommand = 2, ShellFirstCommand = 0x1000, ShellLastCommand = 0x7FFF;
        const uint MfByPosition = 0x00000400, MfString = 0, MfSeparator = 0x00000800;
        const uint TpmRightButton = 0x0002, TpmReturnCommand = 0x0100;
        const uint CmicMaskUnicode = 0x00004000, CmicMaskPointInvoke = 0x20000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct CommandInfo
        {
            internal int cbSize; internal uint fMask; internal IntPtr hwnd; internal IntPtr lpVerb;
            [MarshalAs(UnmanagedType.LPStr)] internal string lpParameters;
            [MarshalAs(UnmanagedType.LPStr)] internal string lpDirectory;
            internal int nShow; internal uint dwHotKey; internal IntPtr hIcon;
            [MarshalAs(UnmanagedType.LPStr)] internal string lpTitle;
            internal IntPtr lpVerbW;
            [MarshalAs(UnmanagedType.LPWStr)] internal string lpParametersW;
            [MarshalAs(UnmanagedType.LPWStr)] internal string lpDirectoryW;
            [MarshalAs(UnmanagedType.LPWStr)] internal string lpTitleW;
            internal Native.POINT ptInvoke;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr itemIdList, uint attributesIn, out uint attributesOut);
        [DllImport("shell32.dll")] static extern int SHBindToParent(IntPtr itemIdList, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellFolderNative parent, out IntPtr childItemIdList);
        [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool InsertMenu(IntPtr menu, uint position, uint flags, UIntPtr command, string text);
        [DllImport("user32.dll")] static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
        [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);

        internal static void Show(FolderPanel owner, string path, string pinText, Action pinAction, Action moveOutAction)
        {
            if (owner == null || owner.IsDisposed || String.IsNullOrEmpty(path)) return;
            IntPtr absolute = IntPtr.Zero, menu = IntPtr.Zero; IShellFolderNative parent = null; object rawContext = null;
            try
            {
                uint attributes; if (SHParseDisplayName(path, IntPtr.Zero, out absolute, 0, out attributes) < 0 || absolute == IntPtr.Zero) throw new InvalidOperationException("Shell item unavailable.");
                Guid folderId = typeof(IShellFolderNative).GUID; IntPtr child; if (SHBindToParent(absolute, ref folderId, out parent, out child) < 0 || parent == null) throw new InvalidOperationException("Shell parent unavailable.");
                Guid contextId = typeof(IContextMenuNative).GUID; if (parent.GetUIObjectOf(owner.Handle, 1, new IntPtr[] { child }, ref contextId, IntPtr.Zero, out rawContext) < 0 || rawContext == null) throw new InvalidOperationException("Shell context menu unavailable.");
                IContextMenuNative context = (IContextMenuNative)rawContext; menu = CreatePopupMenu(); if (menu == IntPtr.Zero) throw new InvalidOperationException("Popup menu unavailable.");
                InsertMenu(menu, 0, MfByPosition | MfString, (UIntPtr)PinCommand, pinText); InsertMenu(menu, 1, MfByPosition | MfString, (UIntPtr)MoveOutCommand, "Đưa ra Desktop"); InsertMenu(menu, 2, MfByPosition | MfSeparator, UIntPtr.Zero, null);
                context.QueryContextMenu(menu, 3, ShellFirstCommand, ShellLastCommand, 0);
                using (ShellMenuMessageWindow messages = new ShellMenuMessageWindow(owner.Handle, rawContext))
                {
                    Point point = Cursor.Position; DataStore.LogDrag("SHELL_MENU native path=" + path); uint selected = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCommand, point.X, point.Y, owner.Handle, IntPtr.Zero); DataStore.LogDrag("SHELL_MENU selected=" + selected);
                    if (selected == PinCommand) { if (pinAction != null) pinAction(); }
                    else if (selected == MoveOutCommand) { if (moveOutAction != null) moveOutAction(); }
                    else if (selected >= ShellFirstCommand && selected <= ShellLastCommand)
                    {
                        IntPtr verb = (IntPtr)(selected - ShellFirstCommand); CommandInfo command = new CommandInfo { cbSize = Marshal.SizeOf(typeof(CommandInfo)), fMask = CmicMaskUnicode | CmicMaskPointInvoke, hwnd = owner.Handle, lpVerb = verb, lpVerbW = verb, nShow = 1, ptInvoke = new Native.POINT { x = point.X, y = point.Y } };
                        context.InvokeCommand(ref command);
                    }
                }
            }
            catch (Exception error) { DataStore.LogDrag("SHELL_MENU fallback=" + error); ShowFallback(owner, path, pinText, pinAction, moveOutAction); }
            finally
            {
                if (menu != IntPtr.Zero) DestroyMenu(menu); if (rawContext != null && Marshal.IsComObject(rawContext)) Marshal.FinalReleaseComObject(rawContext); if (parent != null && Marshal.IsComObject(parent)) Marshal.FinalReleaseComObject(parent); if (absolute != IntPtr.Zero) Marshal.FreeCoTaskMem(absolute);
            }
        }
        static void ShowFallback(FolderPanel owner, string path, string pinText, Action pinAction, Action moveOutAction)
        {
            ContextMenuStrip fallback = new ContextMenuStrip(); fallback.Items.Add(pinText, null, delegate { if (pinAction != null) pinAction(); }); fallback.Items.Add("Đưa ra Desktop", null, delegate { if (moveOutAction != null) moveOutAction(); }); fallback.Items.Add(new ToolStripSeparator()); fallback.Items.Add("Mở", null, delegate { try { Process.Start(path); } catch { } }); fallback.Closed += delegate { fallback.Dispose(); }; fallback.Show(owner, owner.PointToClient(Cursor.Position));
        }

        sealed class ShellMenuMessageWindow : NativeWindow, IDisposable
        {
            readonly IContextMenu2Native menu2;
            readonly IContextMenu3Native menu3;
            internal ShellMenuMessageWindow(IntPtr handle, object context)
            {
                try { menu3 = context as IContextMenu3Native; } catch { }
                try { menu2 = context as IContextMenu2Native; } catch { }
                AssignHandle(handle);
            }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0117 || message.Msg == 0x002B || message.Msg == 0x002C || message.Msg == 0x0120)
                {
                    if (menu3 != null) { IntPtr result; if (menu3.HandleMenuMsg2((uint)message.Msg, message.WParam, message.LParam, out result) >= 0) { message.Result = result; return; } }
                    if (menu2 != null && menu2.HandleMenuMsg((uint)message.Msg, message.WParam, message.LParam) >= 0) { message.Result = IntPtr.Zero; return; }
                }
                base.WndProc(ref message);
            }
            public void Dispose() { try { ReleaseHandle(); } catch { } }
        }
    }

    internal sealed class AppTile : Panel
    {
        readonly Image icon;
        readonly string label;
        readonly bool pinned;
        readonly bool gridMode;
        readonly Color accent;
        readonly bool reduceMotion;
        System.Windows.Forms.Timer hoverAnimation;
        float hoverProgress;
        bool hot;
        bool dragOver;
        bool dragging;
        internal string ItemPath { get; private set; }

        internal AppTile(string path, bool isPinned, bool grid, int width, bool reducedMotion)
        {
            ItemPath = path; label = Path.GetFileNameWithoutExtension(path); pinned = isPinned; gridMode = grid; icon = IconLoader.ForPath(path);
            accent = AccentFromIcon(icon); reduceMotion = reducedMotion;
            Size = grid ? new Size(width, width < 170 ? 106 : 134) : new Size(width, 64); BackColor = Color.Transparent; Cursor = Cursors.Hand;
            TabStop = true; AccessibleRole = AccessibleRole.ListItem; AccessibleName = label; AccessibleDescription = pinned ? "Đã ghim ưu tiên" : "";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Focus(); base.OnMouseDown(e); }
        protected override void OnMouseEnter(EventArgs e) { hot = true; BeginHoverTransition(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; BeginHoverTransition(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { BeginHoverTransition(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { BeginHoverTransition(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        internal void SetDragVisual(bool isDragging, bool isDropTarget) { dragging = isDragging; dragOver = isDropTarget; BeginHoverTransition(); }
        void BeginHoverTransition()
        {
            float target = hot || dragOver || Focused ? 1f : 0f;
            if (reduceMotion) { hoverProgress = target; Invalidate(); return; }
            if (hoverAnimation == null) { hoverAnimation = new System.Windows.Forms.Timer { Interval = 15 }; hoverAnimation.Tick += delegate { AnimateHover(); }; }
            hoverAnimation.Stop(); hoverAnimation.Start(); Invalidate();
        }
        void AnimateHover()
        {
            float target = hot || dragOver || Focused ? 1f : 0f; hoverProgress += (target - hoverProgress) * .34f;
            if (Math.Abs(target - hoverProgress) < .018f) { hoverProgress = target; hoverAnimation.Stop(); }
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float interaction = Math.Max(0f, Math.Min(1f, hoverProgress)); int inset = 3 - (int)Math.Round(interaction * 2f);
            int maximumCardHeight = gridMode ? (Width < 170 ? 76 : 100) : Height - 1;
            Rectangle card = new Rectangle(inset, inset, Math.Max(8, Width - inset * 2 - 1), Math.Max(8, maximumCardHeight - (inset - 1) * 2));
            Color top = Mix(CollectionTheme.Surface, accent, .23f + .11f * interaction);
            Color bottom = Mix(CollectionTheme.Surface, accent, .08f + .07f * interaction);
            using (System.Drawing.Drawing2D.LinearGradientBrush fill = new System.Drawing.Drawing2D.LinearGradientBrush(card, top, bottom, 90f)) e.Graphics.FillRoundedRectangle(fill, card, CollectionTheme.RadiusCard);
            Rectangle glow = new Rectangle(card.X + card.Width / 5, card.Y + card.Height / 5, card.Width * 3 / 5, card.Height * 3 / 5);
            using (System.Drawing.Drawing2D.GraphicsPath glowPath = new System.Drawing.Drawing2D.GraphicsPath())
            {
                glowPath.AddEllipse(glow);
                using (System.Drawing.Drawing2D.PathGradientBrush radial = new System.Drawing.Drawing2D.PathGradientBrush(glowPath))
                {
                    radial.CenterColor = Color.FromArgb((int)(42 + 34 * interaction), accent); radial.SurroundColors = new Color[] { Color.FromArgb(0, accent) }; e.Graphics.FillPath(radial, glowPath);
                }
            }
            Color idleEdge = Mix(CollectionTheme.Stroke, accent, .34f); Color edgeColor = Mix(idleEdge, accent, .72f * interaction);
            using (Pen edge = new Pen(edgeColor, dragOver ? 2f : 1f + .35f * interaction)) e.Graphics.DrawRoundedRectangle(edge, card, CollectionTheme.RadiusCard);
            if (Focused) using (Pen focus = new Pen(CollectionTheme.Focus, 2f)) e.Graphics.DrawRoundedRectangle(focus, Rectangle.Inflate(card, -2, -2), CollectionTheme.RadiusCard - 2);
            if (pinned)
            {
                Rectangle badge = new Rectangle(card.Right - 21, card.Top + 6, 15, 15);
                using (Brush badgeFill = new SolidBrush(Color.FromArgb(185, 8, 9, 12))) e.Graphics.FillEllipse(badgeFill, badge);
                using (Pen badgeEdge = new Pen(Color.FromArgb(75, 255, 255, 255))) e.Graphics.DrawEllipse(badgeEdge, badge);
                using (Brush star = new SolidBrush(CollectionTheme.Pin))
                using (System.Drawing.Drawing2D.GraphicsPath starShape = StarPath(new PointF(badge.X + 7.5f, badge.Y + 7.5f), 4.3f, 2.0f)) e.Graphics.FillPath(star, starShape);
            }
            if (gridMode && Width < 170)
            {
                int iconSize = 42 + (int)Math.Round(3 * interaction), iconX = (Width - iconSize) / 2, iconY = card.Y + (card.Height - iconSize) / 2;
                if (icon != null) e.Graphics.DrawImage(icon, new Rectangle(iconX, iconY, iconSize, iconSize));
                using (Font name = new Font("Segoe UI", 9f, FontStyle.Regular))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
                using (Brush text = new SolidBrush(Mix(CollectionTheme.SecondaryText, CollectionTheme.Text, interaction))) e.Graphics.DrawString(label, name, text, new Rectangle(4, 82, Width - 8, 20), format);
            }
            else if (gridMode)
            {
                int iconSize = 56 + (int)Math.Round(4 * interaction), iconX = (Width - iconSize) / 2, iconY = card.Y + (card.Height - iconSize) / 2;
                if (icon != null) e.Graphics.DrawImage(icon, new Rectangle(iconX, iconY, iconSize, iconSize));
                using (Font name = new Font("Segoe UI", 10f, FontStyle.Regular))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
                using (Brush text = new SolidBrush(Mix(CollectionTheme.SecondaryText, CollectionTheme.Text, interaction))) e.Graphics.DrawString(label, name, text, new Rectangle(6, 108, Width - 12, 22), format);
            }
            else
            {
                int iconSize = 42 + (int)Math.Round(3 * interaction), iconY = (Height - iconSize) / 2;
                if (icon != null) e.Graphics.DrawImage(icon, new Rectangle(12, iconY, iconSize, iconSize));
                using (Font name = new Font("Segoe UI", 10f, FontStyle.Bold))
                using (StringFormat format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
                using (Brush text = new SolidBrush(CollectionTheme.Text)) e.Graphics.DrawString(label, name, text, new Rectangle(68, 6, Math.Max(40, Width - 84), 50), format);
            }
            if (dragging) using (Brush veil = new SolidBrush(Color.FromArgb(118, CollectionTheme.Background))) e.Graphics.FillRoundedRectangle(veil, card, CollectionTheme.RadiusCard);
        }
        static Color Mix(Color background, Color foreground, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb((int)(background.R + (foreground.R - background.R) * amount), (int)(background.G + (foreground.G - background.G) * amount), (int)(background.B + (foreground.B - background.B) * amount));
        }
        static Color AccentFromIcon(Image image)
        {
            try
            {
                using (Bitmap sample = new Bitmap(image, new Size(20, 20)))
                {
                    long r = 0, g = 0, b = 0, weight = 0;
                    for (int y = 0; y < sample.Height; y += 2) for (int x = 0; x < sample.Width; x += 2)
                    {
                        Color c = sample.GetPixel(x, y); if (c.A < 90) continue;
                        int spread = Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)); int w = Math.Max(1, spread);
                        r += c.R * w; g += c.G * w; b += c.B * w; weight += w;
                    }
                    if (weight > 0) return Color.FromArgb(Math.Max(55, Math.Min(230, (int)(r / weight))), Math.Max(55, Math.Min(230, (int)(g / weight))), Math.Max(55, Math.Min(230, (int)(b / weight))));
                }
            }
            catch { }
            return CollectionTheme.Focus;
        }
        static System.Drawing.Drawing2D.GraphicsPath StarPath(PointF center, float outer, float inner)
        {
            System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath(); PointF[] points = new PointF[10];
            for (int i = 0; i < 10; i++) { double angle = -Math.PI / 2 + i * Math.PI / 5; float radius = (i % 2 == 0) ? outer : inner; points[i] = new PointF(center.X + (float)Math.Cos(angle) * radius, center.Y + (float)Math.Sin(angle) * radius); }
            path.AddPolygon(points); return path;
        }
        protected override void Dispose(bool disposing) { if (disposing) { if (hoverAnimation != null) hoverAnimation.Dispose(); if (icon != null) icon.Dispose(); } base.Dispose(disposing); }
    }

    internal static class CollectionTheme
    {
        internal static readonly Color Background = Color.FromArgb(10, 11, 14);
        internal static readonly Color Surface = Color.FromArgb(16, 18, 24);
        internal static readonly Color SurfaceHover = Color.FromArgb(27, 30, 39);
        internal static readonly Color Control = Color.FromArgb(16, 18, 24);
        internal static readonly Color ControlActive = Color.FromArgb(31, 34, 44);
        internal static readonly Color Border = Color.FromArgb(39, 42, 52);
        internal static readonly Color Stroke = Color.FromArgb(33, 36, 45);
        internal static readonly Color Focus = Color.FromArgb(124, 147, 255);
        internal static readonly Color Text = Color.FromArgb(245, 246, 248);
        internal static readonly Color SecondaryText = Color.FromArgb(182, 185, 194);
        internal static readonly Color MutedText = Color.FromArgb(122, 126, 138);
        internal static readonly Color Pin = Color.FromArgb(255, 215, 106);
        internal static readonly Color Danger = Color.FromArgb(255, 84, 84);
        internal const int RadiusWindow = 22;
        internal const int RadiusControl = 12;
        internal const int RadiusCard = 16;
    }

    internal sealed class SectionHeaderControl : Control
    {
        readonly string label;
        readonly int count;
        internal SectionHeaderControl(string sectionLabel, int itemCount, int width)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            label = sectionLabel.ToUpperInvariant(); count = itemCount; Size = new Size(width, 28); BackColor = Color.Transparent; TabStop = false; Margin = new Padding(2, 6, 2, 3);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (Brush accent = new SolidBrush(CollectionTheme.Focus)) e.Graphics.FillEllipse(accent, 1, 10, 5, 5);
            float labelWidth;
            using (Font heading = new Font("Segoe UI", 7.8f, FontStyle.Bold))
            using (Brush text = new SolidBrush(CollectionTheme.SecondaryText)) { e.Graphics.DrawString(label, heading, text, new PointF(13, 7)); labelWidth = e.Graphics.MeasureString(label, heading).Width; }
            string value = count.ToString(); SizeF measured;
            using (Font number = new Font("Segoe UI", 7.5f, FontStyle.Bold)) measured = e.Graphics.MeasureString(value, number);
            RectangleF pill = new RectangleF(Math.Min(Width - 28, 19 + labelWidth), 6, Math.Max(21, measured.Width + 10), 17);
            using (Brush fill = new SolidBrush(CollectionTheme.ControlActive)) e.Graphics.FillRoundedRectangle(fill, Rectangle.Round(pill), 8);
            using (Font number = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (Brush muted = new SolidBrush(CollectionTheme.MutedText)) e.Graphics.DrawString(value, number, muted, pill, format);
        }
    }

    internal enum HeaderIconKind { Close, Expand, Collapse, Grid, List }

    internal sealed class HeaderIconButton : Button
    {
        HeaderIconKind kind;
        bool hot;
        bool selected;
        internal HeaderIconKind IconKind { get { return kind; } set { kind = value; Invalidate(); } }
        internal bool Selected { get { return selected; } set { selected = value; AccessibleDescription = value ? "Đang chọn" : ""; Invalidate(); } }
        internal HeaderIconButton(HeaderIconKind iconKind, int width)
        {
            kind = iconKind; Width = width; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Color.Transparent; ForeColor = CollectionTheme.SecondaryText; Cursor = Cursors.Hand; TabStop = true; Text = ""; AccessibleRole = AccessibleRole.PushButton; AccessibleName = iconKind.ToString();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            Rectangle box = new Rectangle(4, 5, Math.Max(10, Width - 8), Math.Max(10, Height - 10));
            Color background = hot || Focused ? (kind == HeaderIconKind.Close ? Color.FromArgb(48, CollectionTheme.Danger) : CollectionTheme.ControlActive) : (selected ? Color.FromArgb(35, CollectionTheme.Focus) : Color.Transparent);
            Color glyph = selected ? CollectionTheme.Focus : (hot ? CollectionTheme.Text : ForeColor);
            if (background.A > 0) using (Brush fill = new SolidBrush(background)) e.Graphics.FillRoundedRectangle(fill, box, 8);
            float cx = Width / 2f, cy = Height / 2f;
            using (Pen pen = new Pen(kind == HeaderIconKind.Close && hot ? Color.FromArgb(255, 128, 128) : glyph, 1.6f))
            {
                pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                if (kind == HeaderIconKind.Close)
                {
                    e.Graphics.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5); e.Graphics.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
                }
                else if (kind == HeaderIconKind.Expand)
                {
                    e.Graphics.DrawLines(pen, new PointF[] { new PointF(cx - 2, cy - 7), new PointF(cx - 7, cy - 7), new PointF(cx - 7, cy - 2) });
                    e.Graphics.DrawLines(pen, new PointF[] { new PointF(cx + 2, cy - 7), new PointF(cx + 7, cy - 7), new PointF(cx + 7, cy - 2) });
                    e.Graphics.DrawLines(pen, new PointF[] { new PointF(cx - 7, cy + 2), new PointF(cx - 7, cy + 7), new PointF(cx - 2, cy + 7) });
                    e.Graphics.DrawLines(pen, new PointF[] { new PointF(cx + 7, cy + 2), new PointF(cx + 7, cy + 7), new PointF(cx + 2, cy + 7) });
                }
                else if (kind == HeaderIconKind.Collapse)
                {
                    e.Graphics.DrawRectangle(pen, cx - 7, cy - 7, 10, 10); e.Graphics.DrawRectangle(pen, cx - 3, cy - 3, 10, 10);
                }
                else if (kind == HeaderIconKind.Grid)
                {
                    for (int row = 0; row < 3; row++) for (int column = 0; column < 3; column++) e.Graphics.DrawRectangle(pen, cx - 7 + column * 6, cy - 7 + row * 6, 3, 3);
                }
                else
                {
                    using (Brush dot = new SolidBrush(ForeColor)) for (int row = 0; row < 3; row++) { float y = cy - 6 + row * 6; e.Graphics.FillEllipse(dot, cx - 8, y - 1, 2, 2); e.Graphics.DrawLine(pen, cx - 3, y, cx + 8, y); }
                }
            }
        }
    }

    internal sealed class SearchGlyphControl : Control
    {
        internal SearchGlyphControl()
        {
            BackColor = CollectionTheme.Control; ForeColor = CollectionTheme.MutedText; TabStop = false; AccessibleRole = AccessibleRole.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float cx = Width / 2f - 2, cy = Height / 2f - 2;
            using (Pen pen = new Pen(ForeColor, 1.8f)) { pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round; e.Graphics.DrawEllipse(pen, cx - 7, cy - 7, 13, 13); e.Graphics.DrawLine(pen, cx + 4, cy + 4, cx + 10, cy + 10); }
        }
    }

#if WINDOWS_COMPOSITION
    // Windows Composition is deliberately isolated from the rest of the WinForms UI.
    // It is a best-effort visual cue: the collection itself is placed at its final
    // anchor before it is shown, so a compositor failure can never reintroduce a
    // late or stale-position popup.
    [ComImport]
    [Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(IntPtr hwndTarget, bool isTopmost, out IntPtr target);
    }

    [ComImport]
    [Guid("A1BEA8BA-D726-4663-8129-6B5E7927FFA6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
    internal interface ICompositionTargetInterop
    {
        Visual Root { get; set; }
    }

    internal sealed class CompositionMotionOverlay : Form
    {
        static readonly object DispatcherLock = new object();
        static object sharedDispatcherQueue;
        static bool dispatcherAttempted;
        readonly Rectangle sourceBounds;
        readonly Rectangle destinationBounds;
        Action completed;
        object dispatcherQueue;
        Compositor compositor;
        ICompositionTargetInterop target;
        ContainerVisual root;
        SpriteVisual token;
        System.Windows.Forms.Timer fallback;
        bool finished;

        internal CompositionMotionOverlay(Rectangle source, Rectangle destination, Action onCompleted)
        {
            sourceBounds = Normalize(source); destinationBounds = Normalize(destination); completed = onCompleted;
            Rectangle union = Rectangle.Union(sourceBounds, destinationBounds); union.Inflate(4, 4);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; BackColor = Color.Magenta; TransparencyKey = Color.Magenta; Bounds = union;
            Shown += delegate { BeginComposition(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT; return p; } }
        static Rectangle Normalize(Rectangle value) { return new Rectangle(value.X, value.Y, Math.Max(28, value.Width), Math.Max(28, value.Height)); }
        void BeginComposition()
        {
            try
            {
                EnsureDispatcherQueue(); dispatcherQueue = sharedDispatcherQueue;
                compositor = new Compositor();
                ICompositorDesktopInterop interop = (ICompositorDesktopInterop)(object)compositor;
                IntPtr rawTarget; interop.CreateDesktopWindowTarget(Handle, true, out rawTarget);
                if (rawTarget == IntPtr.Zero) throw new InvalidOperationException("Windows Composition target unavailable.");
                try { target = (ICompositionTargetInterop)Marshal.GetObjectForIUnknown(rawTarget); }
                finally { Marshal.Release(rawTarget); }
                root = compositor.CreateContainerVisual(); root.RelativeSizeAdjustment = new Vector2(1.0f, 1.0f); target.Root = root;
                token = compositor.CreateSpriteVisual();
                token.Brush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(46, CollectionTheme.Focus.R, CollectionTheme.Focus.G, CollectionTheme.Focus.B));
                token.Opacity = 0.0f;
                token.Size = new Vector2(sourceBounds.Width, sourceBounds.Height);
                token.Offset = new Vector3(sourceBounds.Left - Left, sourceBounds.Top - Top, 0);
                root.Children.InsertAtTop(token);

                Vector3 sourceOffset = new Vector3(sourceBounds.Left - Left, sourceBounds.Top - Top, 0);
                Vector3 destinationOffset = new Vector3(destinationBounds.Left - Left, destinationBounds.Top - Top, 0);
                Vector2 sourceSize = new Vector2(sourceBounds.Width, sourceBounds.Height);
                Vector2 destinationSize = new Vector2(destinationBounds.Width, destinationBounds.Height);
                CubicBezierEasingFunction easeOut = compositor.CreateCubicBezierEasingFunction(new Vector2(0.12f, 0.0f), new Vector2(0.0f, 1.0f));
                Vector3KeyFrameAnimation offset = compositor.CreateVector3KeyFrameAnimation(); offset.InsertKeyFrame(0.0f, sourceOffset); offset.InsertKeyFrame(1.0f, destinationOffset, easeOut); offset.Duration = TimeSpan.FromMilliseconds(140);
                Vector2KeyFrameAnimation size = compositor.CreateVector2KeyFrameAnimation(); size.InsertKeyFrame(0.0f, sourceSize); size.InsertKeyFrame(1.0f, destinationSize, easeOut); size.Duration = offset.Duration;
                ScalarKeyFrameAnimation opacity = compositor.CreateScalarKeyFrameAnimation(); opacity.InsertKeyFrame(0.0f, 0.0f); opacity.InsertKeyFrame(0.10f, 0.20f); opacity.InsertKeyFrame(0.72f, 0.10f, easeOut); opacity.InsertKeyFrame(1.0f, 0.0f, easeOut); opacity.Duration = offset.Duration;
                token.StartAnimation("Offset", offset); token.StartAnimation("Size", size); token.StartAnimation("Opacity", opacity);
                // The compositor owns the visual frames. The UI timer only retires
                // the transparent host after the 140 ms timeline has completed.
                fallback = new System.Windows.Forms.Timer { Interval = 155 }; fallback.Tick += delegate { fallback.Stop(); Finish(); }; fallback.Start();
            }
            catch
            {
                // Old Windows builds and remote sessions may not expose a Composition
                // target. Finish quietly; opening still remains synchronous.
                fallback = new System.Windows.Forms.Timer { Interval = 1 }; fallback.Tick += delegate { fallback.Stop(); Finish(); }; fallback.Start();
            }
        }
        static void EnsureDispatcherQueue()
        {
            lock (DispatcherLock)
            {
                if (dispatcherAttempted) return;
                dispatcherAttempted = true;
                Native.DispatcherQueueOptions options = new Native.DispatcherQueueOptions { dwSize = Marshal.SizeOf(typeof(Native.DispatcherQueueOptions)), threadType = 1, apartmentType = 2 };
                object controller; int result = Native.CreateDispatcherQueueController(options, out controller);
                if (result < 0 || controller == null) throw new InvalidOperationException("Windows Composition dispatcher unavailable.");
                sharedDispatcherQueue = controller;
            }
        }
        internal void CancelCue() { Finish(); }
        void Finish()
        {
            if (finished) return; finished = true;
            if (fallback != null) { fallback.Stop(); fallback.Dispose(); fallback = null; }
            try { Hide(); } catch { }
            Action done = completed; completed = null; if (done != null) done();
            try { Dispose(); } catch { }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (token != null) { token.Dispose(); token = null; }
                if (root != null) { root.Dispose(); root = null; }
                if (target != null) { Marshal.ReleaseComObject(target); target = null; }
                if (compositor != null) { compositor.Dispose(); compositor = null; }
            }
            base.Dispose(disposing);
        }
    }
#else
    internal sealed class CompositionMotionOverlay : Form
    {
        readonly Action completed;
        internal CompositionMotionOverlay(Rectangle source, Rectangle destination, Action onCompleted) { completed = onCompleted; ShowInTaskbar = false; }
        internal void CancelCue() { if (!IsDisposed) Close(); }
        protected override void OnShown(EventArgs e) { base.OnShown(e); if (completed != null) completed(); Close(); }
    }
#endif

    internal sealed class WindowMorphOverlay : Form
    {
        readonly Bitmap snapshot;
        readonly Rectangle startBounds;
        readonly Rectangle endBounds;
        readonly int duration;
        readonly Action completed;
        readonly System.Windows.Forms.Timer timer;
        Rectangle currentBounds;
        int started;

        internal WindowMorphOverlay(Bitmap image, Rectangle from, Rectangle to, int milliseconds, Action onCompleted)
        {
            snapshot = image; startBounds = Normalize(from); endBounds = Normalize(to); currentBounds = startBounds; duration = Math.Max(70, milliseconds); completed = onCompleted;
            Rectangle union = Rectangle.Union(startBounds, endBounds); union.Inflate(3, 3);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; BackColor = Color.Magenta; TransparencyKey = Color.Magenta; Bounds = union; DoubleBuffered = true;
            timer = new System.Windows.Forms.Timer { Interval = 12 };
            timer.Tick += delegate { AnimateFrame(); };
            Shown += delegate { started = Environment.TickCount; timer.Start(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT; return p; } }
        static Rectangle Normalize(Rectangle rectangle) { return new Rectangle(rectangle.X, rectangle.Y, Math.Max(48, rectangle.Width), Math.Max(48, rectangle.Height)); }
        void AnimateFrame()
        {
            double progress = Math.Min(1.0, unchecked(Environment.TickCount - started) / (double)duration);
            long startArea = (long)startBounds.Width * startBounds.Height, endArea = (long)endBounds.Width * endBounds.Height;
            double eased = endArea < startArea ? progress * progress : 1.0 - Math.Pow(1.0 - progress, 3.0);
            currentBounds = new Rectangle(
                startBounds.X + (int)Math.Round((endBounds.X - startBounds.X) * eased),
                startBounds.Y + (int)Math.Round((endBounds.Y - startBounds.Y) * eased),
                startBounds.Width + (int)Math.Round((endBounds.Width - startBounds.Width) * eased),
                startBounds.Height + (int)Math.Round((endBounds.Height - startBounds.Height) * eased));
            Invalidate();
            if (progress >= 1.0)
            {
                timer.Stop(); Hide(); Action done = completed; if (done != null) done(); Dispose();
            }
        }
        internal void CancelMorph() { if (IsDisposed) return; timer.Stop(); Hide(); Dispose(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Magenta); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            Rectangle destination = new Rectangle(currentBounds.X - Left, currentBounds.Y - Top, currentBounds.Width, currentBounds.Height);
            int radius = Math.Max(8, Math.Min(CollectionTheme.RadiusWindow, Math.Min(destination.Width, destination.Height) / 5));
            using (System.Drawing.Drawing2D.GraphicsPath clip = DrawExtensions.RoundPath(destination, radius)) { e.Graphics.SetClip(clip); e.Graphics.DrawImage(snapshot, destination); e.Graphics.ResetClip(); }
        }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); snapshot.Dispose(); } base.Dispose(disposing); }
    }

    internal sealed class TileMotion
    {
        internal AppTile Tile;
        internal Point From;
        internal Point To;
    }

    internal sealed class FolderPanel : Form
    {
        internal const string SourceGroupFormat = "DesktopFolders.SourceGroup.v1";
        internal const string MemberPathFormat = "DesktopFolders.MemberPath.v1";
        internal static event Action LayoutChanged;
        internal static event Action<Point> DesktopPointerDown;
        static readonly object OpenPanelsLock = new object();
        static readonly Dictionary<string, WeakReference> OpenPanels = new Dictionary<string, WeakReference>(StringComparer.OrdinalIgnoreCase);

        readonly AppSettings settings;
        readonly string groupId;
        readonly Panel appsViewport;
        readonly FlowLayoutPanel apps;
        readonly DarkScrollBar themedScroll;
        readonly TextBox search;
        readonly Label searchHint;
        readonly Label titleLabel;
        readonly TextBox titleEditor;
        readonly HeaderIconButton gridButton;
        readonly HeaderIconButton listButton;
        readonly HeaderIconButton expandButton;
        readonly ToolTip tooltips;
        VirtualLayout layout;
        VirtualGroup group;
        bool gridMode = true;
        bool expanded;
        Size compactSize = new Size(440, 520);
        Size expandedSize = new Size(840, 600);
        Size finalSize;
        Point finalLocation;
        Rectangle anchorBounds;
        Rectangle animationOrigin;
        WindowMorphOverlay activeMorph;
        CompositionMotionOverlay activeCompositionCue;
        System.Windows.Forms.Timer releaseTopMost;
        System.Windows.Forms.Timer reorderAnimation;
        List<TileMotion> reorderMotions;
        int reorderAnimationStarted;
        bool reorderLayoutSuspended;
        bool gridDragActive;
        bool gridDragCommitted;
        string gridDragSourcePath;
        string gridPreviewTargetPath;
        bool gridPreviewAfter;

        internal static void ShowOrActivate(string tilePath, Rectangle source, AppSettings currentSettings)
        {
            Rectangle live;
            if (ExplorerDesktop.TryGetIconBounds(tilePath, out live)) source = live;
            VirtualGroup requested = DataStore.LoadVirtualLayout().Groups.FirstOrDefault(g => !String.IsNullOrEmpty(g.TilePath) && g.TilePath.Equals(tilePath, StringComparison.OrdinalIgnoreCase));
            if (requested == null) return;
            lock (OpenPanelsLock)
            {
                WeakReference reference; FolderPanel existing = null;
                if (OpenPanels.TryGetValue(requested.Id, out reference)) existing = reference.Target as FolderPanel;
                if (existing != null && !existing.IsDisposed)
                {
                    existing.ReanchorForActivation(source); existing.Promote(); return;
                }
                FolderPanel panel = new FolderPanel(tilePath, source, currentSettings);
                OpenPanels[requested.Id] = new WeakReference(panel); panel.Show();
            }
        }

        internal static void SyncDesktopItems(DesktopItem[] items)
        {
            if (items == null) return; List<FolderPanel> panels = new List<FolderPanel>();
            lock (OpenPanelsLock)
            {
                foreach (WeakReference reference in OpenPanels.Values) { FolderPanel panel = reference.Target as FolderPanel; if (panel != null && !panel.IsDisposed) panels.Add(panel); }
            }
            foreach (FolderPanel panel in panels)
            {
                VirtualGroup g = panel.group;
                if (g == null) continue;
                string tilePath = g.TilePath;
                if (String.IsNullOrEmpty(tilePath)) continue;
                DesktopItem tile = items.FirstOrDefault(item => item.IsGroup && item.Path.Equals(tilePath, StringComparison.OrdinalIgnoreCase));
                if (tile != null) panel.QueueAnchorUpdate(tile.Bounds);
            }
        }

        internal FolderPanel(string tilePath, Rectangle source, AppSettings currentSettings)
        {
            settings = currentSettings; layout = DataStore.LoadVirtualLayout();
            group = layout.Groups.FirstOrDefault(g => g.TilePath.Equals(tilePath, StringComparison.OrdinalIgnoreCase));
            if (group == null) throw new InvalidDataException("Không tìm thấy dữ liệu virtual group.");
            Rectangle live;
            if (ExplorerDesktop.TryGetIconBounds(tilePath, out live)) source = live;
            groupId = group.Id; anchorBounds = source; animationOrigin = Rectangle.Inflate(source, -5, -5); Text = "Desktop Folders — " + group.Name;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true; BackColor = CollectionTheme.Border; Padding = new Padding(1); Opacity = 1.0; AllowDrop = true; KeyPreview = true;
#if TRACE_DRAG
            ShowInTaskbar = true;
#endif
            Rectangle work = Screen.FromRectangle(source).WorkingArea;
            compactSize = new Size(Math.Min(compactSize.Width, Math.Max(360, work.Width - 30)), Math.Min(compactSize.Height, Math.Max(420, work.Height - 30)));
            expandedSize = new Size(Math.Min(expandedSize.Width, Math.Max(compactSize.Width, work.Width - 30)), Math.Min(expandedSize.Height, Math.Max(compactSize.Height, work.Height - 30)));
            finalSize = compactSize;
            Point desired = CalculateAnchoredLocation(source, finalSize, work);
            finalLocation = FindOpenLocation(desired, finalSize, work);
            Size = finalSize; Location = finalLocation;
            DataStore.LogDrag("PANEL anchor=" + source + " bounds=" + Bounds);

            GradientPanel shell = new GradientPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 9, 10), CornerRadius = CollectionTheme.RadiusWindow };
            Controls.Add(shell);
            Panel header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.Transparent };
            shell.Controls.Add(header);
            HeaderIconButton close = HeaderButton(HeaderIconKind.Close, 32); close.Dock = DockStyle.Right; close.Click += delegate { CloseWithMorph(); };
            expandButton = HeaderButton(HeaderIconKind.Expand, 32); expandButton.Dock = DockStyle.Right; expandButton.Click += delegate { ToggleExpanded(); };
            listButton = HeaderButton(HeaderIconKind.List, 32); listButton.Dock = DockStyle.Right; listButton.Click += delegate { gridMode = false; UpdateModeButtons(); RenderApps(); };
            gridButton = HeaderButton(HeaderIconKind.Grid, 32); gridButton.Dock = DockStyle.Right; gridButton.Click += delegate { gridMode = true; UpdateModeButtons(); RenderApps(); };
            close.AccessibleName = "Đóng collection"; expandButton.AccessibleName = "Phóng to collection"; listButton.AccessibleName = "Bố cục danh sách"; gridButton.AccessibleName = "Bố cục lưới";
            // Controls dock from the last added element toward the outside edge.
            // Visual order, left-to-right: grid, list, expand, close.
            header.Controls.Add(gridButton); header.Controls.Add(listButton); header.Controls.Add(expandButton); header.Controls.Add(close);
            tooltips = new ToolTip(); tooltips.SetToolTip(gridButton, "Bố cục lưới"); tooltips.SetToolTip(listButton, "Bố cục danh sách"); tooltips.SetToolTip(expandButton, "Phóng to collection"); tooltips.SetToolTip(close, "Đóng collection");

            Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            shell.Controls.Add(content); content.BringToFront();
            titleLabel = new Label { Text = group.Name, ForeColor = CollectionTheme.Text, BackColor = Color.Transparent, Font = new Font("Segoe UI", 18, FontStyle.Bold), Location = new Point(0, 0), Width = 190, Height = 54, Cursor = Cursors.IBeam, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false };
            titleEditor = new TextBox { Text = group.Name, Visible = false, BorderStyle = BorderStyle.None, BackColor = CollectionTheme.Background, ForeColor = CollectionTheme.Text, Font = new Font("Segoe UI", 18, FontStyle.Bold), Location = new Point(0, 12), Width = 190 };
            titleLabel.Click += delegate { BeginTitleEdit(); };
            titleEditor.Leave += delegate { CommitTitleEdit(); };
            titleEditor.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { CommitTitleEdit(); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Escape) { EndTitleEdit(false); e.SuppressKeyPress = true; } };
            header.Controls.Add(titleLabel); header.Controls.Add(titleEditor); titleLabel.SendToBack(); titleEditor.SendToBack();
            header.Resize += delegate { int titleWidth = Math.Max(70, header.ClientSize.Width - 132); titleLabel.Width = titleWidth; titleEditor.Width = titleWidth; };

            RoundedPanel searchContainer = new RoundedPanel { Location = new Point(0, 0), Height = 40, Radius = CollectionTheme.RadiusControl, BackColor = CollectionTheme.Control, BorderColor = CollectionTheme.Stroke, BorderThickness = 1, ShowSearchGlyph = true, GlyphColor = CollectionTheme.MutedText, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            search = new TextBox { BorderStyle = BorderStyle.None, BackColor = CollectionTheme.Control, ForeColor = CollectionTheme.Text, Font = new Font("Segoe UI", 10.5f), Location = new Point(46, 10), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Height = 22, AccessibleName = "Tìm trong collection" };
            searchHint = new Label { Text = "Tìm trong collection…", ForeColor = CollectionTheme.MutedText, BackColor = CollectionTheme.Control, Font = new Font("Segoe UI", 10f), AutoSize = true, Location = new Point(48, 11), Cursor = Cursors.IBeam };
            searchHint.Click += delegate { search.Focus(); };
            search.Enter += delegate { searchContainer.BorderColor = CollectionTheme.Focus; searchContainer.BorderThickness = 2; searchContainer.GlyphColor = CollectionTheme.Focus; searchContainer.Invalidate(); };
            search.Leave += delegate { searchContainer.BorderColor = CollectionTheme.Stroke; searchContainer.BorderThickness = 1; searchContainer.GlyphColor = CollectionTheme.MutedText; searchContainer.Invalidate(); };
            searchContainer.Controls.Add(search); searchContainer.Controls.Add(searchHint); searchHint.BringToFront();
            content.Controls.Add(searchContainer);

            appsViewport = new Panel { Location = new Point(0, 54), BackColor = Color.Transparent, AllowDrop = true, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            apps = new FlowLayoutPanel { Location = Point.Empty, AutoScroll = false, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, BackColor = Color.Transparent, Padding = new Padding(5, 0, 0, 0), AllowDrop = true };
            themedScroll = new DarkScrollBar(appsViewport, apps) { Width = 6, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right };
            appsViewport.Controls.Add(apps); content.Controls.Add(appsViewport);
            content.Controls.Add(themedScroll); themedScroll.BringToFront();
            search.TextChanged += delegate { searchHint.Visible = search.TextLength == 0; RenderApps(); };
            Action layoutContent = delegate {
                int titleWidth = Math.Max(70, header.ClientSize.Width - 132); titleLabel.Width = titleWidth; titleEditor.Width = titleWidth; searchContainer.Width = Math.Max(100, content.ClientSize.Width - 7); search.Width = Math.Max(80, searchContainer.ClientSize.Width - 57);
                int contentHeight = Math.Max(100, content.ClientSize.Height - 54); int scrollX = Math.Max(100, content.ClientSize.Width - 8);
                appsViewport.Size = new Size(Math.Max(100, scrollX - 8), contentHeight); apps.Width = appsViewport.ClientSize.Width;
                themedScroll.Location = new Point(scrollX, 54); themedScroll.Height = contentHeight;
            };
            content.Resize += delegate { layoutContent(); };
            MouseEventHandler clearTextFocus = delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ClearTextFocus(); };
            MouseDown += clearTextFocus; shell.MouseDown += clearTextFocus; header.MouseDown += clearTextFocus; content.MouseDown += clearTextFocus; appsViewport.MouseDown += clearTextFocus; apps.MouseDown += clearTextFocus; themedScroll.MouseDown += clearTextFocus;
            DragEnter += OnDragEnter; DragDrop += OnDragDrop; appsViewport.DragEnter += OnDragEnter; appsViewport.DragDrop += OnDragDrop; apps.DragEnter += OnDragEnter; apps.DragDrop += OnDragDrop;
            LayoutChanged += OnSharedLayoutChanged; DesktopPointerDown += OnDesktopPointerDown;
            FormClosed += delegate {
                CancelActiveMorph(); CancelCompositionCue(); CancelGridDragPreview(false);
                LayoutChanged -= OnSharedLayoutChanged; DesktopPointerDown -= OnDesktopPointerDown;
                lock (OpenPanelsLock) { WeakReference reference; if (OpenPanels.TryGetValue(groupId, out reference) && Object.ReferenceEquals(reference.Target, this)) OpenPanels.Remove(groupId); }
                DataStore.LogDrag("PANEL closed=" + groupId);
            };
            Shown += delegate {
                DataStore.LogDrag("PANEL shown=" + group.Name); UpdateModeButtons(); layoutContent(); RenderApps(); Opacity = 1.0; Promote();
            };
            Deactivate += delegate { TopMost = false; DataStore.LogDrag("PANEL deactivate=" + groupId); };
            KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.F2) { BeginTitleEdit(); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.F) { search.Focus(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape && !titleEditor.Visible) { CloseWithMorph(); e.Handled = true; }
            };
        }

        HeaderIconButton HeaderButton(HeaderIconKind kind, int width)
        {
            return new HeaderIconButton(kind, width) { Margin = new Padding(4) };
        }
        void UpdateModeButtons() { gridButton.Selected = gridMode; listButton.Selected = !gridMode; }
        void Promote()
        {
            if (IsDisposed) return; CancelActiveMorph(); Opacity = 1.0; if (!Visible) Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; TopMost = true; BringToFront(); Activate();
            if (releaseTopMost == null)
            {
                releaseTopMost = new System.Windows.Forms.Timer { Interval = 260 };
                releaseTopMost.Tick += delegate { releaseTopMost.Stop(); TopMost = false; };
            }
            releaseTopMost.Stop(); releaseTopMost.Start();
        }
        void ToggleExpanded()
        {
            CancelActiveMorph();
            expanded = !expanded; Size target = expanded ? expandedSize : compactSize; Rectangle work = Screen.FromControl(this).WorkingArea;
            Point location = FindOpenLocation(CalculateAnchoredLocation(anchorBounds, target, work), target, work, this);
            Rectangle previous = Bounds; finalSize = target; finalLocation = location; Bounds = new Rectangle(location, target); Opacity = 1.0;
            expandButton.IconKind = expanded ? HeaderIconKind.Collapse : HeaderIconKind.Expand; expandButton.AccessibleName = expanded ? "Thu nhỏ collection" : "Phóng to collection"; tooltips.SetToolTip(expandButton, expanded ? "Thu nhỏ collection" : "Phóng to collection"); RenderApps();
            Promote(); if (!settings.ReduceMotion) StartCompositionCue(previous, Bounds);
        }

        Bitmap CaptureSnapshot()
        {
            Bitmap image = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            DrawToBitmap(image, ClientRectangle); return image;
        }
        void StartCompositionCue(Rectangle from, Rectangle to)
        {
            if (IsDisposed || settings.ReduceMotion || from == to) return;
            CancelCompositionCue(); CompositionMotionOverlay cue = null;
            try
            {
                cue = new CompositionMotionOverlay(from, to, delegate { if (Object.ReferenceEquals(activeCompositionCue, cue)) activeCompositionCue = null; });
                activeCompositionCue = cue; cue.Show();
            }
            catch { if (cue != null && !cue.IsDisposed) cue.Dispose(); activeCompositionCue = null; }
        }
        void StartMorph(Rectangle from, Rectangle to)
        {
            Bitmap image = null;
            try
            {
                CancelActiveMorph(); image = CaptureSnapshot(); WindowMorphOverlay overlay = null;
                overlay = new WindowMorphOverlay(image, from, to, 130, delegate { if (Object.ReferenceEquals(activeMorph, overlay)) activeMorph = null; });
                activeMorph = overlay; overlay.Show();
            }
            catch
            {
                if (image != null) image.Dispose(); activeMorph = null; Opacity = 1.0;
            }
        }
        void CancelActiveMorph()
        {
            WindowMorphOverlay overlay = activeMorph; activeMorph = null; if (overlay != null && !overlay.IsDisposed) overlay.CancelMorph();
        }
        void CancelCompositionCue()
        {
            CompositionMotionOverlay cue = activeCompositionCue; activeCompositionCue = null; if (cue != null && !cue.IsDisposed) cue.CancelCue();
        }
        void CloseWithMorph()
        {
            if (IsDisposed) return;
            CancelCompositionCue();
            if (settings.ReduceMotion) { Close(); return; }
            Bitmap image = null; Rectangle from = Bounds, to = animationOrigin;
            try
            {
                image = CaptureSnapshot(); CancelActiveMorph(); Close(); WindowMorphOverlay overlay = new WindowMorphOverlay(image, from, to, 90, null); overlay.Show();
            }
            catch { if (image != null) image.Dispose(); if (!IsDisposed) Close(); }
        }
        void ReanchorForActivation(Rectangle source)
        {
            if (IsDisposed || source.Width <= 0 || source.Height <= 0) return;
            CancelActiveMorph(); anchorBounds = source; animationOrigin = Rectangle.Inflate(source, -5, -5);
            Rectangle work = Screen.FromRectangle(source).WorkingArea;
            Point location = FindOpenLocation(CalculateAnchoredLocation(source, finalSize, work), finalSize, work, this);
            // The popup is always committed to its current tile anchor before the
            // first visible frame. Never animate a previously cached form position.
            Bounds = new Rectangle(location, finalSize); finalLocation = location;
        }
        static Point FindOpenLocation(Point desired, Size size, Rectangle work, FolderPanel ignored = null)
        {
            List<Rectangle> occupied = new List<Rectangle>();
            lock (OpenPanelsLock)
            {
                foreach (WeakReference reference in OpenPanels.Values)
                {
                    FolderPanel panel = reference.Target as FolderPanel; if (panel != null && !panel.IsDisposed && panel.Visible && !Object.ReferenceEquals(panel, ignored)) occupied.Add(panel.Bounds);
                }
            }
            List<Point> candidates = new List<Point> { desired, new Point(desired.X + 30, desired.Y + 30), new Point(desired.X - 30, desired.Y + 30), new Point(desired.X + 60, desired.Y + 54), new Point(desired.X - 60, desired.Y + 54), new Point(desired.X + 54, desired.Y - 42), new Point(desired.X - 54, desired.Y - 42) };
            Point best = desired; long bestScore = Int64.MaxValue;
            foreach (Point raw in candidates)
            {
                Point candidate = new Point(Math.Max(work.Left + 15, Math.Min(raw.X, work.Right - size.Width - 15)), Math.Max(work.Top + 15, Math.Min(raw.Y, work.Bottom - size.Height - 15)));
                Rectangle bounds = new Rectangle(candidate, size); long overlap = 0;
                foreach (Rectangle other in occupied) { Rectangle intersection = Rectangle.Intersect(bounds, other); overlap += (long)intersection.Width * intersection.Height; }
                long distance = Math.Abs(candidate.X - desired.X) + Math.Abs(candidate.Y - desired.Y); long score = overlap + distance * 300L;
                if (score < bestScore) { bestScore = score; best = candidate; }
            }
            return best;
        }
        void QueueAnchorUpdate(Rectangle updated)
        {
            if (IsDisposed || !IsHandleCreated || updated.Width <= 0 || updated.Height <= 0) return;
            BeginInvoke(new Action(delegate {
                if (IsDisposed) return; anchorBounds = updated; animationOrigin = Rectangle.Inflate(updated, -5, -5);
                Rectangle work = Screen.FromRectangle(updated).WorkingArea; Point location = FindOpenLocation(CalculateAnchoredLocation(updated, finalSize, work), finalSize, work, this);
                if (Math.Abs(location.X - Left) <= 2 && Math.Abs(location.Y - Top) <= 2) return;
                Bounds = new Rectangle(location, finalSize); finalLocation = location;
            }));
        }
        static Point CalculateAnchoredLocation(Rectangle anchor, Size size, Rectangle work)
        {
            const int edge = 12, gap = 10;
            int x = Math.Max(work.Left + edge, Math.Min(anchor.Left, work.Right - size.Width - edge));
            int below = anchor.Bottom + gap, above = anchor.Top - size.Height - gap;
            if (below + size.Height <= work.Bottom - edge) return new Point(x, below);
            if (above >= work.Top + edge) return new Point(x, above);
            int centeredY = Math.Max(work.Top + edge, Math.Min(anchor.Top + (anchor.Height - size.Height) / 2, work.Bottom - size.Height - edge));
            int right = anchor.Right + gap, left = anchor.Left - size.Width - gap;
            if (right + size.Width <= work.Right - edge) return new Point(right, centeredY);
            if (left >= work.Left + edge) return new Point(left, centeredY);
            return new Point(x, centeredY);
        }
        void BeginTitleEdit() { titleEditor.Text = group.Name; titleLabel.Visible = false; titleEditor.Visible = true; titleEditor.Focus(); titleEditor.SelectAll(); }
        void EndTitleEdit(bool keepText) { if (!keepText) titleEditor.Text = group.Name; titleEditor.Visible = false; titleLabel.Visible = true; }
        void CommitTitleEdit() { if (!titleEditor.Visible) return; RenameGroup(titleEditor.Text); EndTitleEdit(true); }
        void ClearTextFocus() { if (titleEditor.Visible) CommitTitleEdit(); ActiveControl = null; if (IsHandleCreated) Native.SetFocus(Handle); }

        void RenderApps()
        {
            if (apps == null || group == null) return;
            CancelReorderAnimation(); themedScroll.SetValue(0); apps.SuspendLayout(); apps.Controls.Clear(); apps.Top = 0; apps.Height = 10000; string query = search == null ? "" : search.Text.Trim().ToLowerInvariant();
            VirtualMember[] members = group.Members.Where(m => File.Exists(m.Path) && Path.GetFileNameWithoutExtension(m.Path).ToLowerInvariant().Contains(query)).OrderByDescending(m => group.Pinned.Contains(m.Path, StringComparer.OrdinalIgnoreCase)).ToArray();
            bool compactGrid = gridMode && apps.ClientSize.Width < 600;
            int usableWidth = Math.Max(260, apps.ClientSize.Width);
            int width = gridMode ? (compactGrid ? Math.Max(84, (usableWidth - 24) / 3) : Math.Max(140, (usableWidth - 24) / 3)) : Math.Max(250, usableWidth - 4);
            int rowWidth = gridMode ? (width + 8) * 3 : width;
            apps.Padding = new Padding(Math.Max(0, (usableWidth - rowWidth) / 2), 0, 0, 0);
            if (members.Length == 0) apps.Controls.Add(new Label { Text = "Không tìm thấy ứng dụng phù hợp.", ForeColor = CollectionTheme.MutedText, Font = new Font("Segoe UI", 11), AutoSize = true, Margin = new Padding(16) });
            bool? activePinnedSection = null; int pinnedCount = members.Count(m => group.Pinned.Contains(m.Path, StringComparer.OrdinalIgnoreCase)); int regularCount = members.Length - pinnedCount;
            foreach (VirtualMember member in members)
            {
                string path = member.Path; bool pinned = group.Pinned.Contains(path, StringComparer.OrdinalIgnoreCase);
                if (!activePinnedSection.HasValue || activePinnedSection.Value != pinned)
                {
                    if (apps.Controls.Count > 0) apps.SetFlowBreak(apps.Controls[apps.Controls.Count - 1], true);
                    SectionHeaderControl section = new SectionHeaderControl(pinned ? "Đã ghim" : "Tất cả ứng dụng", pinned ? pinnedCount : regularCount, Math.Max(100, usableWidth - apps.Padding.Left - 6));
                    apps.Controls.Add(section); apps.SetFlowBreak(section, true); activePinnedSection = pinned;
                }
                AppTile tile = new AppTile(path, pinned, gridMode, width, settings.ReduceMotion) { Margin = gridMode ? new Padding(4, 3, 4, 7) : new Padding(0, 0, 0, 5), AllowDrop = true };
                tooltips.SetToolTip(tile, Path.GetFileNameWithoutExtension(path));
                tile.MouseWheel += delegate(object sender, MouseEventArgs e) { themedScroll.ScrollBy(-(e.Delta / 120) * 70); HandledMouseEventArgs handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true; };
                Point childDragStart = Point.Empty; bool childDidDrag = false;
                tile.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { childDragStart = e.Location; childDidDrag = false; } };
                tile.MouseMove += delegate(object sender, MouseEventArgs e) {
                    if (e.Button != MouseButtons.Left || childDidDrag) return;
                    if (Math.Abs(e.X - childDragStart.X) < SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y - childDragStart.Y) < SystemInformation.DragSize.Height / 2) return;
                    childDidDrag = true; DataObject data = new DataObject(); data.SetData(SourceGroupFormat, groupId); data.SetData(MemberPathFormat, path);
                    BeginGridDrag(path); tile.SetDragVisual(true, false);
                    DragDropEffects effect = tile.DoDragDrop(data, DragDropEffects.Link | DragDropEffects.Move);
                    bool committed = gridDragCommitted; bool droppedOnDesktop = effect != DragDropEffects.Move && ExplorerDesktop.IsPointOnDesktopSurface(Cursor.Position);
                    if (!committed) CancelGridDragPreview(true); else EndGridDrag();
                    AppTile currentTile = apps.Controls.OfType<AppTile>().FirstOrDefault(item => item.ItemPath.Equals(path, StringComparison.OrdinalIgnoreCase));
                    if (currentTile != null && !currentTile.IsDisposed) currentTile.SetDragVisual(false, false);
                    if (droppedOnDesktop) BeginInvoke(new Action(delegate { AppTile visibleTile = apps.Controls.OfType<AppTile>().FirstOrDefault(item => item.ItemPath.Equals(path, StringComparison.OrdinalIgnoreCase)); AnimateMoveOut(visibleTile, path); }));
                };
                tile.Click += delegate { if (childDidDrag) { childDidDrag = false; return; } try { Process.Start(path); } catch (Exception e) { MessageBox.Show(e.Message, "Desktop Folders"); } };
                tile.DragEnter += delegate(object sender, DragEventArgs e) { HandleTileDragEnter(tile, path, pinned, e); };
                tile.DragOver += delegate(object sender, DragEventArgs e) { HandleTileDragEnter(tile, path, pinned, e); };
                tile.DragLeave += delegate { tile.SetDragVisual(false, false); };
                tile.DragDrop += delegate(object sender, DragEventArgs e) { HandleTileDrop(tile, path, pinned, e); };
                ContextMenuStrip shellTrigger = new ContextMenuStrip();
                shellTrigger.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e) { e.Cancel = true; BeginInvoke(new Action(delegate { if (IsDisposed) return; DataStore.LogDrag("SHELL_MENU request=" + path); ShellContextMenu.Show(this, path, pinned ? "Bỏ ghim ưu tiên" : "Ghim ưu tiên", delegate { TogglePin(path); }, delegate { AnimateMoveOut(tile, path); }); })); };
                tile.ContextMenuStrip = shellTrigger;
                apps.Controls.Add(tile);
            }
            apps.ResumeLayout(); apps.PerformLayout(); int contentBottom = 0;
            foreach (Control control in apps.Controls) contentBottom = Math.Max(contentBottom, control.Bottom + control.Margin.Bottom);
            apps.Height = Math.Max(appsViewport.ClientSize.Height, contentBottom + 4);
            themedScroll.SyncFromTarget();
        }

        void HandleTileDragEnter(AppTile targetTile, string targetPath, bool targetPinned, DragEventArgs e)
        {
            string sourceGroup = e.Data.GetData(SourceGroupFormat) as string; string sourcePath = e.Data.GetData(MemberPathFormat) as string;
            if (sourceGroup == groupId && !String.IsNullOrEmpty(sourcePath))
            {
                bool sourcePinned = group.Pinned.Contains(sourcePath, StringComparer.OrdinalIgnoreCase);
                e.Effect = sourcePinned == targetPinned ? DragDropEffects.Move : DragDropEffects.None;
                if (e.Effect == DragDropEffects.Move && !sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    Point local = targetTile.PointToClient(new Point(e.X, e.Y)); bool insertAfter = gridMode ? local.X >= targetTile.Width / 2 : local.Y >= targetTile.Height / 2;
                    PreviewGridReorder(sourcePath, targetPath, insertAfter);
                }
                targetTile.SetDragVisual(false, e.Effect == DragDropEffects.Move); return;
            }
            targetTile.SetDragVisual(false, false); OnDragEnter(targetTile, e);
        }
        void HandleTileDrop(AppTile targetTile, string targetPath, bool targetPinned, DragEventArgs e)
        {
            targetTile.SetDragVisual(false, false); string sourceGroup = e.Data.GetData(SourceGroupFormat) as string; string sourcePath = e.Data.GetData(MemberPathFormat) as string;
            if (sourceGroup == groupId && !String.IsNullOrEmpty(sourcePath))
            {
                bool sourcePinned = group.Pinned.Contains(sourcePath, StringComparer.OrdinalIgnoreCase);
                if (sourcePinned == targetPinned)
                {
                    if (!sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase)) { Point local = targetTile.PointToClient(new Point(e.X, e.Y)); PreviewGridReorder(sourcePath, targetPath, gridMode ? local.X >= targetTile.Width / 2 : local.Y >= targetTile.Height / 2); }
                    DataStore.LogDrag("REORDER commit=" + Path.GetFileName(sourcePath)); CommitGridReorder(); e.Effect = DragDropEffects.Move;
                }
                else e.Effect = DragDropEffects.None;
                return;
            }
            OnDragDrop(targetTile, e);
        }
        void BeginGridDrag(string sourcePath)
        {
            CancelReorderAnimation(); gridDragActive = true; gridDragCommitted = false; gridDragSourcePath = sourcePath; gridPreviewTargetPath = null; gridPreviewAfter = false;
        }
        void PreviewGridReorder(string sourcePath, string targetPath, bool insertAfter)
        {
            if (!gridDragActive || String.IsNullOrEmpty(sourcePath) || sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase)) return;
            if (String.Equals(gridPreviewTargetPath, targetPath, StringComparison.OrdinalIgnoreCase) && gridPreviewAfter == insertAfter) return;
            CancelReorderAnimation(); AppTile source = apps.Controls.OfType<AppTile>().FirstOrDefault(tile => tile.ItemPath.Equals(sourcePath, StringComparison.OrdinalIgnoreCase)); AppTile target = apps.Controls.OfType<AppTile>().FirstOrDefault(tile => tile.ItemPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
            if (source == null || target == null) return;
            bool sourcePinned = group.Pinned.Contains(sourcePath, StringComparer.OrdinalIgnoreCase), targetPinned = group.Pinned.Contains(targetPath, StringComparer.OrdinalIgnoreCase); if (sourcePinned != targetPinned) return;
            List<AppTile> sectionTiles = apps.Controls.OfType<AppTile>().Where(tile => group.Pinned.Contains(tile.ItemPath, StringComparer.OrdinalIgnoreCase) == sourcePinned).ToList();
            Dictionary<string, Point> previous = sectionTiles.ToDictionary(tile => tile.ItemPath, tile => tile.Location, StringComparer.OrdinalIgnoreCase);
            sectionTiles.Remove(source); int targetIndex = sectionTiles.FindIndex(tile => tile.ItemPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase)); if (targetIndex < 0) return; if (insertAfter) targetIndex++; sectionTiles.Insert(Math.Max(0, Math.Min(sectionTiles.Count, targetIndex)), source);
            int firstControlIndex = sectionTiles.Min(tile => apps.Controls.GetChildIndex(tile)); apps.SuspendLayout();
            for (int index = 0; index < sectionTiles.Count; index++) apps.Controls.SetChildIndex(sectionTiles[index], firstControlIndex + index);
            apps.ResumeLayout(true); apps.PerformLayout(); gridPreviewTargetPath = targetPath; gridPreviewAfter = insertAfter; StartReorderAnimation(previous);
        }
        void CommitGridReorder()
        {
            if (!gridDragActive) return; CancelReorderAnimation(); layout = DataStore.LoadVirtualLayout(); group = layout.Groups.FirstOrDefault(g => g.Id == groupId); if (group == null) { EndGridDrag(); return; }
            List<string> visual = apps.Controls.OfType<AppTile>().Select(tile => tile.ItemPath).ToList(); ApplyVisualSubset(group.Members, visual.Where(path => group.Pinned.Contains(path, StringComparer.OrdinalIgnoreCase)).ToList()); ApplyVisualSubset(group.Members, visual.Where(path => !group.Pinned.Contains(path, StringComparer.OrdinalIgnoreCase)).ToList());
            DataStore.SaveVirtualLayout(layout); try { GroupTileFactory.CreateOrUpdate(group); } catch { } gridDragCommitted = true; gridDragActive = false; gridPreviewTargetPath = null;
        }
        static void ApplyVisualSubset(List<VirtualMember> members, List<string> visualOrder)
        {
            if (visualOrder == null || visualOrder.Count < 2) return; Dictionary<string, VirtualMember> byPath = members.ToDictionary(member => member.Path, StringComparer.OrdinalIgnoreCase); HashSet<string> visible = new HashSet<string>(visualOrder, StringComparer.OrdinalIgnoreCase); int next = 0;
            for (int index = 0; index < members.Count && next < visualOrder.Count; index++) if (visible.Contains(members[index].Path)) members[index] = byPath[visualOrder[next++]];
        }
        void CancelGridDragPreview(bool restoreLayout)
        {
            CancelReorderAnimation(); bool wasActive = gridDragActive; EndGridDrag();
            if (restoreLayout && wasActive && !IsDisposed) { layout = DataStore.LoadVirtualLayout(); group = layout.Groups.FirstOrDefault(g => g.Id == groupId); if (group != null) RenderApps(); }
        }
        void EndGridDrag()
        {
            gridDragActive = false; gridDragCommitted = false; gridDragSourcePath = null; gridPreviewTargetPath = null; gridPreviewAfter = false;
        }
        void StartReorderAnimation(Dictionary<string, Point> previous)
        {
            if (settings.ReduceMotion || previous == null || previous.Count == 0) return;
            List<TileMotion> motions = new List<TileMotion>();
            foreach (AppTile tile in apps.Controls.OfType<AppTile>())
            {
                Point from; if (!previous.TryGetValue(tile.ItemPath, out from) || from == tile.Location) continue;
                motions.Add(new TileMotion { Tile = tile, From = from, To = tile.Location });
            }
            if (motions.Count == 0) return;
            apps.SuspendLayout(); reorderLayoutSuspended = true; reorderMotions = motions;
            foreach (TileMotion motion in motions) motion.Tile.Location = motion.From;
            if (reorderAnimation == null) { reorderAnimation = new System.Windows.Forms.Timer { Interval = 15 }; reorderAnimation.Tick += delegate { AnimateReorderFrame(); }; }
            reorderAnimationStarted = Environment.TickCount; reorderAnimation.Start();
        }
        void AnimateReorderFrame()
        {
            if (reorderMotions == null || reorderMotions.Count == 0) { CancelReorderAnimation(); return; }
            double progress = Math.Min(1.0, unchecked(Environment.TickCount - reorderAnimationStarted) / 155.0); double eased = 1.0 - Math.Pow(1.0 - progress, 3.0);
            foreach (TileMotion motion in reorderMotions) if (!motion.Tile.IsDisposed) motion.Tile.Location = new Point(motion.From.X + (int)Math.Round((motion.To.X - motion.From.X) * eased), motion.From.Y + (int)Math.Round((motion.To.Y - motion.From.Y) * eased));
            if (progress >= 1.0) CancelReorderAnimation();
        }
        void CancelReorderAnimation()
        {
            if (reorderAnimation != null) reorderAnimation.Stop();
            if (reorderMotions != null) foreach (TileMotion motion in reorderMotions) if (!motion.Tile.IsDisposed) motion.Tile.Location = motion.To;
            reorderMotions = null;
            if (reorderLayoutSuspended && apps != null && !apps.IsDisposed) { reorderLayoutSuspended = false; apps.ResumeLayout(false); apps.PerformLayout(); }
        }
        void AnimateMoveOut(AppTile tile, string path)
        {
            if (settings.ReduceMotion || tile == null || tile.IsDisposed) { MoveOut(path); return; }
            Bitmap image = null;
            try
            {
                image = new Bitmap(Math.Max(1, tile.Width), Math.Max(1, tile.Height)); tile.DrawToBitmap(image, tile.ClientRectangle);
                Rectangle from = tile.RectangleToScreen(tile.ClientRectangle); Point pointer = Cursor.Position; Rectangle to = new Rectangle(pointer.X - 28, pointer.Y - 28, 56, 56);
                WindowMorphOverlay overlay = new WindowMorphOverlay(image, from, to, 125, delegate { MoveOut(path); }); image = null; overlay.Show();
            }
            catch { if (image != null) image.Dispose(); MoveOut(path); }
        }

        void ReloadGroup()
        {
            layout = DataStore.LoadVirtualLayout(); group = layout.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) { Close(); return; }
            Text = "Desktop Folders — " + group.Name; titleLabel.Text = group.Name; if (!titleEditor.Visible) titleEditor.Text = group.Name; RenderApps();
        }
        void OnSharedLayoutChanged() { if (IsDisposed || !IsHandleCreated) return; BeginInvoke(new Action(ReloadGroup)); }
        void OnDesktopPointerDown(Point point)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(delegate { if (!IsDisposed && ExplorerDesktop.IsPointOnDesktopSurface(point) && !ExplorerDesktop.IsAnyIconAtPoint(point)) CloseWithMorph(); }));
        }
        internal static void NotifyLayoutChanged() { Action changed = LayoutChanged; if (changed != null) changed(); }
        internal static void NotifyDesktopClick(Point point) { Action<Point> clicked = DesktopPointerDown; if (clicked != null) clicked(point); }

        void TogglePin(string path)
        {
            layout = DataStore.LoadVirtualLayout(); group = layout.Groups.FirstOrDefault(g => g.Id == groupId); if (group == null) return;
            string existing = group.Pinned.FirstOrDefault(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)); if (existing == null) group.Pinned.Add(path); else group.Pinned.Remove(existing);
            DataStore.SaveVirtualLayout(layout); try { GroupTileFactory.CreateOrUpdate(group); } catch { } NotifyLayoutChanged();
        }
        void MoveOut(string path)
        {
            layout = DataStore.LoadVirtualLayout(); group = layout.Groups.FirstOrDefault(g => g.Id == groupId); if (group == null) return;
            VirtualMember member = group.Members.FirstOrDefault(m => m.Path.Equals(path, StringComparison.OrdinalIgnoreCase)); if (member == null) return;
            List<string> oldPinned = new List<string>(group.Pinned);
            try { File.SetAttributes(member.Path, (FileAttributes)member.OriginalAttributes); group.Members.Remove(member); group.Pinned.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)); DataStore.SaveVirtualLayout(layout); ExplorerDesktop.NotifyPathChanged(member.Path); }
            catch (Exception e) { try { File.SetAttributes(member.Path, File.GetAttributes(member.Path) | FileAttributes.Hidden); } catch { } if (!group.Members.Contains(member)) group.Members.Add(member); group.Pinned = oldPinned; MessageBox.Show(e.Message, "Desktop Folders"); return; }
            if (settings.DissolveSingleAppGroup && group.Members.Count <= 1) DissolveGroup(); else { try { GroupTileFactory.CreateOrUpdate(group); } catch { } NotifyLayoutChanged(); }
        }
        void DissolveGroup()
        {
            foreach (VirtualMember member in group.Members) try { if (File.Exists(member.Path)) { File.SetAttributes(member.Path, (FileAttributes)member.OriginalAttributes); ExplorerDesktop.NotifyPathChanged(member.Path); } } catch { }
            try { if (File.Exists(group.TilePath)) File.Delete(group.TilePath); } catch { } try { if (!String.IsNullOrEmpty(group.IconPath) && File.Exists(group.IconPath)) File.Delete(group.IconPath); } catch { }
            layout.Groups.Remove(group); DataStore.SaveVirtualLayout(layout); NotifyLayoutChanged(); Close();
        }
        void RenameGroup(string value)
        {
            string requested = value.Trim(); if (requested.Length == 0 || requested == group.Name) { titleLabel.Text = group.Name; return; }
            foreach (char invalid in Path.GetInvalidFileNameChars()) requested = requested.Replace(invalid.ToString(), ""); if (requested.Length == 0) return;
            layout = DataStore.LoadVirtualLayout(); group = layout.Groups.FirstOrDefault(g => g.Id == groupId); if (group == null) return;
            string newPath = Path.Combine(ExplorerDesktop.Desktop, requested + ".lnk"); if (File.Exists(newPath)) { titleLabel.Text = group.Name; titleEditor.Text = group.Name; return; }
            string oldPath = group.TilePath, oldName = group.Name;
            try { File.Move(oldPath, newPath); group.TilePath = newPath; group.Name = requested; GroupTileFactory.CreateOrUpdate(group); DataStore.SaveVirtualLayout(layout); titleLabel.Text = requested; NotifyLayoutChanged(); }
            catch { try { if (File.Exists(newPath) && !File.Exists(oldPath)) File.Move(newPath, oldPath); } catch { } group.TilePath = oldPath; group.Name = oldName; titleLabel.Text = oldName; titleEditor.Text = oldName; }
        }
        void OnDragEnter(object sender, DragEventArgs e)
        {
            string sourceGroup = e.Data.GetData(SourceGroupFormat) as string;
            string memberPath = e.Data.GetData(MemberPathFormat) as string;
            e.Effect = !String.IsNullOrEmpty(sourceGroup) && !String.IsNullOrEmpty(memberPath) ? DragDropEffects.Move : (e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None);
        }
        void OnDragDrop(object sender, DragEventArgs e)
        {
            string sourceGroup = e.Data.GetData(SourceGroupFormat) as string;
            string internalPath = e.Data.GetData(MemberPathFormat) as string;
            string[] paths = !String.IsNullOrEmpty(internalPath) ? new string[] { internalPath } : e.Data.GetData(DataFormats.FileDrop) as string[]; if (paths == null || paths.Length == 0) return;
            try
            {
                if (sourceGroup == groupId && !String.IsNullOrEmpty(internalPath)) { CommitGridReorder(); e.Effect = DragDropEffects.Move; return; }
                if (!String.IsNullOrEmpty(sourceGroup) && sourceGroup != groupId && TransferMember(sourceGroup, paths[0])) { e.Effect = DragDropEffects.Move; return; }
                foreach (string path in paths) AddMember(path); e.Effect = DragDropEffects.Link; NotifyLayoutChanged();
            }
            catch (Exception error) { e.Effect = DragDropEffects.None; MessageBox.Show("Không thể thêm vào collection: " + error.Message, "Desktop Folders"); }
        }
        bool TransferMember(string sourceGroupId, string path)
        {
            VirtualLayout latest = DataStore.LoadVirtualLayout(); VirtualGroup from = latest.Groups.FirstOrDefault(g => g.Id == sourceGroupId); VirtualGroup to = latest.Groups.FirstOrDefault(g => g.Id == groupId);
            if (from == null || to == null || to.Members.Any(m => m.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return false;
            VirtualMember member = from.Members.FirstOrDefault(m => m.Path.Equals(path, StringComparison.OrdinalIgnoreCase)); if (member == null) return false;
            from.Members.Remove(member); from.Pinned.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)); to.Members.Add(member);
            bool dissolveSource = settings.DissolveSingleAppGroup && from.Members.Count <= 1;
            if (dissolveSource) latest.Groups.Remove(from);
            DataStore.SaveVirtualLayout(latest); try { GroupTileFactory.CreateOrUpdate(to); } catch { }
            if (dissolveSource)
            {
                foreach (VirtualMember remaining in from.Members) try { if (File.Exists(remaining.Path)) { File.SetAttributes(remaining.Path, (FileAttributes)remaining.OriginalAttributes); ExplorerDesktop.NotifyPathChanged(remaining.Path); } } catch { }
                try { if (File.Exists(from.TilePath)) File.Delete(from.TilePath); } catch { } try { if (!String.IsNullOrEmpty(from.IconPath) && File.Exists(from.IconPath)) File.Delete(from.IconPath); } catch { }
            }
            else try { GroupTileFactory.CreateOrUpdate(from); } catch { }
            NotifyLayoutChanged(); return true;
        }
        void AddMember(string path)
        {
            if (!File.Exists(path)) return; layout = DataStore.LoadVirtualLayout(); group = layout.Groups.FirstOrDefault(g => g.Id == groupId); if (group == null || group.Members.Any(m => m.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
            FileAttributes attributes = File.GetAttributes(path); VirtualMember added = new VirtualMember { Path = path, OriginalAttributes = (int)attributes };
            try { File.SetAttributes(path, attributes | FileAttributes.Hidden); group.Members.Add(added); DataStore.SaveVirtualLayout(layout); ExplorerDesktop.NotifyPathChanged(path); }
            catch { group.Members.Remove(added); try { File.SetAttributes(path, attributes); } catch { } throw; }
            try { GroupTileFactory.CreateOrUpdate(group); } catch { }
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e); if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) return;
            Rectangle rounded = new Rectangle(0, 0, Math.Max(1, ClientSize.Width - 1), Math.Max(1, ClientSize.Height - 1));
            using (System.Drawing.Drawing2D.GraphicsPath path = DrawExtensions.RoundPath(rounded, CollectionTheme.RadiusWindow)) { Region old = Region; Region = new Region(path); if (old != null) old.Dispose(); }
        }
        protected override void Dispose(bool disposing) { if (disposing) { if (releaseTopMost != null) releaseTopMost.Dispose(); if (reorderAnimation != null) reorderAnimation.Dispose(); if (tooltips != null) tooltips.Dispose(); } base.Dispose(disposing); }
    }

    internal sealed class GradientPanel : Panel
    {
        int cornerRadius;
        internal int CornerRadius { get { return cornerRadius; } set { cornerRadius = value; UpdateRoundedRegion(); } }
        internal GradientPanel() { DoubleBuffered = true; BackColor = Color.Transparent; }
        protected override void OnResize(EventArgs eventArgs) { base.OnResize(eventArgs); UpdateRoundedRegion(); }
        void UpdateRoundedRegion()
        {
            if (cornerRadius <= 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            Rectangle rounded = new Rectangle(0, 0, Math.Max(1, ClientSize.Width - 1), Math.Max(1, ClientSize.Height - 1));
            using (System.Drawing.Drawing2D.GraphicsPath path = DrawExtensions.RoundPath(rounded, cornerRadius)) { Region old = Region; Region = new Region(path); if (old != null) old.Dispose(); }
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle area = ClientRectangle; if (area.Width <= 0 || area.Height <= 0) return;
            using (Brush background = new SolidBrush(CollectionTheme.Background)) e.Graphics.FillRectangle(background, area);
            Rectangle glow = new Rectangle(-area.Width / 4, -area.Height / 2, area.Width + area.Width / 2, Math.Max(180, area.Height));
            using (System.Drawing.Drawing2D.LinearGradientBrush ambient = new System.Drawing.Drawing2D.LinearGradientBrush(glow, Color.FromArgb(29, CollectionTheme.Focus), Color.FromArgb(0, CollectionTheme.Focus), 90f)) e.Graphics.FillEllipse(ambient, glow);
        }
    }

    internal sealed class DarkScrollBar : Control
    {
        readonly Panel viewport;
        readonly Control content;
        bool hot;
        bool dragging;
        int dragOffset;
        int value;
        int maximum;

        internal DarkScrollBar(Panel scrollViewport, Control scrollContent)
        {
            viewport = scrollViewport; content = scrollContent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand; Visible = false;
            viewport.MouseWheel += delegate(object sender, MouseEventArgs e) { ScrollBy(-(e.Delta / 120) * 70); };
            content.MouseWheel += delegate(object sender, MouseEventArgs e) { ScrollBy(-(e.Delta / 120) * 70); };
            viewport.Resize += delegate { SyncFromTarget(); };
            content.Resize += delegate { SyncFromTarget(); };
        }
        internal void SyncFromTarget()
        {
            if (IsDisposed || viewport.IsDisposed || content.IsDisposed) return;
            maximum = Math.Max(0, content.Height - viewport.ClientSize.Height); value = Math.Max(0, Math.Min(maximum, value)); content.Top = -value;
            Visible = maximum > 0 && Height > 20; Invalidate();
        }
        internal void SetValue(int requested) { value = Math.Max(0, Math.Min(maximum, requested)); content.Top = -value; Invalidate(); }
        internal void ScrollBy(int delta) { SyncFromTarget(); SetValue(value + delta); }
        Rectangle ThumbRectangle()
        {
            int total = Math.Max(1, content.Height), visible = Math.Max(1, viewport.ClientSize.Height);
            int thumbHeight = Math.Max(28, Math.Min(Height, Height * visible / total));
            int range = Math.Max(1, maximum); int y = (Height - thumbHeight) * Math.Min(range, value) / range;
            return new Rectangle(2, y, Math.Max(4, Width - 4), thumbHeight);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color thumbColor = hot || dragging ? Color.FromArgb(180, CollectionTheme.Focus) : Color.FromArgb(68, 255, 255, 255);
            using (Brush thumb = new SolidBrush(thumbColor)) e.Graphics.FillRoundedRectangle(thumb, ThumbRectangle(), Math.Max(2, Width / 2 - 1));
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; if (!dragging) Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return; Rectangle thumb = ThumbRectangle(); dragging = true; dragOffset = thumb.Contains(e.Location) ? e.Y - thumb.Top : thumb.Height / 2; Capture = true; SetFromPointer(e.Y); base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromPointer(e.Y); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(-(e.Delta / 120) * 70); base.OnMouseWheel(e);
        }
        void SetFromPointer(int pointerY)
        {
            Rectangle thumb = ThumbRectangle(); int travel = Math.Max(1, Height - thumb.Height); int top = Math.Max(0, Math.Min(travel, pointerY - dragOffset));
            SetValue(maximum == 0 ? 0 : top * maximum / travel);
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        internal int Radius { get; set; }
        internal Color BorderColor { get; set; }
        internal int BorderThickness { get; set; }
        internal bool ShowSearchGlyph { get; set; }
        internal Color GlyphColor { get; set; }
        internal RoundedPanel() { Radius = 16; BorderColor = Color.Transparent; BorderThickness = 0; GlyphColor = CollectionTheme.MutedText; DoubleBuffered = true; Resize += delegate { UpdateRoundedRegion(); }; }
        void UpdateRoundedRegion()
        {
            if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) return;
            Rectangle rounded = new Rectangle(0, 0, Math.Max(1, ClientSize.Width - 1), Math.Max(1, ClientSize.Height - 1));
            using (System.Drawing.Drawing2D.GraphicsPath path = DrawExtensions.RoundPath(rounded, Radius))
            {
                Region old = Region; Region = new Region(path); if (old != null) old.Dispose();
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (ClientSize.Width <= 1 || ClientSize.Height <= 1) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (BorderThickness > 0 && BorderColor.A > 0)
            {
                int inset = Math.Max(1, BorderThickness);
                Rectangle border = new Rectangle(inset, inset, Math.Max(1, ClientSize.Width - inset * 2 - 1), Math.Max(1, ClientSize.Height - inset * 2 - 1));
                using (Pen pen = new Pen(BorderColor, BorderThickness)) e.Graphics.DrawRoundedRectangle(pen, border, Math.Max(3, Radius - inset));
            }
            if (ShowSearchGlyph)
            {
                float cx = 21f, cy = ClientSize.Height / 2f - 1f;
                using (Pen pen = new Pen(GlyphColor, 1.7f))
                {
                    pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    e.Graphics.DrawEllipse(pen, cx - 6, cy - 6, 12, 12); e.Graphics.DrawLine(pen, cx + 4, cy + 4, cx + 9, cy + 9);
                }
            }
        }
    }

    internal sealed class ModernToggleSwitch : Control
    {
        bool isChecked;
        bool hot;

        internal event EventHandler CheckedChanged;

        internal bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked != value)
                {
                    isChecked = value;
                    Invalidate();
                    var handler = CheckedChanged;
                    if (handler != null) handler(this, EventArgs.Empty);
                }
            }
        }

        internal ModernToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(46, 24);
            Cursor = Cursors.Hand;
            BackColor = Color.FromArgb(24, 32, 47);
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                Checked = !Checked;
            }
            base.OnMouseDown(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                Checked = !Checked;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color bg = (Parent != null && Parent.BackColor.A > 0) ? Parent.BackColor : BackColor;
            using (Brush bgBrush = new SolidBrush(bg)) e.Graphics.FillRectangle(bgBrush, ClientRectangle);

            Rectangle track = new Rectangle(1, 2, Width - 3, Height - 5);
            Color trackColor = isChecked
                ? (hot ? Color.FromArgb(59, 130, 246) : Color.FromArgb(37, 99, 235))
                : (hot ? Color.FromArgb(71, 85, 105) : Color.FromArgb(51, 65, 85));

            using (Brush fill = new SolidBrush(trackColor))
                e.Graphics.FillRoundedRectangle(fill, track, track.Height / 2);

            if (Focused)
            {
                using (Pen focusPen = new Pen(CollectionTheme.Focus, 1.5f))
                    e.Graphics.DrawRoundedRectangle(focusPen, track, track.Height / 2);
            }

            int thumbSize = track.Height - 4;
            int thumbX = isChecked ? track.Right - thumbSize - 2 : track.Left + 2;
            int thumbY = track.Top + 2;
            Rectangle thumb = new Rectangle(thumbX, thumbY, thumbSize, thumbSize);

            using (Brush thumbFill = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(thumbFill, thumb);
        }
    }

    internal sealed class ModernSlider : Control
    {
        int min = 120;
        int max = 800;
        int step = 20;
        int val = 280;
        bool dragging;
        bool hot;

        internal event EventHandler ValueChanged;

        internal int Minimum { get { return min; } set { min = value; Invalidate(); } }
        internal int Maximum { get { return max; } set { max = value; Invalidate(); } }
        internal int Step { get { return step; } set { step = Math.Max(1, value); } }

        internal int Value
        {
            get { return val; }
            set
            {
                int clamped = Math.Max(min, Math.Min(max, value));
                clamped = min + (int)Math.Round((clamped - min) / (double)step) * step;
                if (val != clamped)
                {
                    val = clamped;
                    Invalidate();
                    var handler = ValueChanged;
                    if (handler != null) handler(this, EventArgs.Empty);
                }
            }
        }

        internal ModernSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(200, 26);
            Cursor = Cursors.Hand;
            BackColor = Color.FromArgb(24, 32, 47);
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; if (!dragging) Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                dragging = true;
                Capture = true;
                UpdateFromPointer(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) UpdateFromPointer(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            Capture = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) { Value -= step; e.Handled = true; }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) { Value += step; e.Handled = true; }
            base.OnKeyDown(e);
        }

        void UpdateFromPointer(int pointerX)
        {
            int pad = 10;
            int travel = Math.Max(1, Width - pad * 2);
            double fraction = Math.Max(0.0, Math.Min(1.0, (pointerX - pad) / (double)travel));
            Value = min + (int)Math.Round(fraction * (max - min));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color bg = (Parent != null && Parent.BackColor.A > 0) ? Parent.BackColor : BackColor;
            using (Brush bgBrush = new SolidBrush(bg)) e.Graphics.FillRectangle(bgBrush, ClientRectangle);

            int pad = 10;
            int trackY = Height / 2 - 3;
            int trackHeight = 6;
            int trackWidth = Math.Max(1, Width - pad * 2);

            Rectangle trackBg = new Rectangle(pad, trackY, trackWidth, trackHeight);
            using (Brush bgFill = new SolidBrush(Color.FromArgb(51, 65, 85)))
                e.Graphics.FillRoundedRectangle(bgFill, trackBg, 3);

            double frac = (val - min) / (double)(max - min);
            int thumbX = pad + (int)Math.Round(frac * trackWidth);

            if (thumbX > pad)
            {
                Rectangle activeTrack = new Rectangle(pad, trackY, thumbX - pad, trackHeight);
                using (Brush activeFill = new SolidBrush(Color.FromArgb(59, 130, 246)))
                    e.Graphics.FillRoundedRectangle(activeFill, activeTrack, 3);
            }

            int thumbSize = 16;
            Rectangle thumb = new Rectangle(thumbX - thumbSize / 2, Height / 2 - thumbSize / 2, thumbSize, thumbSize);

            if (hot || dragging || Focused)
            {
                Rectangle glow = Rectangle.Inflate(thumb, 3, 3);
                using (Brush glowBrush = new SolidBrush(Color.FromArgb(50, 59, 130, 246)))
                    e.Graphics.FillEllipse(glowBrush, glow);
            }

            using (Brush thumbBrush = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(thumbBrush, thumb);

            using (Pen thumbPen = new Pen(Color.FromArgb(59, 130, 246), 2f))
                e.Graphics.DrawEllipse(thumbPen, thumb);
        }
    }

    internal sealed class ModernCard : Panel
    {
        internal int Radius { get; set; }
        internal Color CardColor { get; set; }
        internal Color BorderColor { get; set; }

        internal ModernCard()
        {
            Radius = 14;
            CardColor = Color.FromArgb(24, 32, 47);
            BorderColor = Color.FromArgb(51, 65, 85);
            DoubleBuffered = true;
            BackColor = CardColor;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color parentBg = (Parent != null && Parent.BackColor.A > 0) ? Parent.BackColor : CollectionTheme.Background;
            using (Brush bgBrush = new SolidBrush(parentBg)) e.Graphics.FillRectangle(bgBrush, ClientRectangle);

            Rectangle rect = new Rectangle(1, 1, Width - 3, Height - 3);
            using (System.Drawing.Drawing2D.GraphicsPath path = DrawExtensions.RoundPath(rect, Radius))
            {
                using (Brush brush = new SolidBrush(CardColor))
                    e.Graphics.FillPath(brush, path);
                using (Pen pen = new Pen(BorderColor, 1f))
                    e.Graphics.DrawPath(pen, path);
            }
            base.OnPaint(e);
        }
    }

    internal sealed class ModernButton : Button
    {
        bool isPrimary;
        bool hot;

        internal bool IsPrimary
        {
            get { return isPrimary; }
            set { isPrimary = value; Invalidate(); }
        }

        internal ModernButton(string text, bool primary = false)
        {
            Text = text;
            isPrimary = primary;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            BackColor = primary ? Color.FromArgb(59, 130, 246) : Color.FromArgb(30, 41, 59);
            ForeColor = Color.White;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color parentBg = (Parent != null && Parent.BackColor.A > 0) ? Parent.BackColor : CollectionTheme.Background;
            using (Brush bgBrush = new SolidBrush(parentBg)) e.Graphics.FillRectangle(bgBrush, ClientRectangle);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = isPrimary
                ? (hot ? Color.FromArgb(96, 165, 250) : Color.FromArgb(59, 130, 246))
                : (hot ? Color.FromArgb(51, 65, 85) : Color.FromArgb(30, 41, 59));
            Color border = isPrimary ? Color.FromArgb(59, 130, 246) : Color.FromArgb(71, 85, 105);

            using (System.Drawing.Drawing2D.GraphicsPath path = DrawExtensions.RoundPath(rect, 8))
            {
                using (Brush brush = new SolidBrush(bg))
                    e.Graphics.FillPath(brush, path);
                using (Pen pen = new Pen(border, 1f))
                    e.Graphics.DrawPath(pen, path);
            }

            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (Brush textBrush = new SolidBrush(ForeColor))
            {
                e.Graphics.DrawString(Text, Font, textBrush, rect, sf);
            }

            if (Focused)
            {
                Rectangle fRect = Rectangle.Inflate(rect, -2, -2);
                using (System.Drawing.Drawing2D.GraphicsPath fPath = DrawExtensions.RoundPath(fRect, 6))
                using (Pen fPen = new Pen(CollectionTheme.Focus, 1.5f))
                    e.Graphics.DrawPath(fPen, fPath);
            }
        }
    }

    internal sealed class SettingsForm : Form
    {
        readonly DirectDesktopController controller;
        ModernToggleSwitch startupSwitch;
        ModernToggleSwitch reduceSwitch;
        ModernToggleSwitch dissolveSwitch;
        ModernSlider delaySlider;
        Label delayBadge;
        Label targetStatus;

        internal SettingsForm(DirectDesktopController owner)
        {
            controller = owner;
            Text = "Desktop Folders Settings";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(520, 620);
            FormBorderStyle = FormBorderStyle.None;
            BackColor = CollectionTheme.Background;
            ForeColor = CollectionTheme.Text;
            ShowInTaskbar = true;
            DoubleBuffered = true;
            KeyPreview = true;

            // Header draggable bar
            Panel header = new Panel { Location = new Point(0, 0), Size = new Size(520, 52), BackColor = Color.Transparent };
            Icon appIcon = null;
            try { appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            if (appIcon == null) appIcon = SystemIcons.Application;
            PictureBox iconBox = new PictureBox { Image = appIcon.ToBitmap(), SizeMode = PictureBoxSizeMode.StretchImage, Location = new Point(20, 14), Size = new Size(24, 24), BackColor = Color.Transparent };
            Label titleLabel = new Label { Text = "Cài đặt Desktop Folders", Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), ForeColor = CollectionTheme.Text, Location = new Point(52, 16), AutoSize = true, BackColor = Color.Transparent };
            Label badgeLabel = new Label { Text = "v6.0", Font = new Font("Segoe UI", 8f, FontStyle.Bold), ForeColor = CollectionTheme.MutedText, BackColor = Color.FromArgb(30, 41, 59), Location = new Point(236, 18), AutoSize = true, Padding = new Padding(4, 1, 4, 1) };
            HeaderIconButton closeBtn = new HeaderIconButton(HeaderIconKind.Close, 34) { Location = new Point(520 - 46, 9), Size = new Size(34, 34), BackColor = Color.Transparent };
            closeBtn.Click += delegate { Close(); };

            Point dragStart = Point.Empty;
            bool isDragging = false;
            MouseEventHandler onHeaderDown = delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isDragging = true; dragStart = e.Location; } };
            MouseEventHandler onHeaderMove = delegate(object s, MouseEventArgs e) { if (isDragging && e.Button == MouseButtons.Left) { Left += e.X - dragStart.X; Top += e.Y - dragStart.Y; } };
            MouseEventHandler onHeaderUp = delegate { isDragging = false; };
            header.MouseDown += onHeaderDown; header.MouseMove += onHeaderMove; header.MouseUp += onHeaderUp;
            titleLabel.MouseDown += onHeaderDown; titleLabel.MouseMove += onHeaderMove; titleLabel.MouseUp += onHeaderUp;
            iconBox.MouseDown += onHeaderDown; iconBox.MouseMove += onHeaderMove; iconBox.MouseUp += onHeaderUp;
            header.Controls.AddRange(new Control[] { iconBox, titleLabel, badgeLabel, closeBtn });
            Controls.Add(header);

            int hoverDelay = (controller != null && controller.Settings != null) ? controller.Settings.FolderHoverDelay : 280;
            bool dissolve = (controller != null && controller.Settings != null) && controller.Settings.DissolveSingleAppGroup;
            bool startWin = (controller != null && controller.Settings != null) && controller.Settings.StartWithWindows;
            bool reduceMotion = (controller != null && controller.Settings != null) && controller.Settings.ReduceMotion;

            // Card 1: Thao tác & Kéo thả
            Label sec1 = CreateSectionHeader("THAO TÁC & KÉO THẢ", 56);
            Controls.Add(sec1);

            ModernCard card1 = new ModernCard { Location = new Point(25, 78), Size = new Size(470, 142) };
            Label delayTitle = new Label { Text = "Thời gian giữ icon để gộp nhóm", Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = CollectionTheme.Text, Location = new Point(14, 12), AutoSize = true, BackColor = Color.Transparent };
            Label delaySub = new Label { Text = "Giữ icon trên đích quá ngưỡng này để tạo nhóm. Thả trước đó Windows sẽ xử lý.", Font = new Font("Segoe UI", 8.5f), ForeColor = CollectionTheme.MutedText, Location = new Point(14, 32), Size = new Size(300, 18), BackColor = Color.Transparent };
            delayBadge = new Label { Size = new Size(130, 22), Location = new Point(326, 12), Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(96, 165, 250), TextAlign = ContentAlignment.MiddleRight, BackColor = Color.Transparent };
            delaySlider = new ModernSlider { Minimum = 120, Maximum = 800, Step = 20, Value = Math.Max(120, Math.Min(800, hoverDelay)), Location = new Point(14, 56), Size = new Size(442, 26) };
            Action updateBadge = delegate {
                int ms = delaySlider.Value;
                string desc = ms <= 200 ? "Nhanh" : (ms <= 340 ? "Mặc định" : "Chậm");
                delayBadge.Text = ms + " ms • " + desc;
            };
            delaySlider.ValueChanged += delegate { updateBadge(); };
            updateBadge();

            Panel div1 = new Panel { Location = new Point(14, 88), Size = new Size(442, 1), BackColor = Color.FromArgb(45, 57, 77) };
            Label disTitle = new Label { Text = "Tự giải thể khi còn 1 shortcut", Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = CollectionTheme.Text, Location = new Point(14, 98), AutoSize = true, BackColor = Color.Transparent };
            Label disSub = new Label { Text = "Tự hoàn trả icon gốc ra Desktop và xóa nhóm khi chỉ còn một ứng dụng.", Font = new Font("Segoe UI", 8.5f), ForeColor = CollectionTheme.MutedText, Location = new Point(14, 118), Size = new Size(380, 18), BackColor = Color.Transparent };
            dissolveSwitch = new ModernToggleSwitch { Checked = dissolve, Location = new Point(410, 102) };
            card1.Controls.AddRange(new Control[] { delayTitle, delaySub, delayBadge, delaySlider, div1, disTitle, disSub, dissolveSwitch });
            Controls.Add(card1);

            // Card 2: Hệ thống & Hiệu năng
            Label sec2 = CreateSectionHeader("HỆ THỐNG & HIỆU NĂNG", 232);
            Controls.Add(sec2);

            ModernCard card2 = new ModernCard { Location = new Point(25, 254), Size = new Size(470, 124) };
            Label startTitle = new Label { Text = "Khởi động cùng Windows", Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = CollectionTheme.Text, Location = new Point(14, 12), AutoSize = true, BackColor = Color.Transparent };
            Label startSub = new Label { Text = "Tự động kích hoạt Desktop Folders chạy nền khi bạn đăng nhập.", Font = new Font("Segoe UI", 8.5f), ForeColor = CollectionTheme.MutedText, Location = new Point(14, 32), Size = new Size(380, 18), BackColor = Color.Transparent };
            startupSwitch = new ModernToggleSwitch { Checked = startWin, Location = new Point(410, 18) };

            Panel div2 = new Panel { Location = new Point(14, 60), Size = new Size(442, 1), BackColor = Color.FromArgb(45, 57, 77) };
            Label redTitle = new Label { Text = "Giảm chuyển động (Reduce Motion)", Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = CollectionTheme.Text, Location = new Point(14, 70), AutoSize = true, BackColor = Color.Transparent };
            Label redSub = new Label { Text = "Tắt toàn bộ animation preview, phóng to/thu nhỏ để mở folder tức thì.", Font = new Font("Segoe UI", 8.5f), ForeColor = CollectionTheme.MutedText, Location = new Point(14, 90), Size = new Size(380, 18), BackColor = Color.Transparent };
            reduceSwitch = new ModernToggleSwitch { Checked = reduceMotion, Location = new Point(410, 76) };
            card2.Controls.AddRange(new Control[] { startTitle, startSub, startupSwitch, div2, redTitle, redSub, reduceSwitch });
            Controls.Add(card2);

            // Card 3: Chẩn đoán Desktop Explorer
            Label sec3 = CreateSectionHeader("CHẨN ĐOÁN DESKTOP EXPLORER", 390);
            Controls.Add(sec3);

            ModernCard card3 = new ModernCard { Location = new Point(25, 412), Size = new Size(470, 128) };
            Label connLabel = new Label { Text = "● Kết nối Desktop Explorer (SysListView32)", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(52, 211, 153), Location = new Point(14, 12), AutoSize = true, BackColor = Color.Transparent };
            ModernButton rescanBtn = new ModernButton("Làm mới Desktop", false) { Location = new Point(318, 10), Size = new Size(138, 28) };
            rescanBtn.Click += delegate { if (controller != null) controller.RefreshNow(); targetStatus.Text = ExplorerDesktop.ScanStatus; };
            Panel div3 = new Panel { Location = new Point(14, 44), Size = new Size(442, 1), BackColor = Color.FromArgb(45, 57, 77) };
            targetStatus = new Label { Text = ExplorerDesktop.ScanStatus, AutoSize = false, Size = new Size(442, 70), Location = new Point(14, 52), Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(148, 163, 184), BackColor = Color.Transparent };
            card3.Controls.AddRange(new Control[] { connLabel, rescanBtn, div3, targetStatus });
            Controls.Add(card3);

            // Footer
            Label tip = new Label { Text = "Escape: Đóng  •  Enter: Lưu thay đổi", Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(100, 116, 139), Location = new Point(28, 568), AutoSize = true, BackColor = Color.Transparent };
            ModernButton cancelBtn = new ModernButton("Đóng", false) { Location = new Point(292, 560), Size = new Size(88, 36) };
            cancelBtn.Click += delegate { Close(); };
            ModernButton saveBtn = new ModernButton("Lưu cài đặt", true) { Location = new Point(390, 560), Size = new Size(105, 36) };
            saveBtn.Click += delegate { Apply(); Close(); };
            Controls.AddRange(new Control[] { tip, cancelBtn, saveBtn });

            KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter) { Apply(); Close(); e.Handled = true; }
            };
        }

        Label CreateSectionHeader(string text, int y)
        {
            return new Label { Text = text, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = Color.FromArgb(148, 163, 184), Location = new Point(28, y), AutoSize = true, BackColor = Color.Transparent };
        }

        void Apply()
        {
            if (controller == null || controller.Settings == null) return;
            controller.Settings.StartWithWindows = startupSwitch.Checked;
            controller.Settings.ReduceMotion = reduceSwitch.Checked;
            controller.Settings.DissolveSingleAppGroup = dissolveSwitch.Checked;
            controller.Settings.FolderHoverDelay = delaySlider.Value;
            controller.SaveSettings();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            Rectangle rounded = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            using (System.Drawing.Drawing2D.GraphicsPath path = DrawExtensions.RoundPath(rounded, 16))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (Pen borderPen = new Pen(CollectionTheme.Border, 1.5f))
                e.Graphics.DrawRoundedRectangle(borderPen, rect, 16);
        }
    }

    internal sealed class DirectDesktopController : ApplicationContext
    {
        enum DragOwnership { Windows, HoverPending, MergeArmed }
        readonly object cacheLock = new object();
        DesktopItem[] cache = new DesktopItem[0];
        int scanInProgress;
        int inactiveTicks;
        volatile bool exiting;
        System.Threading.Timer scanner;
        Native.MouseHook callback;
        IntPtr hook;
        DesktopItem source;
        DesktopItem hoverTarget;
        bool dragging;
        Point down;
        int hoverStarted;
        DragOwnership dragOwnership;
        DesktopItem armedTarget;
        FolderPreviewOverlay folderPreview;
        NotifyIcon tray;
        System.Windows.Forms.Timer deferred;
        System.Windows.Forms.Timer postDrop;
        System.Windows.Forms.Timer hoverArm;
        System.Windows.Forms.Timer refreshAfterWindowsDrop;
        System.Windows.Forms.Timer commandPump;
        DesktopItem pendingSource;
        DesktopItem pendingTarget;
        Action pending;
        SingleInstanceCommandWindow commandWindow;
        readonly object openRequestLock = new object();
        readonly HashSet<string> pendingOpenRequests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int requestFileTicks;
        internal AppSettings Settings { get; private set; }

        internal DirectDesktopController(string requestedGroupId)
        {
            Settings = DataStore.LoadSettings();
            commandWindow = new SingleInstanceCommandWindow(delegate(string groupId) { EnqueueOpenGroup(groupId); });
            folderPreview = new FolderPreviewOverlay(); folderPreview.CreateControl();

            deferred = new System.Windows.Forms.Timer { Interval = 10 };
            deferred.Tick += delegate { deferred.Stop(); Action action = pending; pending = null; if (action != null) action(); };
            postDrop = new System.Windows.Forms.Timer { Interval = 140 };
            postDrop.Tick += delegate { postDrop.Stop(); CommitPendingFolder(); };
            hoverArm = new System.Windows.Forms.Timer();
            hoverArm.Tick += delegate { ArmHoverTarget(); };
            refreshAfterWindowsDrop = new System.Windows.Forms.Timer { Interval = 450 };
            refreshAfterWindowsDrop.Tick += delegate { refreshAfterWindowsDrop.Stop(); RefreshCache(); };
            commandPump = new System.Windows.Forms.Timer { Interval = 20 };
            commandPump.Tick += delegate { DrainOpenGroupRequests(); };
            commandPump.Start();

            Icon appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            tray = new NotifyIcon { Icon = appIcon, Visible = false, Text = "Desktop Folders" };
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Desktop Folders đang hoạt động").Enabled = false;
            menu.Items.Add("Settings…", null, delegate { OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { OpenSettings(); }; tray.Visible = true;

            callback = HookCallback; hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, callback, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                MessageBox.Show("Không thể kết nối với thao tác chuột của Desktop. Mã lỗi: " + Marshal.GetLastWin32Error(), "Desktop Folders"); ExitThread(); return;
            }
            if (!String.IsNullOrEmpty(requestedGroupId)) EnqueueOpenGroup(requestedGroupId);
            StartBackgroundInitialization();
        }

        void StartBackgroundInitialization()
        {
            Thread initialization = new Thread(new ThreadStart(delegate {
                try
                {
#if !TEST
                    if (!exiting) RegisterVirtualGroupType();
                    bool repairedTiles = false;
                    if (!exiting && NeedsVirtualTileRepair()) { EnsureVirtualTiles(); repairedTiles = true; }
#endif
                    if (!exiting) { RefreshCache();
#if !TEST
                        if (repairedTiles) FolderPanel.NotifyLayoutChanged();
#endif
                    }
                    if (!exiting) scanner = new System.Threading.Timer(delegate {
                        if (ExplorerDesktop.IsDesktopForeground()) { inactiveTicks = 0; RefreshCache(); }
                        else if (++inactiveTicks >= 10)
                        {
                            inactiveTicks = 0;
                            try { using (Process current = Process.GetCurrentProcess()) Native.EmptyWorkingSet(current.Handle); } catch { }
                        }
                    }, null, 700, 1200);
                }
                catch (Exception error) { DataStore.LogDrag("INIT failed=" + error.Message); }
            }));
            initialization.IsBackground = true; initialization.Name = "DesktopFolders initialization"; initialization.SetApartmentState(ApartmentState.STA); initialization.Start();
        }

        bool NeedsVirtualTileRepair()
        {
            VirtualLayout layout = DataStore.LoadVirtualLayout();
            DateTime executableStamp = File.GetLastWriteTimeUtc(Application.ExecutablePath);
            return layout.Groups.Any(g => String.IsNullOrEmpty(g.TilePath) || !Path.GetExtension(g.TilePath).Equals(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(g.TilePath) || String.IsNullOrEmpty(g.IconPath) || !File.Exists(g.IconPath) || File.GetLastWriteTimeUtc(g.IconPath) < executableStamp);
        }

        void EnqueueOpenGroup(string groupId)
        {
            if (String.IsNullOrWhiteSpace(groupId)) return; lock (openRequestLock) pendingOpenRequests.Add(groupId.Trim());
        }

        void DrainOpenGroupRequests()
        {
            if (++requestFileTicks >= 50) { requestFileTicks = 0; foreach (string fileRequest in DataStore.TakeOpenRequests()) EnqueueOpenGroup(fileRequest); }
            string[] requests; lock (openRequestLock) { if (pendingOpenRequests.Count == 0) return; requests = pendingOpenRequests.ToArray(); pendingOpenRequests.Clear(); }
            foreach (string request in requests) OpenGroupById(request);
        }

        void OpenGroupById(string groupId)
        {
            string requested = (groupId ?? "").Trim();
            try { if (File.Exists(requested) && Path.GetExtension(requested).Equals(".desktopgroup", StringComparison.OrdinalIgnoreCase)) requested = File.ReadAllText(requested).Trim(); } catch { }
            VirtualGroup group = DataStore.LoadVirtualLayout().Groups.FirstOrDefault(g => g.Id.Equals(requested, StringComparison.OrdinalIgnoreCase) || (!String.IsNullOrEmpty(g.TilePath) && g.TilePath.Equals(requested, StringComparison.OrdinalIgnoreCase)));
            if (group == null) { DataStore.LogDrag("OPEN group not found=" + groupId); return; }
            DesktopItem[] snapshot; lock (cacheLock) snapshot = cache;
            DesktopItem tile = snapshot.FirstOrDefault(item => item.IsGroup && item.Path.Equals(group.TilePath, StringComparison.OrdinalIgnoreCase));
            Rectangle exactBounds; Rectangle origin = tile != null ? tile.Bounds : (ExplorerDesktop.TryGetIconBounds(group.TilePath, out exactBounds) ? exactBounds : new Rectangle(Cursor.Position.X - 48, Cursor.Position.Y - 48, 96, 96));
            try { DataStore.LogDrag("OPEN group=" + group.Name); FolderPanel.ShowOrActivate(group.TilePath, origin, Settings); }
            catch (Exception error) { DataStore.LogDrag("OPEN failed=" + error.Message); }
        }

        void RegisterVirtualGroupType()
        {
            try
            {
                bool changed = false;
                changed |= SetRegistryDefault("Software\\Classes\\.desktopgroup", "DesktopFolders.VirtualGroup");
                changed |= SetRegistryDefault("Software\\Classes\\DesktopFolders.VirtualGroup", "Desktop Folders Group");
                changed |= SetRegistryDefault("Software\\Classes\\DesktopFolders.VirtualGroup\\DefaultIcon", Application.ExecutablePath + ",0");
                changed |= SetRegistryDefault("Software\\Classes\\DesktopFolders.VirtualGroup\\shell\\open\\command", "\"" + Application.ExecutablePath + "\" --open-group \"%1\"");
                if (changed) Native.SHChangeNotify(Native.SHCNE_ASSOCCHANGED, Native.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        static bool SetRegistryDefault(string subKey, string requested)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subKey))
            {
                string current = key.GetValue("") as string;
                if (String.Equals(current, requested, StringComparison.Ordinal)) return false;
                key.SetValue("", requested); return true;
            }
        }

        void EnsureVirtualTiles()
        {
            VirtualLayout layout = DataStore.LoadVirtualLayout();
            foreach (VirtualGroup group in layout.Groups)
            {
                string oldTile = group.TilePath;
                if (String.IsNullOrEmpty(group.TilePath) || !Path.GetExtension(group.TilePath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    string candidate = Path.Combine(ExplorerDesktop.Desktop, group.Name + ".lnk"); int suffix = 2;
                    while (File.Exists(candidate)) candidate = Path.Combine(ExplorerDesktop.Desktop, group.Name + " (" + suffix++ + ").lnk");
                    group.TilePath = candidate;
                }
                try
                {
                    GroupTileFactory.CreateOrUpdate(group);
                    if (!String.IsNullOrEmpty(oldTile) && !oldTile.Equals(group.TilePath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldTile)) File.Delete(oldTile);
                }
                catch { group.TilePath = oldTile; }
            }
            DataStore.SaveVirtualLayout(layout);
        }

        void RefreshCache()
        {
            if (Interlocked.Exchange(ref scanInProgress, 1) != 0) return;
            try
            {
                DesktopItem[] fresh = ExplorerDesktop.Scan();
                lock (cacheLock) cache = fresh;
                FolderPanel.SyncDesktopItems(fresh);
            }
            finally { Interlocked.Exchange(ref scanInProgress, 0); }
        }

        internal void RefreshNow() { RefreshCache(); }

        DesktopItem Hit(Point point)
        {
            DesktopItem[] snapshot; lock (cacheLock) snapshot = cache;
            return ExplorerDesktop.HitTest(snapshot, point, source == null ? null : source.Path);
        }

        IntPtr HookCallback(int code, IntPtr message, IntPtr data)
        {
            if (code < 0) return Native.CallNextHookEx(hook, code, message, data);
            Native.MSLLHOOKSTRUCT raw = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(data, typeof(Native.MSLLHOOKSTRUCT));
            Point point = new Point(raw.pt.x, raw.pt.y); int action = message.ToInt32();

            if ((action == Native.WM_RBUTTONDOWN || action == Native.WM_MBUTTONDOWN) && source != null)
            {
                ResetDrag(); return Native.CallNextHookEx(hook, code, message, data);
            }

            if (action == Native.WM_LBUTTONDOWN)
            {
                source = null;
                if (ExplorerDesktop.IsDesktopForeground())
                {
                    FolderPanel.NotifyDesktopClick(point);
                    DesktopItem[] snapshot; lock (cacheLock) snapshot = cache;
                    source = ExplorerDesktop.HitTest(snapshot, point, null);
                }
                dragging = false; down = point; hoverTarget = null; armedTarget = null; dragOwnership = DragOwnership.Windows; hoverArm.Stop();
                if (source != null) DataStore.LogDrag("DOWN source=" + source.Name);
                return Native.CallNextHookEx(hook, code, message, data);
            }

            if (source != null && action == Native.WM_MOUSEMOVE)
            {
                if ((Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) == 0)
                {
                    ResetDrag(); return Native.CallNextHookEx(hook, code, message, data);
                }
                int thresholdX = Math.Max(2, SystemInformation.DragSize.Width / 2), thresholdY = Math.Max(2, SystemInformation.DragSize.Height / 2);
                if (!dragging && (Math.Abs(point.X - down.X) >= thresholdX || Math.Abs(point.Y - down.Y) >= thresholdY))
                {
                    dragging = true;
                }
                if (!dragging) return Native.CallNextHookEx(hook, code, message, data);
                if (dragOwnership == DragOwnership.MergeArmed)
                {
                    // Do not swallow WM_MOUSEMOVE: Explorer keeps its native drag image
                    // responsive. The mouse-up is still intercepted before a drop can
                    // reach Explorer. Leaving the target safely returns ownership because
                    // the OLE loop has not been cancelled yet.
                    if (armedTarget != null && Rectangle.Inflate(armedTarget.Bounds, 8, 8).Contains(point))
                    {
                        if (!folderPreview.Visible) folderPreview.Arm(armedTarget, true);
                    }
                    else { armedTarget = null; CancelHover(); }
                    return Native.CallNextHookEx(hook, code, message, data);
                }
                // Collection tiles remain ordinary movable Desktop items; this version
                // does not nest one collection inside another.
                if (source.IsGroup) { CancelHover(); return Native.CallNextHookEx(hook, code, message, data); }
                DesktopItem target = Hit(point);
                if (target != null && target.Path != source.Path)
                {
                    if (hoverTarget == null || hoverTarget.Path != target.Path)
                    {
                        hoverTarget = target; hoverStarted = Environment.TickCount; dragOwnership = DragOwnership.HoverPending; folderPreview.HideAnimated();
                        hoverArm.Stop(); hoverArm.Interval = Settings.FolderHoverDelay; hoverArm.Start();
                        DataStore.LogDrag("HOVER_PENDING target=" + target.Name + " delay=" + Settings.FolderHoverDelay);
                    }
                }
                else CancelHover();
                return Native.CallNextHookEx(hook, code, message, data);
            }

            if (source != null && action == Native.WM_LBUTTONUP)
            {
                DesktopItem start = source, target = armedTarget; bool wasDragging = dragging;
                bool owned = wasDragging && dragOwnership == DragOwnership.MergeArmed;
                bool folderReady = owned && target != null && Rectangle.Inflate(target.Bounds, 8, 8).Contains(point);
                DataStore.LogDrag("UP owned=" + owned + " commit=" + folderReady + (target == null ? "" : " target=" + target.Name));
                if (owned)
                {
                    // Cancel Explorer's OLE target immediately before consuming the
                    // physical mouse-up. Mouse movement stayed native up to this point.
                    Native.keybd_event(Native.VK_ESCAPE, 0, 0, UIntPtr.Zero);
                    Native.keybd_event(Native.VK_ESCAPE, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                    IntPtr list = ExplorerDesktop.CachedListView;
                    if (list != IntPtr.Zero) Native.SendMessage(list, Native.WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero);
                }
                source = null; hoverTarget = null; armedTarget = null; dragging = false; dragOwnership = DragOwnership.Windows; hoverArm.Stop(); folderPreview.HideAnimated();
                if (!wasDragging)
                {
                    if (start.IsGroup)
                    {
                        // A collection is a virtual tile, not a normal Explorer shortcut.
                        // Consume this click so Explorer cannot launch a duplicate process;
                        // the named pipe remains the fallback when cache is not ready yet.
                        IntPtr list = ExplorerDesktop.CachedListView;
                        if (list != IntPtr.Zero) Native.SendMessage(list, Native.WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero);
                        Queue(delegate { FolderPanel.ShowOrActivate(start.Path, start.Bounds, Settings); });
                        return (IntPtr)1;
                    }
                    return Native.CallNextHookEx(hook, code, message, data);
                }
                if (folderReady)
                {
                    pendingSource = start; pendingTarget = target;
                    postDrop.Stop(); postDrop.Start();
                }
                // When MERGE_ARMED, even a release outside the armed tile is consumed:
                // Windows can no longer become the target again during this gesture.
                if (owned) return (IntPtr)1;
                refreshAfterWindowsDrop.Stop(); refreshAfterWindowsDrop.Start();
                return Native.CallNextHookEx(hook, code, message, data);
            }
            return Native.CallNextHookEx(hook, code, message, data);
        }

        void Queue(Action action) { pending = action; deferred.Stop(); deferred.Start(); }

        void ResetDrag()
        {
            source = null; hoverTarget = null; armedTarget = null; dragging = false; dragOwnership = DragOwnership.Windows; hoverArm.Stop(); folderPreview.HideAnimated();
        }

        void CancelHover()
        {
            hoverTarget = null; hoverStarted = 0; dragOwnership = DragOwnership.Windows; hoverArm.Stop(); folderPreview.HideAnimated();
        }

        void ArmHoverTarget()
        {
            hoverArm.Stop();
            if (source == null || !dragging || dragOwnership != DragOwnership.HoverPending || hoverTarget == null) return;
            if ((Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) == 0 || !Rectangle.Inflate(hoverTarget.Bounds, 8, 8).Contains(Cursor.Position)) { CancelHover(); return; }
            armedTarget = hoverTarget; dragOwnership = DragOwnership.MergeArmed;
            folderPreview.Arm(armedTarget, Settings.ReduceMotion);
            DataStore.LogDrag("MERGE_ARMED target=" + armedTarget.Name + " elapsed=" + unchecked(Environment.TickCount - hoverStarted));
        }

        void CommitPendingFolder()
        {
            DesktopItem first = pendingSource, target = pendingTarget; pendingSource = null; pendingTarget = null;
            if (first == null || target == null) return;
            if (target.IsGroup)
            {
                if (File.Exists(first.Path)) AddOrCreateGroup(first, target);
                RefreshCache(); return;
            }
            if (File.Exists(first.Path) && File.Exists(target.Path)) AddOrCreateGroup(first, target);
            RefreshCache();
        }

        void AddOrCreateGroup(DesktopItem first, DesktopItem second)
        {
            VirtualLayout layout = DataStore.LoadVirtualLayout();
            try
            {
                if (second.IsGroup)
                {
                    VirtualGroup existing = layout.Groups.FirstOrDefault(g => g.TilePath.Equals(second.Path, StringComparison.OrdinalIgnoreCase));
                    if (existing == null) throw new InvalidDataException("Không tìm thấy virtual group.");
                    if (existing.Members.Any(m => m.Path.Equals(first.Path, StringComparison.OrdinalIgnoreCase))) return;
                    FileAttributes original = File.GetAttributes(first.Path); VirtualMember added = new VirtualMember { Path = first.Path, OriginalAttributes = (int)original };
                    try { File.SetAttributes(first.Path, original | FileAttributes.Hidden); existing.Members.Add(added); DataStore.SaveVirtualLayout(layout); ExplorerDesktop.NotifyPathChanged(first.Path); }
                    catch { existing.Members.Remove(added); try { File.SetAttributes(first.Path, original); } catch { } throw; }
                    try { GroupTileFactory.CreateOrUpdate(existing); } catch { }
                    FolderPanel.NotifyLayoutChanged();
                    return;
                }
                FileAttributes firstAttributes = File.GetAttributes(first.Path), secondAttributes = File.GetAttributes(second.Path);
                string name = ExplorerDesktop.NewGroupName(), groupId = Guid.NewGuid().ToString("N");
                string tilePath = Path.Combine(ExplorerDesktop.Desktop, name + ".lnk");
                VirtualGroup group = new VirtualGroup { Id = groupId, Name = name, TilePath = tilePath };
                group.Members.Add(new VirtualMember { Path = second.Path, OriginalAttributes = (int)secondAttributes });
                group.Members.Add(new VirtualMember { Path = first.Path, OriginalAttributes = (int)firstAttributes });
                try
                {
                    File.SetAttributes(second.Path, secondAttributes | FileAttributes.Hidden);
                    File.SetAttributes(first.Path, firstAttributes | FileAttributes.Hidden);
                    GroupTileFactory.CreateOrUpdate(group); layout.Groups.Add(group); DataStore.SaveVirtualLayout(layout); ExplorerDesktop.NotifyPathChanged(first.Path); ExplorerDesktop.NotifyPathChanged(second.Path);
                    FolderPanel.NotifyLayoutChanged();
                }
                catch
                {
                    try { File.SetAttributes(second.Path, secondAttributes); } catch { }
                    try { File.SetAttributes(first.Path, firstAttributes); } catch { }
                    try { if (File.Exists(tilePath)) File.Delete(tilePath); } catch { }
                    try { if (!String.IsNullOrEmpty(group.IconPath) && File.Exists(group.IconPath)) File.Delete(group.IconPath); } catch { }
                    throw;
                }
                // Never write SysListView32 positions by an AutomationElement index:
                // those index spaces are not guaranteed to match and can move an
                // unrelated Desktop icon. Explorer chooses the free grid slot.
                RefreshCache();
            }
            catch (Exception error) { MessageBox.Show("Không thể tạo virtual folder: " + error.Message, "Desktop Folders"); }
        }

        SettingsForm activeSettingsForm;

        internal void OpenSettings()
        {
            if (activeSettingsForm != null && !activeSettingsForm.IsDisposed)
            {
                if (activeSettingsForm.WindowState == FormWindowState.Minimized) activeSettingsForm.WindowState = FormWindowState.Normal;
                activeSettingsForm.BringToFront();
                activeSettingsForm.Activate();
                return;
            }
            activeSettingsForm = new SettingsForm(this);
            activeSettingsForm.Show();
        }

        internal void SaveSettings()
        {
            DataStore.SaveSettings(Settings);
            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (Settings.StartWithWindows) key.SetValue("DesktopFolders", Application.ExecutablePath); else key.DeleteValue("DesktopFolders", false);
                key.Close();
            }
            catch { MessageBox.Show("Windows không cho phép thay đổi Startup.", "Desktop Folders"); }
        }

        protected override void ExitThreadCore()
        {
            exiting = true;
            if (scanner != null) scanner.Dispose(); if (hoverArm != null) hoverArm.Dispose(); if (refreshAfterWindowsDrop != null) refreshAfterWindowsDrop.Dispose(); if (commandPump != null) commandPump.Dispose(); if (commandWindow != null) commandWindow.Dispose(); if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            folderPreview.Dispose(); base.ExitThreadCore();
        }
    }

    internal static class DrawExtensions
    {
        internal static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle rectangle, int radius)
        { using (System.Drawing.Drawing2D.GraphicsPath path = RoundPath(rectangle, radius)) graphics.DrawPath(pen, path); }
        internal static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rectangle, int radius)
        { using (System.Drawing.Drawing2D.GraphicsPath path = RoundPath(rectangle, radius)) graphics.FillPath(brush, path); }
        internal static System.Drawing.Drawing2D.GraphicsPath RoundPath(Rectangle r, int radius)
        {
            int d = radius * 2; System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90); path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
#if TEST
            if (args != null && args.Length >= 5 && args[0] == "--test-tile")
            {
                VirtualGroup testGroup = new VirtualGroup { Id = args[1], Name = "Test Collection", TilePath = args[2], IconPath = args[3] };
                testGroup.Members.Add(new VirtualMember { Path = args[4] });
                if (args.Length >= 6) testGroup.Members.Add(new VirtualMember { Path = args[5] });
                GroupTileFactory.CreateOrUpdate(testGroup); return;
            }
#endif
            bool created;
            string requestedGroupId = null;
            if (args != null && args.Length >= 2 && args[0].Equals("--open-group", StringComparison.OrdinalIgnoreCase)) requestedGroupId = args[1];
            string mutexName = "DesktopFolders.Direct.SingleInstance";
#if TEST
            mutexName = "DesktopFolders.Direct.TestInstance";
#endif
            using (Mutex instance = new Mutex(true, mutexName, out created))
            {
                if (!created)
                {
                    if (!String.IsNullOrEmpty(requestedGroupId) && !SingleInstanceCommandWindow.SendOpenGroup(requestedGroupId)) DataStore.SaveOpenRequest(requestedGroupId);
                    return;
                }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new DirectDesktopController(requestedGroupId));
            }
        }
    }
}
