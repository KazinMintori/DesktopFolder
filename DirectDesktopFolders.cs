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

    internal sealed class AppTile : Panel
    {
        readonly Image icon;
        readonly string label;
        readonly bool pinned;
        readonly bool gridMode;
        bool hot;

        internal AppTile(string path, bool isPinned, bool grid, int width)
        {
            label = Path.GetFileNameWithoutExtension(path); pinned = isPinned; gridMode = grid; icon = IconLoader.ForPath(path);
            Size = grid ? new Size(width, width < 170 ? 112 : 164) : new Size(width, 76); BackColor = Color.Transparent; Cursor = Cursors.Hand;
            TabStop = true; AccessibleRole = AccessibleRole.ListItem; AccessibleName = label; AccessibleDescription = pinned ? "Đã ghim ưu tiên" : "";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Focus(); base.OnMouseDown(e); }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle card = new Rectangle(0, 0, Width - 2, Height - 2);
            using (Brush fill = new SolidBrush(hot || Focused ? CollectionTheme.SurfaceHover : CollectionTheme.Surface)) e.Graphics.FillRoundedRectangle(fill, card, CollectionTheme.RadiusCard);
            if (Focused) using (Pen focus = new Pen(CollectionTheme.Focus, 2f)) e.Graphics.DrawRoundedRectangle(focus, Rectangle.Inflate(card, -1, -1), CollectionTheme.RadiusCard - 1);
            if (pinned)
            {
                using (Pen pin = new Pen(CollectionTheme.Focus, 1.6f)) { e.Graphics.DrawEllipse(pin, Width - 18, 10, 7, 7); e.Graphics.DrawLine(pin, Width - 14.5f, 17, Width - 14.5f, 23); }
            }
            if (gridMode && Width < 170)
            {
                int iconSize = 44, iconX = (Width - iconSize) / 2;
                if (icon != null) e.Graphics.DrawImage(icon, new Rectangle(iconX, 10, iconSize, iconSize));
                using (Font name = new Font("Segoe UI", 9f, FontStyle.Bold))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
                using (Brush text = new SolidBrush(CollectionTheme.Text)) e.Graphics.DrawString(label, name, text, new Rectangle(6, 58, Width - 12, 49), format);
            }
            else if (gridMode)
            {
                if (icon != null) e.Graphics.DrawImage(icon, new Rectangle(24, 50, 64, 64));
                using (Font name = new Font("Segoe UI", 12f, FontStyle.Bold))
                using (StringFormat format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
                using (Brush text = new SolidBrush(CollectionTheme.Text)) e.Graphics.DrawString(label, name, text, new Rectangle(104, 28, Math.Max(40, Width - 120), 108), format);
            }
            else
            {
                if (icon != null) e.Graphics.DrawImage(icon, new Rectangle(14, 12, 52, 52));
                using (Font name = new Font("Segoe UI", 11f, FontStyle.Bold))
                using (StringFormat format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
                using (Brush text = new SolidBrush(CollectionTheme.Text)) e.Graphics.DrawString(label, name, text, new Rectangle(82, 10, Math.Max(40, Width - 96), 56), format);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing && icon != null) icon.Dispose(); base.Dispose(disposing); }
    }

    internal static class CollectionTheme
    {
        internal static readonly Color Background = Color.FromArgb(15, 23, 42);
        internal static readonly Color Surface = Color.FromArgb(27, 35, 54);
        internal static readonly Color SurfaceHover = Color.FromArgb(38, 49, 72);
        internal static readonly Color Control = Color.FromArgb(30, 41, 59);
        internal static readonly Color ControlActive = Color.FromArgb(51, 65, 85);
        internal static readonly Color Border = Color.FromArgb(71, 85, 105);
        internal static readonly Color Focus = Color.FromArgb(147, 197, 253);
        internal static readonly Color Text = Color.FromArgb(248, 250, 252);
        internal static readonly Color MutedText = Color.FromArgb(148, 163, 184);
        internal static readonly Color Danger = Color.FromArgb(190, 50, 60);
        internal const int RadiusWindow = 24;
        internal const int RadiusControl = 12;
        internal const int RadiusCard = 16;
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
            kind = iconKind; Width = width; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = CollectionTheme.Background; ForeColor = CollectionTheme.Text; Cursor = Cursors.Hand; TabStop = true; Text = ""; AccessibleRole = AccessibleRole.PushButton; AccessibleName = iconKind.ToString();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            Rectangle box = new Rectangle(4, 5, Math.Max(10, Width - 8), Math.Max(10, Height - 10));
            Color background = hot || Focused ? (kind == HeaderIconKind.Close ? CollectionTheme.Danger : CollectionTheme.ControlActive) : (selected ? CollectionTheme.ControlActive : Color.Transparent);
            if (background.A > 0) using (Brush fill = new SolidBrush(background)) e.Graphics.FillRoundedRectangle(fill, box, 8);
            float cx = Width / 2f, cy = Height / 2f;
            using (Pen pen = new Pen(ForeColor, 1.8f))
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
            BackColor = CollectionTheme.Control; ForeColor = CollectionTheme.Text; TabStop = false; AccessibleRole = AccessibleRole.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float cx = Width / 2f - 2, cy = Height / 2f - 2;
            using (Pen pen = new Pen(ForeColor, 1.8f)) { pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round; e.Graphics.DrawEllipse(pen, cx - 7, cy - 7, 13, 13); e.Graphics.DrawLine(pen, cx + 4, cy + 4, cx + 10, cy + 10); }
        }
    }

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
        System.Windows.Forms.Timer releaseTopMost;

        internal static void ShowOrActivate(string tilePath, Rectangle source, AppSettings currentSettings)
        {
            VirtualGroup requested = DataStore.LoadVirtualLayout().Groups.FirstOrDefault(g => !String.IsNullOrEmpty(g.TilePath) && g.TilePath.Equals(tilePath, StringComparison.OrdinalIgnoreCase));
            if (requested == null) return;
            lock (OpenPanelsLock)
            {
                WeakReference reference; FolderPanel existing = null;
                if (OpenPanels.TryGetValue(requested.Id, out reference)) existing = reference.Target as FolderPanel;
                if (existing != null && !existing.IsDisposed)
                {
                    existing.Promote(); return;
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
                DesktopItem tile = items.FirstOrDefault(item => item.IsGroup && panel.group != null && !String.IsNullOrEmpty(panel.group.TilePath) && item.Path.Equals(panel.group.TilePath, StringComparison.OrdinalIgnoreCase));
                if (tile != null) panel.QueueAnchorUpdate(tile.Bounds);
            }
        }

        internal FolderPanel(string tilePath, Rectangle source, AppSettings currentSettings)
        {
            settings = currentSettings; layout = DataStore.LoadVirtualLayout();
            group = layout.Groups.FirstOrDefault(g => g.TilePath.Equals(tilePath, StringComparison.OrdinalIgnoreCase));
            if (group == null) throw new InvalidDataException("Không tìm thấy dữ liệu virtual group.");
            groupId = group.Id; anchorBounds = source; animationOrigin = Rectangle.Inflate(source, -5, -5); Text = "Desktop Folders — " + group.Name;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; BackColor = CollectionTheme.Border; Padding = new Padding(2); Opacity = 1.0; AllowDrop = true; KeyPreview = true;
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

            GradientPanel shell = new GradientPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 8, 12), CornerRadius = 22 };
            Controls.Add(shell);
            Panel header = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.Transparent };
            shell.Controls.Add(header);
            HeaderIconButton close = HeaderButton(HeaderIconKind.Close, 36); close.Dock = DockStyle.Right; close.Click += delegate { CloseWithMorph(); };
            expandButton = HeaderButton(HeaderIconKind.Expand, 36); expandButton.Dock = DockStyle.Right; expandButton.Click += delegate { ToggleExpanded(); };
            listButton = HeaderButton(HeaderIconKind.List, 36); listButton.Dock = DockStyle.Right; listButton.Click += delegate { gridMode = false; UpdateModeButtons(); RenderApps(); };
            gridButton = HeaderButton(HeaderIconKind.Grid, 36); gridButton.Dock = DockStyle.Right; gridButton.Click += delegate { gridMode = true; UpdateModeButtons(); RenderApps(); };
            close.AccessibleName = "Đóng collection"; expandButton.AccessibleName = "Phóng to collection"; listButton.AccessibleName = "Bố cục danh sách"; gridButton.AccessibleName = "Bố cục lưới";
            // Controls dock from the last added element toward the outside edge.
            // Visual order, left-to-right: grid, list, expand, close.
            header.Controls.Add(gridButton); header.Controls.Add(listButton); header.Controls.Add(expandButton); header.Controls.Add(close);
            tooltips = new ToolTip(); tooltips.SetToolTip(gridButton, "Bố cục lưới"); tooltips.SetToolTip(listButton, "Bố cục danh sách"); tooltips.SetToolTip(expandButton, "Phóng to collection"); tooltips.SetToolTip(close, "Đóng collection");

            Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            shell.Controls.Add(content); content.BringToFront();
            titleLabel = new Label { Text = group.Name, ForeColor = CollectionTheme.Text, BackColor = Color.Transparent, Font = new Font("Segoe UI", 18, FontStyle.Bold), Location = new Point(0, 1), Width = 190, Height = 42, Cursor = Cursors.IBeam, TextAlign = ContentAlignment.MiddleLeft };
            titleEditor = new TextBox { Text = group.Name, Visible = false, BorderStyle = BorderStyle.None, BackColor = CollectionTheme.Background, ForeColor = CollectionTheme.Text, Font = new Font("Segoe UI", 18, FontStyle.Bold), Location = new Point(0, 8), Width = 190 };
            titleLabel.Click += delegate { BeginTitleEdit(); };
            titleEditor.Leave += delegate { CommitTitleEdit(); };
            titleEditor.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { CommitTitleEdit(); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Escape) { EndTitleEdit(false); e.SuppressKeyPress = true; } };
            header.Controls.Add(titleLabel); header.Controls.Add(titleEditor); titleLabel.SendToBack(); titleEditor.SendToBack();
            header.Resize += delegate { int titleWidth = Math.Max(70, header.ClientSize.Width - 152); titleLabel.Width = titleWidth; titleEditor.Width = titleWidth; };

            RoundedPanel searchContainer = new RoundedPanel { Location = new Point(0, 0), Height = 48, Radius = CollectionTheme.RadiusControl, BackColor = CollectionTheme.Control, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            SearchGlyphControl searchIcon = new SearchGlyphControl { Dock = DockStyle.Left, Width = 44 };
            HeaderIconButton clear = HeaderButton(HeaderIconKind.Close, 36); clear.Dock = DockStyle.Right; clear.BackColor = CollectionTheme.Control; clear.Click += delegate { search.Clear(); search.Focus(); };
            clear.AccessibleName = "Xóa tìm kiếm";
            tooltips.SetToolTip(clear, "Xóa tìm kiếm");
            search = new TextBox { BorderStyle = BorderStyle.None, BackColor = CollectionTheme.Control, ForeColor = CollectionTheme.Text, Font = new Font("Segoe UI", 11), Location = new Point(44, 14), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Height = 24, AccessibleName = "Tìm trong collection" };
            searchHint = new Label { Text = "Tìm trong collection…", ForeColor = CollectionTheme.MutedText, BackColor = CollectionTheme.Control, Font = new Font("Segoe UI", 10.5f), AutoSize = true, Location = new Point(48, 15), Cursor = Cursors.IBeam };
            searchHint.Click += delegate { search.Focus(); };
            searchContainer.Controls.Add(search); searchContainer.Controls.Add(searchIcon); searchContainer.Controls.Add(clear); searchContainer.Controls.Add(searchHint); searchHint.BringToFront();
            content.Controls.Add(searchContainer);

            appsViewport = new Panel { Location = new Point(0, 64), BackColor = Color.Transparent, AllowDrop = true, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            apps = new FlowLayoutPanel { Location = Point.Empty, AutoScroll = false, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, BackColor = Color.Transparent, Padding = new Padding(5, 0, 0, 0), AllowDrop = true };
            themedScroll = new DarkScrollBar(appsViewport, apps) { Width = 6, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right };
            appsViewport.Controls.Add(apps); content.Controls.Add(appsViewport);
            content.Controls.Add(themedScroll); themedScroll.BringToFront();
            search.TextChanged += delegate { searchHint.Visible = search.TextLength == 0; RenderApps(); };
            Action layoutContent = delegate {
                int titleWidth = Math.Max(70, header.ClientSize.Width - 152); titleLabel.Width = titleWidth; titleEditor.Width = titleWidth; searchContainer.Width = Math.Max(100, content.ClientSize.Width - 8); search.Width = Math.Max(80, searchContainer.ClientSize.Width - 88);
                int contentHeight = Math.Max(100, content.ClientSize.Height - 64); int scrollX = Math.Max(100, content.ClientSize.Width - 12);
                appsViewport.Size = new Size(Math.Max(100, scrollX - 16), contentHeight); apps.Width = appsViewport.ClientSize.Width;
                themedScroll.Location = new Point(scrollX, 64); themedScroll.Height = contentHeight;
            };
            content.Resize += delegate { layoutContent(); };
            MouseEventHandler clearTextFocus = delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ClearTextFocus(); };
            MouseDown += clearTextFocus; shell.MouseDown += clearTextFocus; header.MouseDown += clearTextFocus; content.MouseDown += clearTextFocus; appsViewport.MouseDown += clearTextFocus; apps.MouseDown += clearTextFocus; themedScroll.MouseDown += clearTextFocus;
            DragEnter += OnDragEnter; DragDrop += OnDragDrop; appsViewport.DragEnter += OnDragEnter; appsViewport.DragDrop += OnDragDrop; apps.DragEnter += OnDragEnter; apps.DragDrop += OnDragDrop;
            LayoutChanged += OnSharedLayoutChanged; DesktopPointerDown += OnDesktopPointerDown;
            FormClosed += delegate {
                CancelActiveMorph();
                LayoutChanged -= OnSharedLayoutChanged; DesktopPointerDown -= OnDesktopPointerDown;
                lock (OpenPanelsLock) { WeakReference reference; if (OpenPanels.TryGetValue(groupId, out reference) && Object.ReferenceEquals(reference.Target, this)) OpenPanels.Remove(groupId); }
                DataStore.LogDrag("PANEL closed=" + groupId);
            };
            Shown += delegate {
                DataStore.LogDrag("PANEL shown=" + group.Name); UpdateModeButtons(); layoutContent(); RenderApps(); Opacity = 1.0; Promote();
                if (!settings.ReduceMotion) BeginInvoke(new Action(StartOpeningMorph));
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
            Promote(); if (!settings.ReduceMotion) StartMorph(previous, Bounds);
        }

        Bitmap CaptureSnapshot()
        {
            Bitmap image = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            DrawToBitmap(image, ClientRectangle); return image;
        }
        void StartOpeningMorph()
        {
            if (IsDisposed) return; StartMorph(animationOrigin, Bounds);
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
        void CloseWithMorph()
        {
            if (IsDisposed) return;
            if (settings.ReduceMotion) { Close(); return; }
            Bitmap image = null; Rectangle from = Bounds, to = animationOrigin;
            try
            {
                image = CaptureSnapshot(); CancelActiveMorph(); Close(); WindowMorphOverlay overlay = new WindowMorphOverlay(image, from, to, 90, null); overlay.Show();
            }
            catch { if (image != null) image.Dispose(); if (!IsDisposed) Close(); }
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
                Rectangle previous = Bounds; Bounds = new Rectangle(location, finalSize); if (!settings.ReduceMotion) StartMorph(previous, Bounds);
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
            themedScroll.SetValue(0); apps.SuspendLayout(); apps.Controls.Clear(); apps.Top = 0; apps.Height = 10000; string query = search == null ? "" : search.Text.Trim().ToLowerInvariant();
            VirtualMember[] members = group.Members.Where(m => File.Exists(m.Path) && Path.GetFileNameWithoutExtension(m.Path).ToLowerInvariant().Contains(query)).OrderByDescending(m => group.Pinned.Contains(m.Path, StringComparer.OrdinalIgnoreCase)).ToArray();
            bool compactGrid = gridMode && apps.ClientSize.Width < 600;
            int usableWidth = Math.Max(260, apps.ClientSize.Width);
            int width = gridMode ? (compactGrid ? Math.Min(96, Math.Max(76, (usableWidth - 30) / 3)) : Math.Max(140, (usableWidth - 30) / 3)) : Math.Max(250, usableWidth - 10);
            int rowWidth = gridMode ? (width + 10) * 3 : width;
            apps.Padding = new Padding(Math.Max(0, (usableWidth - rowWidth) / 2), 0, 0, 0);
            if (members.Length == 0) apps.Controls.Add(new Label { Text = "Không tìm thấy ứng dụng phù hợp.", ForeColor = CollectionTheme.MutedText, Font = new Font("Segoe UI", 11), AutoSize = true, Margin = new Padding(16) });
            foreach (VirtualMember member in members)
            {
                string path = member.Path; bool pinned = group.Pinned.Contains(path, StringComparer.OrdinalIgnoreCase);
                AppTile tile = new AppTile(path, pinned, gridMode, width) { Margin = gridMode ? new Padding(5, 6, 5, 6) : new Padding(0, 0, 0, 12) };
                tooltips.SetToolTip(tile, Path.GetFileNameWithoutExtension(path));
                tile.MouseWheel += delegate(object sender, MouseEventArgs e) { themedScroll.ScrollBy(-(e.Delta / 120) * 70); HandledMouseEventArgs handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true; };
                Point childDragStart = Point.Empty; bool childDidDrag = false;
                tile.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { childDragStart = e.Location; childDidDrag = false; } };
                tile.MouseMove += delegate(object sender, MouseEventArgs e) {
                    if (e.Button != MouseButtons.Left || childDidDrag) return;
                    if (Math.Abs(e.X - childDragStart.X) < SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y - childDragStart.Y) < SystemInformation.DragSize.Height / 2) return;
                    childDidDrag = true; DataObject data = new DataObject(); data.SetData(SourceGroupFormat, groupId); data.SetData(MemberPathFormat, path);
                    DragDropEffects effect = tile.DoDragDrop(data, DragDropEffects.Link | DragDropEffects.Move);
                    if (effect != DragDropEffects.Move && ExplorerDesktop.IsPointOnDesktopSurface(Cursor.Position)) BeginInvoke(new Action(delegate { MoveOut(path); }));
                };
                tile.Click += delegate { if (childDidDrag) { childDidDrag = false; return; } try { Process.Start(path); } catch (Exception e) { MessageBox.Show(e.Message, "Desktop Folders"); } };
                ContextMenuStrip menu = new ContextMenuStrip(); menu.Items.Add("Mở", null, delegate { try { Process.Start(path); } catch { } });
                menu.Items.Add(pinned ? "Bỏ ghim ưu tiên" : "Ghim ưu tiên", null, delegate { TogglePin(path); });
                menu.Items.Add("Đưa ra Desktop", null, delegate { MoveOut(path); });
                tile.ContextMenuStrip = menu; apps.Controls.Add(tile);
            }
            apps.ResumeLayout(); apps.PerformLayout(); int contentBottom = 0;
            foreach (Control control in apps.Controls) contentBottom = Math.Max(contentBottom, control.Bottom + control.Margin.Bottom);
            apps.Height = Math.Max(appsViewport.ClientSize.Height, contentBottom + 4);
            themedScroll.SyncFromTarget();
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
            e.Effect = !String.IsNullOrEmpty(sourceGroup) && !String.IsNullOrEmpty(memberPath) && sourceGroup != groupId ? DragDropEffects.Move : (e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None);
        }
        void OnDragDrop(object sender, DragEventArgs e)
        {
            string sourceGroup = e.Data.GetData(SourceGroupFormat) as string;
            string internalPath = e.Data.GetData(MemberPathFormat) as string;
            string[] paths = !String.IsNullOrEmpty(internalPath) ? new string[] { internalPath } : e.Data.GetData(DataFormats.FileDrop) as string[]; if (paths == null || paths.Length == 0) return;
            try
            {
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
        protected override void Dispose(bool disposing) { if (disposing) { if (releaseTopMost != null) releaseTopMost.Dispose(); if (tooltips != null) tooltips.Dispose(); } base.Dispose(disposing); }
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
            using (Brush track = new SolidBrush(CollectionTheme.Control)) e.Graphics.FillRoundedRectangle(track, new Rectangle(1, 0, Math.Max(4, Width - 2), Height), Math.Max(2, Width / 2));
            using (Brush thumb = new SolidBrush(hot || dragging ? CollectionTheme.Focus : CollectionTheme.MutedText)) e.Graphics.FillRoundedRectangle(thumb, ThumbRectangle(), Math.Max(2, Width / 2 - 1));
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
        internal RoundedPanel() { Radius = 16; DoubleBuffered = true; Resize += delegate { UpdateRoundedRegion(); }; }
        void UpdateRoundedRegion()
        {
            if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) return;
            using (System.Drawing.Drawing2D.GraphicsPath path = DrawExtensions.RoundPath(ClientRectangle, Radius))
            {
                Region old = Region; Region = new Region(path); if (old != null) old.Dispose();
            }
        }
    }

    internal sealed class SettingsForm : Form
    {
        readonly DirectDesktopController controller;
        CheckBox startup, reduce, dissolve;
        NumericUpDown delay;
        internal SettingsForm(DirectDesktopController owner)
        {
            controller = owner; Text = "Desktop Folders Settings"; StartPosition = FormStartPosition.CenterScreen; Size = new Size(430, 465); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; BackColor = Color.FromArgb(28, 39, 57); ForeColor = Color.White;
            Label heading = new Label { Text = "Settings", AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold), Location = new Point(22, 20), ForeColor = Color.White };
            startup = NewCheck("Khởi động cùng Windows", controller.Settings.StartWithWindows, 70);
            reduce = NewCheck("Giảm chuyển động", controller.Settings.ReduceMotion, 105);
            dissolve = NewCheck("Tự giải thể folder khi chỉ còn một shortcut", controller.Settings.DissolveSingleAppGroup, 140);
            Label delayLabel = new Label { Text = "Thời gian giữ trên icon để tạo folder (ms)", AutoSize = true, Location = new Point(22, 186), ForeColor = Color.White };
            delay = new NumericUpDown { Minimum = 120, Maximum = 800, Increment = 20, Value = Math.Max(120, Math.Min(800, controller.Settings.FolderHoverDelay)), Location = new Point(310, 182), Width = 75 };
            Button backup = new Button { Text = "Backup bố cục…", Location = new Point(22, 235), Size = new Size(120, 29) }; backup.Click += delegate { controller.ExportLayout(); };
            Button restore = new Button { Text = "Khôi phục…", Location = new Point(150, 235), Size = new Size(100, 29) }; restore.Click += delegate { controller.RestoreLayout(); };
            Label targetHeading = new Label { Text = "Desktop target detection", AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(22, 283), ForeColor = Color.White };
            Label targetStatus = new Label { Text = ExplorerDesktop.ScanStatus, AutoSize = false, Size = new Size(364, 72), Location = new Point(22, 310), ForeColor = Color.FromArgb(190, 205, 225) };
            Button rescan = new Button { Text = "Quét lại icon", Location = new Point(22, 385), Size = new Size(100, 27) };
            rescan.Click += delegate { controller.RefreshNow(); targetStatus.Text = ExplorerDesktop.ScanStatus; };
            Button save = new Button { Text = "Lưu thay đổi", Location = new Point(278, 385), Size = new Size(108, 31), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(59, 130, 246), ForeColor = Color.White };
            save.FlatAppearance.BorderSize = 0; save.Click += delegate { Apply(); Close(); };
            Controls.AddRange(new Control[] { heading, startup, reduce, dissolve, delayLabel, delay, backup, restore, targetHeading, targetStatus, rescan, save });
        }
        CheckBox NewCheck(string text, bool value, int y) { return new CheckBox { Text = text, Checked = value, AutoSize = true, Location = new Point(22, y), ForeColor = Color.White, BackColor = BackColor, Font = new Font("Segoe UI", 10) }; }
        void Apply() { controller.Settings.StartWithWindows = startup.Checked; controller.Settings.ReduceMotion = reduce.Checked; controller.Settings.DissolveSingleAppGroup = dissolve.Checked; controller.Settings.FolderHoverDelay = (int)delay.Value; controller.SaveSettings(); }
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
            menu.Items.Add("Settings…", null, delegate { new SettingsForm(this).Show(); });
            menu.Items.Add("Backup bố cục…", null, delegate { ExportLayout(); });
            menu.Items.Add("Khôi phục bố cục…", null, delegate { RestoreLayout(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Khôi phục toàn bộ icon rồi Exit", null, delegate { RestoreAllAndExit(); });
            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { new SettingsForm(this).Show(); }; tray.Visible = true;

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

        internal void ExportLayout()
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "Desktop Folders backup|*.desktopfolders", FileName = "DesktopFolders-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".desktopfolders" })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                try
                {
                    VirtualLayout backup = DataStore.LoadVirtualLayout(); File.WriteAllText(dialog.FileName, DataStore.SerializeVirtualLayout(backup));
                    MessageBox.Show("Đã backup " + backup.Groups.Count + " virtual group.", "Desktop Folders");
                }
                catch (Exception error) { MessageBox.Show("Backup thất bại: " + error.Message, "Desktop Folders"); }
            }
        }

        internal void RestoreLayout()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Desktop Folders backup|*.desktopfolders" })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                if (MessageBox.Show("Khôi phục sẽ áp dụng lại virtual groups và ẩn các icon thành viên. Tiếp tục?", "Desktop Folders", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                try
                {
                    VirtualLayout backup = DataStore.DeserializeVirtualLayout(File.ReadAllText(dialog.FileName));
                    if (backup == null || backup.Groups == null) throw new InvalidDataException("File backup không hợp lệ.");
                    foreach (VirtualGroup group in backup.Groups)
                    {
                        if (String.IsNullOrEmpty(group.TilePath) || !Path.GetExtension(group.TilePath).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) group.TilePath = Path.Combine(ExplorerDesktop.Desktop, group.Name + ".lnk");
                        foreach (VirtualMember member in group.Members) if (File.Exists(member.Path)) { File.SetAttributes(member.Path, File.GetAttributes(member.Path) | FileAttributes.Hidden); ExplorerDesktop.NotifyPathChanged(member.Path); }
                        GroupTileFactory.CreateOrUpdate(group);
                    }
                    DataStore.SaveVirtualLayout(backup); RefreshCache(); FolderPanel.NotifyLayoutChanged(); MessageBox.Show("Khôi phục virtual layout hoàn tất.", "Desktop Folders");
                }
                catch (Exception error) { MessageBox.Show("Khôi phục thất bại: " + error.Message, "Desktop Folders"); }
            }
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

        void RestoreAllAndExit()
        {
            if (MessageBox.Show("Hiện lại toàn bộ icon thành viên, xóa các tile nhóm và thoát?", "Desktop Folders", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            VirtualLayout layout = DataStore.LoadVirtualLayout();
            foreach (VirtualGroup group in layout.Groups)
            {
                foreach (VirtualMember member in group.Members) try { if (File.Exists(member.Path)) { File.SetAttributes(member.Path, (FileAttributes)member.OriginalAttributes); ExplorerDesktop.NotifyPathChanged(member.Path); } } catch { }
                try { if (File.Exists(group.TilePath)) File.Delete(group.TilePath); } catch { }
                try { if (!String.IsNullOrEmpty(group.IconPath) && File.Exists(group.IconPath)) File.Delete(group.IconPath); } catch { }
            }
            DataStore.SaveVirtualLayout(new VirtualLayout()); ExitThread();
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