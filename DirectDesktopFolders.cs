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
        // Set for a nested collection. Path is kept as the current shortcut path
        // so old layouts and shell operations remain compatible.
        public string GroupId;
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
        public int Version = 3;
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
                    if (layout != null && layout.Groups != null) { VirtualLayoutGraph.Normalize(layout); return layout; }
                }
            }
            catch { }
            return new VirtualLayout();
        }

        internal static void SaveVirtualLayout(VirtualLayout layout)
        {
            VirtualLayoutGraph.Normalize(layout);
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
            return !String.IsNullOrEmpty(path) && LoadVirtualLayout().Groups.Any(g => g.Members.Any(m => !String.IsNullOrEmpty(m.Path) && m.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));
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

    internal static class VirtualLayoutGraph
    {
        internal static void Normalize(VirtualLayout layout)
        {
            if (layout == null) return;
            if (layout.Groups == null) layout.Groups = new List<VirtualGroup>();
            layout.Version = Math.Max(3, layout.Version);
            foreach (VirtualGroup parent in layout.Groups)
            {
                if (parent.Members == null) parent.Members = new List<VirtualMember>();
                if (parent.Pinned == null) parent.Pinned = new List<string>();
                foreach (VirtualMember member in parent.Members)
                {
                    if (member == null) continue;
                    string oldPath = member.Path;
                    VirtualGroup child = null;
                    if (!String.IsNullOrEmpty(member.GroupId)) child = layout.Groups.FirstOrDefault(g => String.Equals(g.Id, member.GroupId, StringComparison.OrdinalIgnoreCase));
                    if (child == null && !String.IsNullOrEmpty(member.Path)) child = layout.Groups.FirstOrDefault(g => !String.IsNullOrEmpty(g.TilePath) && g.TilePath.Equals(member.Path, StringComparison.OrdinalIgnoreCase));
                    if (child == null) continue;
                    member.GroupId = child.Id; member.Path = child.TilePath;
                    if (!String.IsNullOrEmpty(oldPath) && !oldPath.Equals(member.Path, StringComparison.OrdinalIgnoreCase))
                        ReplacePinnedPath(parent, oldPath, member.Path);
                }
            }
        }

        internal static VirtualGroup ResolveMemberGroup(VirtualLayout layout, VirtualMember member)
        {
            if (layout == null || member == null) return null;
            if (!String.IsNullOrEmpty(member.GroupId))
            {
                VirtualGroup byId = layout.Groups.FirstOrDefault(g => String.Equals(g.Id, member.GroupId, StringComparison.OrdinalIgnoreCase));
                if (byId != null) return byId;
            }
            return String.IsNullOrEmpty(member.Path) ? null : layout.Groups.FirstOrDefault(g => !String.IsNullOrEmpty(g.TilePath) && g.TilePath.Equals(member.Path, StringComparison.OrdinalIgnoreCase));
        }

        internal static VirtualGroup ResolveTileGroup(VirtualLayout layout, string path)
        {
            return layout == null || String.IsNullOrEmpty(path) ? null : layout.Groups.FirstOrDefault(g => !String.IsNullOrEmpty(g.TilePath) && g.TilePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool ContainsGroup(VirtualLayout layout, string ancestorId, string candidateId)
        {
            return ContainsGroup(layout, ancestorId, candidateId, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        static bool ContainsGroup(VirtualLayout layout, string ancestorId, string candidateId, HashSet<string> visited)
        {
            if (String.IsNullOrEmpty(ancestorId) || String.IsNullOrEmpty(candidateId) || !visited.Add(ancestorId)) return false;
            VirtualGroup ancestor = layout.Groups.FirstOrDefault(g => String.Equals(g.Id, ancestorId, StringComparison.OrdinalIgnoreCase));
            if (ancestor == null) return false;
            foreach (VirtualMember member in ancestor.Members)
            {
                VirtualGroup child = ResolveMemberGroup(layout, member); if (child == null) continue;
                if (String.Equals(child.Id, candidateId, StringComparison.OrdinalIgnoreCase) || ContainsGroup(layout, child.Id, candidateId, visited)) return true;
            }
            return false;
        }

        internal static bool WouldCreateCycle(VirtualLayout layout, string childId, string destinationParentId)
        {
            return String.Equals(childId, destinationParentId, StringComparison.OrdinalIgnoreCase) || ContainsGroup(layout, childId, destinationParentId);
        }

        internal static List<VirtualGroup> ParentsOf(VirtualLayout layout, string childId)
        {
            VirtualGroup child = layout.Groups.FirstOrDefault(g => String.Equals(g.Id, childId, StringComparison.OrdinalIgnoreCase));
            if (child == null) return new List<VirtualGroup>();
            return layout.Groups.Where(parent => parent.Members.Any(member => String.Equals(member.GroupId, childId, StringComparison.OrdinalIgnoreCase) || (!String.IsNullOrEmpty(member.Path) && member.Path.Equals(child.TilePath, StringComparison.OrdinalIgnoreCase)))).ToList();
        }

        internal static bool IsNested(VirtualLayout layout, string groupId) { return ParentsOf(layout, groupId).Count > 0; }

        internal static void ReplaceTilePath(VirtualLayout layout, string childId, string oldPath, string newPath)
        {
            foreach (VirtualGroup parent in layout.Groups)
            {
                foreach (VirtualMember member in parent.Members)
                {
                    if (String.Equals(member.GroupId, childId, StringComparison.OrdinalIgnoreCase) || (!String.IsNullOrEmpty(oldPath) && String.Equals(member.Path, oldPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        member.GroupId = childId; member.Path = newPath; ReplacePinnedPath(parent, oldPath, newPath);
                    }
                }
            }
        }

        static void ReplacePinnedPath(VirtualGroup parent, string oldPath, string newPath)
        {
            for (int i = 0; i < parent.Pinned.Count; i++) if (String.Equals(parent.Pinned[i], oldPath, StringComparison.OrdinalIgnoreCase)) parent.Pinned[i] = newPath;
        }

        internal static void RefreshGroupAndAncestors(VirtualLayout layout, string groupId)
        {
            RefreshGroupAndAncestors(layout, groupId, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        static void RefreshGroupAndAncestors(VirtualLayout layout, string groupId, HashSet<string> visited)
        {
            if (layout == null || String.IsNullOrEmpty(groupId) || !visited.Add(groupId)) return;
            VirtualGroup group = layout.Groups.FirstOrDefault(g => String.Equals(g.Id, groupId, StringComparison.OrdinalIgnoreCase));
            if (group == null) return;
            try { GroupTileFactory.CreateOrUpdate(group); } catch { }
            foreach (VirtualGroup parent in ParentsOf(layout, groupId)) RefreshGroupAndAncestors(layout, parent.Id, visited);
        }
    }

    internal sealed class SingleInstanceCommandWindow : NativeWindow, IDisposable
    {
        internal const string WindowCaption = "DesktopFolders.Direct.Command.v6";
        internal const string ActivateCommand = "DesktopFolders.Direct.Activate";
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

        internal static bool SendCommand(string command)
        {
            if (String.IsNullOrWhiteSpace(command)) return false;
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
                text = Marshal.StringToHGlobalUni(command);
                Native.COPYDATASTRUCT data = new Native.COPYDATASTRUCT { dwData = (IntPtr)1, cbData = (command.Length + 1) * 2, lpData = text };
                packet = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.COPYDATASTRUCT)));
                Marshal.StructureToPtr(data, packet, false);
                bool accepted = Native.SendMessage(target, Native.WM_COPYDATA, IntPtr.Zero, packet) != IntPtr.Zero;
                DataStore.LogDrag("COMMAND sent=" + command + " accepted=" + accepted);
                return accepted;
            }
            catch (Exception error) { DataStore.LogDrag("COMMAND send failed=" + error.Message); return false; }
            finally { if (packet != IntPtr.Zero) Marshal.FreeHGlobal(packet); if (text != IntPtr.Zero) Marshal.FreeHGlobal(text); }
        }

        internal static bool SendOpenGroup(string groupId) { return SendCommand(groupId); }
        internal static bool SendActivate() { return SendCommand(ActivateCommand); }

        public void Dispose() { try { DestroyHandle(); } catch { } }
    }

    internal static class Native
    {
        internal const int WM_KEYDOWN = 0x0100;
        internal const int WM_KEYUP = 0x0101;
        internal const int WM_CANCELMODE = 0x001F;
        internal const int WM_MOUSEACTIVATE = 0x0021;
        internal const int WM_COPYDATA = 0x004A;
        internal const int WM_NCHITTEST = 0x0084;
        internal const int HTTRANSPARENT = -1;
        internal const int MA_ACTIVATE = 1;
        internal const int WS_EX_TRANSPARENT = 0x20;
        internal const int WS_EX_TOOLWINDOW = 0x80;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal const int CS_DROPSHADOW = 0x00020000;
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_NOOWNERZORDER = 0x0200;
        internal const byte VK_ESCAPE = 0x1B;
        internal const uint SHCNE_ASSOCCHANGED = 0x08000000;
        internal const uint SHCNE_DELETE = 0x00000004;
        internal const uint SHCNE_UPDATEITEM = 0x00002000;
        internal const uint SHCNE_ATTRIBUTES = 0x00000800;
        internal const uint SHCNF_IDLIST = 0x0000;
        internal const uint SHCNF_PATHW = 0x0005;
        internal const uint GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int left; public int top; public int right; public int bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }
        internal delegate bool EnumWindowsDelegate(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Auto)] internal static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsDelegate callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] internal static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] internal static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out RECT rectangle);
        [DllImport("user32.dll")] internal static extern bool UpdateWindow(IntPtr window);
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
        static readonly object PlacementLock = new object();
        static readonly Queue<DesktopPlacement> PlacementQueue = new Queue<DesktopPlacement>();
        static bool placementWorkerActive;
        internal static IntPtr CachedListView { get { return cachedListView; } }

        sealed class DesktopPlacement
        {
            internal string Path;
            internal Point PreferredScreenPoint;
        }

        internal static void QueuePlacement(string path, Point preferredScreenPoint)
        {
            if (String.IsNullOrEmpty(path)) return;
            lock (PlacementLock)
            {
                PlacementQueue.Enqueue(new DesktopPlacement { Path = path, PreferredScreenPoint = preferredScreenPoint });
                if (placementWorkerActive) return; placementWorkerActive = true;
            }
            Thread worker = new Thread(new ThreadStart(ProcessPlacementQueue)); worker.IsBackground = true; worker.Name = "DesktopFolders shell placement"; worker.SetApartmentState(ApartmentState.STA); worker.Start();
        }

        static void ProcessPlacementQueue()
        {
            while (true)
            {
                DesktopPlacement placement;
                lock (PlacementLock)
                {
                    if (PlacementQueue.Count == 0) { placementWorkerActive = false; return; }
                    placement = PlacementQueue.Dequeue();
                }
                bool placed = false;
                for (int attempt = 0; attempt < 18 && !placed; attempt++)
                {
                    if (!File.Exists(placement.Path)) break;
                    NotifyPathChanged(placement.Path);
                    placed = TryPlaceAtNearestFreeSlot(placement.Path, placement.PreferredScreenPoint);
                    if (!placed) Thread.Sleep(35 + attempt * 12);
                }
                DataStore.LogDrag("DESKTOP_PLACE path=" + placement.Path + " placed=" + placed);
            }
        }

        internal static bool TryPlaceAtNearestFreeSlot(string path, Point preferredScreenPoint)
        {
            IntPtr list = cachedListView == IntPtr.Zero ? GetListView() : cachedListView; if (list == IntPtr.Zero) return false; cachedListView = list;
            object shellWindows = null, desktopDispatch = null, browserObject = null, viewObject = null, folderObject = null; IntPtr browserPointer = IntPtr.Zero, viewPointer = IntPtr.Zero, targetItem = IntPtr.Zero; List<IntPtr> enumeratedItems = new List<IntPtr>();
            try
            {
                shellWindows = new ShellWindowsObject(); IShellWindowsNative windows = (IShellWindowsNative)shellWindows; object location = 0, locationRoot = Type.Missing; int desktopWindow;
                desktopDispatch = windows.FindWindowSW(ref location, ref locationRoot, 8, out desktopWindow, 1); if (desktopDispatch == null) { DataStore.LogDrag("SHELL_PLACE no desktop dispatch"); return false; }
                IServiceProviderNative provider = desktopDispatch as IServiceProviderNative; if (provider == null) { DataStore.LogDrag("SHELL_PLACE no service provider"); return false; }
                Guid browserService = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"), browserId = typeof(IShellBrowserNative).GUID;
                int serviceResult = provider.QueryService(ref browserService, ref browserId, out browserPointer); if (serviceResult < 0 || browserPointer == IntPtr.Zero) { DataStore.LogDrag("SHELL_PLACE QueryService=" + serviceResult); return false; }
                browserObject = Marshal.GetObjectForIUnknown(browserPointer); Marshal.Release(browserPointer); browserPointer = IntPtr.Zero; IShellBrowserNative browser = (IShellBrowserNative)browserObject;
                int viewResult = browser.QueryActiveShellView(out viewPointer); if (viewResult < 0 || viewPointer == IntPtr.Zero) { DataStore.LogDrag("SHELL_PLACE QueryActiveShellView=" + viewResult); return false; }
                viewObject = Marshal.GetObjectForIUnknown(viewPointer); Marshal.Release(viewPointer); viewPointer = IntPtr.Zero; IFolderViewNative view = (IFolderViewNative)viewObject;
                Guid folderId = typeof(IShellFolderNative).GUID; int folderResult = view.GetFolder(ref folderId, out folderObject); if (folderResult < 0 || folderObject == null) { DataStore.LogDrag("SHELL_PLACE GetFolder=" + folderResult); return false; } IShellFolderNative folder = (IShellFolderNative)folderObject;
                string parsingName = Path.GetFileName(path); uint eaten = 0, attributes = 0; int parseResult = folder.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, parsingName, ref eaten, out targetItem, ref attributes); if (parseResult < 0 || targetItem == IntPtr.Zero) { DataStore.LogDrag("SHELL_PLACE Parse=" + parseResult + " path=" + path); return false; }
                Native.POINT currentPosition; int positionResult = view.GetItemPosition(targetItem, out currentPosition); if (positionResult < 0) { DataStore.LogDrag("SHELL_PLACE GetItemPosition=" + positionResult); return false; }
                int count; int countResult = view.ItemCount(2, out count); if (countResult < 0 || count <= 0) { DataStore.LogDrag("SHELL_PLACE ItemCount=" + countResult + " count=" + count); return false; }

                List<Point> occupied = new List<Point>();
                for (int index = 0; index < count; index++)
                {
                    IntPtr item; if (view.Item(index, out item) < 0 || item == IntPtr.Zero) continue; enumeratedItems.Add(item);
                    int comparison = folder.CompareIDs(IntPtr.Zero, item, targetItem); if (unchecked((short)(comparison & 0xFFFF)) == 0) continue;
                    Native.POINT position; if (view.GetItemPosition(item, out position) >= 0) occupied.Add(new Point(position.x, position.y));
                }

                Native.POINT spacing = new Native.POINT(); if (view.GetSpacing(ref spacing) < 0) view.GetDefaultSpacing(out spacing); int spacingX = spacing.x, spacingY = spacing.y;
                if (spacingX < 40 || spacingX > 320) spacingX = 96; if (spacingY < 40 || spacingY > 320) spacingY = 112;
                Native.RECT client; if (!Native.GetWindowRect(list, out client)) return false; Native.POINT preferred = new Native.POINT { x = preferredScreenPoint.X - client.left, y = preferredScreenPoint.Y - client.top };
                int originX = occupied.Count == 0 ? 0 : PositiveModulo(occupied.Min(point => point.X), spacingX); int originY = occupied.Count == 0 ? 0 : PositiveModulo(occupied.Min(point => point.Y), spacingY);
                int columns = Math.Max(1, (client.right - client.left - originX) / spacingX), rows = Math.Max(1, (client.bottom - client.top - originY) / spacingY);
                int preferredColumn = Math.Max(0, Math.Min(columns - 1, (int)Math.Round((preferred.x - originX - spacingX * .5) / spacingX))); int preferredRow = Math.Max(0, Math.Min(rows - 1, (int)Math.Round((preferred.y - originY - spacingY * .5) / spacingY)));
                Point free = Point.Empty; bool found = false;
                for (int radius = 0; radius < columns + rows && !found; radius++)
                {
                    for (int dx = -radius; dx <= radius && !found; dx++)
                    {
                        int dyMagnitude = radius - Math.Abs(dx);
                        foreach (int dy in dyMagnitude == 0 ? new int[] { 0 } : new int[] { -dyMagnitude, dyMagnitude })
                        {
                            int column = preferredColumn + dx, row = preferredRow + dy; if (column < 0 || column >= columns || row < 0 || row >= rows) continue;
                            Point candidate = new Point(originX + column * spacingX, originY + row * spacingY);
                            if (occupied.Any(point => Math.Abs(point.X - candidate.X) < spacingX / 2 && Math.Abs(point.Y - candidate.Y) < spacingY / 2)) continue;
                            free = candidate; found = true; break;
                        }
                    }
                }
                if (!found) return false;
                Native.POINT requested = new Native.POINT { x = free.X, y = free.Y }; int positionedResult = view.SelectAndPositionItems(1, new IntPtr[] { targetItem }, new Native.POINT[] { requested }, 0x80); if (positionedResult < 0) { DataStore.LogDrag("SHELL_PLACE SelectAndPositionItems=" + positionedResult); return false; }
                Native.POINT actualPosition; bool positioned = view.GetItemPosition(targetItem, out actualPosition) >= 0;
                if (positioned)
                {
                    Point actual = new Point(actualPosition.x, actualPosition.y); positioned = Math.Abs(actual.X - free.X) <= 2 && Math.Abs(actual.Y - free.Y) <= 2;
                    if (!positioned) positioned = !occupied.Any(point => Math.Abs(point.X - actual.X) < spacingX / 2 && Math.Abs(point.Y - actual.Y) < spacingY / 2);
                }
                if (positioned) Native.UpdateWindow(list);
                return positioned;
            }
            catch (Exception error) { DataStore.LogDrag("SHELL_PLACE exception=" + error); return false; }
            finally
            {
                foreach (IntPtr item in enumeratedItems) if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item);
                if (targetItem != IntPtr.Zero) Marshal.FreeCoTaskMem(targetItem);
                if (viewPointer != IntPtr.Zero) Marshal.Release(viewPointer); if (browserPointer != IntPtr.Zero) Marshal.Release(browserPointer);
                foreach (object value in new object[] { folderObject, viewObject, browserObject, desktopDispatch, shellWindows }) try { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); } catch { }
            }
        }
        static int PositiveModulo(int value, int divisor) { int result = value % divisor; return result < 0 ? result + divisor : result; }

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

        internal static void NotifyPathDeleted(string path)
        {
            IntPtr item = IntPtr.Zero;
            try
            {
                if (String.IsNullOrEmpty(path)) return; item = Marshal.StringToHGlobalUni(path); Native.SHChangeNotify(Native.SHCNE_DELETE, Native.SHCNF_PATHW, item, IntPtr.Zero);
                IntPtr list = cachedListView == IntPtr.Zero ? GetListView() : cachedListView; if (list != IntPtr.Zero) Native.UpdateWindow(list);
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
        internal static void Invalidate(string path)
        {
            lock (CacheLock)
            {
                Bitmap cached; if (!Cache.TryGetValue(path ?? "", out cached)) return;
                Cache.Remove(path ?? ""); cached.Dispose();
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
            FileAttributes? preservedAttributes = null;
            try { if (File.Exists(group.TilePath)) preservedAttributes = File.GetAttributes(group.TilePath); } catch { }
            IShellLinkW link = (IShellLinkW)new ShellLinkObject();
            link.SetPath(Application.ExecutablePath);
            link.SetArguments("--open-group \"" + group.Id + "\"");
            link.SetDescription("Desktop Folders collection: " + group.Name);
            link.SetWorkingDirectory(Path.GetDirectoryName(Application.ExecutablePath));
            link.SetIconLocation(group.IconPath, 0);
            ((IPersistFile)link).Save(group.TilePath, false);
            // Rewriting a nested collection shortcut must not make it reappear on
            // the Desktop. Some shell-link implementations clear Hidden on save.
            try { if (preservedAttributes.HasValue) File.SetAttributes(group.TilePath, preservedAttributes.Value); } catch { }
            IconLoader.Invalidate(group.TilePath);
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
        int animationStarted;
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
            target = item; finalBounds = Rectangle.Inflate(item.Bounds, 5, 5); animation.Stop(); animationStarted = Environment.TickCount;
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
            double progress = Math.Min(1.0, unchecked(Environment.TickCount - animationStarted) / 120.0);
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

    [ComImport, Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
    internal class ShellWindowsObject { }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    internal interface IShellWindowsNative
    {
        [DispId(1610743816)]
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW([In] ref object location, [In] ref object locationRoot, int windowClass, out int windowHandle, int options);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IServiceProviderNative
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid interfaceId, out IntPtr result);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellBrowserNative
    {
        [PreserveSig] int GetWindow(out IntPtr window);
        [PreserveSig] int ContextSensitiveHelp(bool enterMode);
        [PreserveSig] int InsertMenusSB(IntPtr sharedMenu, IntPtr menuWidths);
        [PreserveSig] int SetMenuSB(IntPtr sharedMenu, IntPtr oleMenu, IntPtr activeWindow);
        [PreserveSig] int RemoveMenusSB(IntPtr sharedMenu);
        [PreserveSig] int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string statusText);
        [PreserveSig] int EnableModelessSB(bool enable);
        [PreserveSig] int TranslateAcceleratorSB(IntPtr message, ushort id);
        [PreserveSig] int BrowseObject(IntPtr itemIdList, uint flags);
        [PreserveSig] int GetViewStateStream(uint mode, out IntPtr stream);
        [PreserveSig] int GetControlWindow(uint id, out IntPtr window);
        [PreserveSig] int SendControlMsg(uint id, uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
        [PreserveSig] int QueryActiveShellView(out IntPtr shellView);
        [PreserveSig] int OnViewWindowActive(IntPtr shellView);
        [PreserveSig] int SetToolbarItems(IntPtr buttons, uint buttonCount, uint flags);
    }

    [ComImport, Guid("cde725b0-ccc9-4519-917e-325d72fab4ce"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFolderViewNative
    {
        [PreserveSig] int GetCurrentViewMode(out uint viewMode);
        [PreserveSig] int SetCurrentViewMode(uint viewMode);
        [PreserveSig] int GetFolder(ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object result);
        [PreserveSig] int Item(int itemIndex, out IntPtr itemIdList);
        [PreserveSig] int ItemCount(uint flags, out int count);
        [PreserveSig] int Items(uint flags, ref Guid interfaceId, out IntPtr result);
        [PreserveSig] int GetSelectionMarkedItem(out int itemIndex);
        [PreserveSig] int GetFocusedItem(out int itemIndex);
        [PreserveSig] int GetItemPosition(IntPtr itemIdList, out Native.POINT point);
        [PreserveSig] int GetSpacing(ref Native.POINT point);
        [PreserveSig] int GetDefaultSpacing(out Native.POINT point);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int itemIndex, uint flags);
        [PreserveSig] int SelectAndPositionItems(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] itemIdLists, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Native.POINT[] points, uint flags);
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
        const uint PinCommand = 1, MoveOutCommand = 2, MoveBeforeCommand = 3, MoveAfterCommand = 4, ShellFirstCommand = 0x1000, ShellLastCommand = 0x7FFF;
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

        internal static void Show(FolderPanel owner, string path, string pinText, Action pinAction, Action moveOutAction, Action moveBeforeAction, Action moveAfterAction)
        {
            if (owner == null || owner.IsDisposed || String.IsNullOrEmpty(path)) return;
            IntPtr absolute = IntPtr.Zero, menu = IntPtr.Zero; IShellFolderNative parent = null; object rawContext = null;
            try
            {
                uint attributes; if (SHParseDisplayName(path, IntPtr.Zero, out absolute, 0, out attributes) < 0 || absolute == IntPtr.Zero) throw new InvalidOperationException("Shell item unavailable.");
                Guid folderId = typeof(IShellFolderNative).GUID; IntPtr child; if (SHBindToParent(absolute, ref folderId, out parent, out child) < 0 || parent == null) throw new InvalidOperationException("Shell parent unavailable.");
                Guid contextId = typeof(IContextMenuNative).GUID; if (parent.GetUIObjectOf(owner.Handle, 1, new IntPtr[] { child }, ref contextId, IntPtr.Zero, out rawContext) < 0 || rawContext == null) throw new InvalidOperationException("Shell context menu unavailable.");
                IContextMenuNative context = (IContextMenuNative)rawContext; menu = CreatePopupMenu(); if (menu == IntPtr.Zero) throw new InvalidOperationException("Popup menu unavailable.");
                InsertMenu(menu, 0, MfByPosition | MfString, (UIntPtr)PinCommand, pinText); InsertMenu(menu, 1, MfByPosition | MfString, (UIntPtr)MoveOutCommand, "Đưa ra Desktop");
                InsertMenu(menu, 2, MfByPosition | MfSeparator, UIntPtr.Zero, null); InsertMenu(menu, 3, MfByPosition | MfString, (UIntPtr)MoveBeforeCommand, "Di chuyển về trước"); InsertMenu(menu, 4, MfByPosition | MfString, (UIntPtr)MoveAfterCommand, "Di chuyển về sau"); InsertMenu(menu, 5, MfByPosition | MfSeparator, UIntPtr.Zero, null);
                context.QueryContextMenu(menu, 6, ShellFirstCommand, ShellLastCommand, 0);
                using (ShellMenuMessageWindow messages = new ShellMenuMessageWindow(owner.Handle, rawContext))
                {
                    Point point = Cursor.Position; DataStore.LogDrag("SHELL_MENU native path=" + path); uint selected = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCommand, point.X, point.Y, owner.Handle, IntPtr.Zero); DataStore.LogDrag("SHELL_MENU selected=" + selected);
                    if (selected == PinCommand) { if (pinAction != null) pinAction(); }
                    else if (selected == MoveOutCommand) { if (moveOutAction != null) moveOutAction(); }
                    else if (selected == MoveBeforeCommand) { if (moveBeforeAction != null) moveBeforeAction(); }
                    else if (selected == MoveAfterCommand) { if (moveAfterAction != null) moveAfterAction(); }
                    else if (selected >= ShellFirstCommand && selected <= ShellLastCommand)
                    {
                        IntPtr verb = (IntPtr)(selected - ShellFirstCommand); CommandInfo command = new CommandInfo { cbSize = Marshal.SizeOf(typeof(CommandInfo)), fMask = CmicMaskUnicode | CmicMaskPointInvoke, hwnd = owner.Handle, lpVerb = verb, lpVerbW = verb, nShow = 1, ptInvoke = new Native.POINT { x = point.X, y = point.Y } };
                        context.InvokeCommand(ref command);
                    }
                }
            }
            catch (Exception error) { DataStore.LogDrag("SHELL_MENU fallback=" + error); ShowFallback(owner, path, pinText, pinAction, moveOutAction, moveBeforeAction, moveAfterAction); }
            finally
            {
                if (menu != IntPtr.Zero) DestroyMenu(menu); if (rawContext != null && Marshal.IsComObject(rawContext)) Marshal.FinalReleaseComObject(rawContext); if (parent != null && Marshal.IsComObject(parent)) Marshal.FinalReleaseComObject(parent); if (absolute != IntPtr.Zero) Marshal.FreeCoTaskMem(absolute);
            }
        }
        static void ShowFallback(FolderPanel owner, string path, string pinText, Action pinAction, Action moveOutAction, Action moveBeforeAction, Action moveAfterAction)
        {
            ContextMenuStrip fallback = new ContextMenuStrip(); fallback.Items.Add(pinText, null, delegate { if (pinAction != null) pinAction(); }); fallback.Items.Add("Đưa ra Desktop", null, delegate { if (moveOutAction != null) moveOutAction(); }); fallback.Items.Add(new ToolStripSeparator()); fallback.Items.Add("Di chuyển về trước", null, delegate { if (moveBeforeAction != null) moveBeforeAction(); }); fallback.Items.Add("Di chuyển về sau", null, delegate { if (moveAfterAction != null) moveAfterAction(); }); fallback.Items.Add(new ToolStripSeparator()); fallback.Items.Add("Mở", null, delegate { try { Process.Start(path); } catch { } }); fallback.Closed += delegate { fallback.Dispose(); }; fallback.Show(owner, owner.PointToClient(Cursor.Position));
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
        internal event EventHandler ItemActivated;
        readonly Image icon;
        readonly string label;
        readonly bool pinned;
        readonly bool gridMode;
        readonly bool reduceMotion;
        System.Windows.Forms.Timer hoverAnimation;
        float hoverProgress;
        int hoverLastFrame;
        bool hot;
        bool dragOver;
        bool dragging;
        bool nestingTarget;
        ContextMenuStrip immediateContextMenu;
        internal string ItemPath { get; private set; }

        internal AppTile(string path, bool isPinned, bool grid, int width, bool reducedMotion)
        {
            ItemPath = path; label = Path.GetFileNameWithoutExtension(path); pinned = isPinned; gridMode = grid; icon = IconLoader.ForPath(path);
            reduceMotion = reducedMotion;
            Size = grid ? new Size(width, width < 170 ? 106 : 134) : new Size(width, 64); BackColor = Color.Transparent; Cursor = Cursors.Hand;
            TabStop = true; AccessibleRole = AccessibleRole.ListItem; AccessibleName = label; AccessibleDescription = pinned ? "Đã ghim ưu tiên" : "";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Focus();
            else if (e.Button == MouseButtons.Right && immediateContextMenu != null) { Focus(); immediateContextMenu.Show(this, e.Location); }
            base.OnMouseDown(e);
        }
        internal void SetImmediateContextMenu(ContextMenuStrip menu) { immediateContextMenu = menu; if (immediateContextMenu != null) immediateContextMenu.CreateControl(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            // Control.Click also fires for a completed right-click on WinForms.
            // Activation is deliberately restricted to primary-button clicks.
            if (e.Button == MouseButtons.Left) RaiseItemActivated();
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; BeginHoverTransition(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; BeginHoverTransition(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { BeginHoverTransition(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { BeginHoverTransition(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { RaiseItemActivated(); e.Handled = true; e.SuppressKeyPress = true; } base.OnKeyDown(e); }
        void RaiseItemActivated() { EventHandler activated = ItemActivated; if (activated != null) activated(this, EventArgs.Empty); }
        internal void SetDragVisual(bool isDragging, bool isDropTarget, bool isNestingTarget = false) { dragging = isDragging; dragOver = isDropTarget; nestingTarget = isNestingTarget; BeginHoverTransition(); }
        void BeginHoverTransition()
        {
            float target = hot || dragOver || Focused ? 1f : 0f;
            if (reduceMotion) { hoverProgress = target; Invalidate(); return; }
            if (hoverAnimation == null) { hoverAnimation = new System.Windows.Forms.Timer { Interval = 15 }; hoverAnimation.Tick += delegate { AnimateHover(); }; }
            hoverAnimation.Stop(); hoverLastFrame = Environment.TickCount; hoverAnimation.Start(); Invalidate();
        }
        void AnimateHover()
        {
            int now = Environment.TickCount; float seconds = Math.Max(1, Math.Min(45, unchecked(now - hoverLastFrame))) / 1000f; hoverLastFrame = now;
            float target = hot || dragOver || Focused ? 1f : 0f; float response = 1f - (float)Math.Exp(-seconds / .045f); hoverProgress += (target - hoverProgress) * response;
            if (Math.Abs(target - hoverProgress) < .018f) { hoverProgress = target; hoverAnimation.Stop(); }
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float interaction = Math.Max(0f, Math.Min(1f, hoverProgress)); int inset = 3 - (int)Math.Round(interaction * 2f);
            int maximumCardHeight = Height - 1;
            Rectangle card = new Rectangle(inset, inset, Math.Max(8, Width - inset * 2 - 1), Math.Max(8, maximumCardHeight - (inset - 1) * 2));
            if (dragging)
            {
                using (Brush placeholder = new SolidBrush(Color.FromArgb(22, CollectionTheme.Focus))) e.Graphics.FillRoundedRectangle(placeholder, card, CollectionTheme.RadiusCard);
                using (Pen placeholderEdge = new Pen(Color.FromArgb(105, CollectionTheme.Focus), 1.5f)) { placeholderEdge.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash; e.Graphics.DrawRoundedRectangle(placeholderEdge, Rectangle.Inflate(card, -1, -1), CollectionTheme.RadiusCard - 1); }
                return;
            }
            int glassAlpha = (int)(60 + 42 * interaction); Color glassBlue = Color.FromArgb(glassAlpha, 19, 54, 125), glassPurple = Color.FromArgb(Math.Max(40, glassAlpha - 10), 91, 24, 137);
            using (System.Drawing.Drawing2D.LinearGradientBrush fill = new System.Drawing.Drawing2D.LinearGradientBrush(card, glassBlue, glassPurple, 0f)) e.Graphics.FillRoundedRectangle(fill, card, CollectionTheme.RadiusCard);
            Color edgeBlue = Color.FromArgb((int)(74 + 120 * interaction), CollectionTheme.FocusBlue), edgePurple = Color.FromArgb((int)(74 + 120 * interaction), CollectionTheme.Focus);
            using (System.Drawing.Drawing2D.LinearGradientBrush edgeGradient = new System.Drawing.Drawing2D.LinearGradientBrush(card, edgeBlue, edgePurple, 0f))
            using (Pen edge = new Pen(edgeGradient, dragOver ? 2f : 1f + .35f * interaction)) e.Graphics.DrawRoundedRectangle(edge, card, CollectionTheme.RadiusCard);
            using (Pen reflection = new Pen(Color.FromArgb((int)(30 + 24 * interaction), 236, 223, 255), 1f)) e.Graphics.DrawLine(reflection, card.Left + 13, card.Top + 1, card.Right - 13, card.Top + 1);
            if (nestingTarget)
            {
                using (Brush nestFill = new SolidBrush(Color.FromArgb(42, CollectionTheme.Focus))) e.Graphics.FillRoundedRectangle(nestFill, Rectangle.Inflate(card, -5, -5), CollectionTheme.RadiusCard - 4);
                using (Pen nestEdge = new Pen(CollectionTheme.Focus, 2.5f)) e.Graphics.DrawRoundedRectangle(nestEdge, Rectangle.Inflate(card, -4, -4), CollectionTheme.RadiusCard - 3);
            }
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
                int iconSize = 42 + (int)Math.Round(3 * interaction), iconX = (Width - iconSize) / 2, iconY = card.Y + 10;
                if (icon != null) e.Graphics.DrawImage(icon, new Rectangle(iconX, iconY, iconSize, iconSize));
                using (Font name = new Font("Segoe UI", 9f, FontStyle.Regular))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
                using (Brush text = new SolidBrush(Mix(CollectionTheme.SecondaryText, CollectionTheme.Text, interaction))) e.Graphics.DrawString(label, name, text, new Rectangle(card.Left + 4, card.Bottom - 27, card.Width - 8, 22), format);
            }
            else if (gridMode)
            {
                int iconSize = 56 + (int)Math.Round(4 * interaction), iconX = (Width - iconSize) / 2, iconY = card.Y + 14;
                if (icon != null) e.Graphics.DrawImage(icon, new Rectangle(iconX, iconY, iconSize, iconSize));
                using (Font name = new Font("Segoe UI", 10f, FontStyle.Regular))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
                using (Brush text = new SolidBrush(Mix(CollectionTheme.SecondaryText, CollectionTheme.Text, interaction))) e.Graphics.DrawString(label, name, text, new Rectangle(card.Left + 6, card.Bottom - 29, card.Width - 12, 23), format);
            }
            else
            {
                int iconSize = 42 + (int)Math.Round(3 * interaction), iconY = (Height - iconSize) / 2;
                if (icon != null) e.Graphics.DrawImage(icon, new Rectangle(12, iconY, iconSize, iconSize));
                using (Font name = new Font("Segoe UI", 10f, FontStyle.Bold))
                using (StringFormat format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
                using (Brush text = new SolidBrush(CollectionTheme.Text)) e.Graphics.DrawString(label, name, text, new Rectangle(68, 6, Math.Max(40, Width - 84), 50), format);
            }
        }
        static Color Mix(Color background, Color foreground, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb((int)(background.R + (foreground.R - background.R) * amount), (int)(background.G + (foreground.G - background.G) * amount), (int)(background.B + (foreground.B - background.B) * amount));
        }
        static System.Drawing.Drawing2D.GraphicsPath StarPath(PointF center, float outer, float inner)
        {
            System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath(); PointF[] points = new PointF[10];
            for (int i = 0; i < 10; i++) { double angle = -Math.PI / 2 + i * Math.PI / 5; float radius = (i % 2 == 0) ? outer : inner; points[i] = new PointF(center.X + (float)Math.Cos(angle) * radius, center.Y + (float)Math.Sin(angle) * radius); }
            path.AddPolygon(points); return path;
        }
        protected override void Dispose(bool disposing) { if (disposing) { if (hoverAnimation != null) hoverAnimation.Dispose(); if (immediateContextMenu != null) immediateContextMenu.Dispose(); if (ContextMenuStrip != null) ContextMenuStrip.Dispose(); if (icon != null) icon.Dispose(); } base.Dispose(disposing); }
    }

    internal sealed class DragTileGhost : Form
    {
        readonly Bitmap snapshot;
        readonly Point grabOffset;
        readonly System.Windows.Forms.Timer followTimer;

        internal DragTileGhost(AppTile tile, Point offset)
        {
            grabOffset = new Point(Math.Max(0, Math.Min(tile.Width - 1, offset.X)), Math.Max(0, Math.Min(tile.Height - 1, offset.Y)));
            snapshot = new Bitmap(Math.Max(1, tile.Width), Math.Max(1, tile.Height)); tile.DrawToBitmap(snapshot, tile.ClientRectangle);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; BackColor = Color.Magenta; TransparencyKey = Color.Magenta; ClientSize = snapshot.Size; DoubleBuffered = true; Opacity = .97;
            followTimer = new System.Windows.Forms.Timer { Interval = 8 }; followTimer.Tick += delegate { FollowPointer(); };
            Shown += delegate { FollowPointer(); followTimer.Start(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ClassStyle |= Native.CS_DROPSHADOW; p.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT; return p; } }
        protected override void WndProc(ref Message message) { if (message.Msg == Native.WM_NCHITTEST) { message.Result = (IntPtr)Native.HTTRANSPARENT; return; } base.WndProc(ref message); }
        internal void FollowPointer()
        {
            Point pointer = Cursor.Position; Point requested = new Point(pointer.X - grabOffset.X, pointer.Y - grabOffset.Y);
            if (Location != requested && IsHandleCreated) Native.SetWindowPos(Handle, IntPtr.Zero, requested.X, requested.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Magenta); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImageUnscaled(snapshot, Point.Empty);
        }
        protected override void Dispose(bool disposing) { if (disposing) { followTimer.Dispose(); snapshot.Dispose(); } base.Dispose(disposing); }
    }

    internal static class CollectionTheme
    {
        internal static readonly Color Background = Color.FromArgb(5, 3, 19);
        internal static readonly Color Surface = Color.FromArgb(25, 16, 53);
        internal static readonly Color SurfaceHover = Color.FromArgb(49, 27, 91);
        internal static readonly Color Control = Color.FromArgb(13, 9, 38);
        internal static readonly Color ControlActive = Color.FromArgb(57, 22, 104);
        internal static readonly Color Border = Color.FromArgb(64, 43, 116);
        internal static readonly Color Stroke = Color.FromArgb(91, 75, 138);
        internal static readonly Color NeonBlue = Color.FromArgb(55, 196, 255);
        internal static readonly Color NeonIndigo = Color.FromArgb(88, 96, 255);
        internal static readonly Color NeonPurple = Color.FromArgb(220, 72, 255);
        internal static readonly Color Focus = NeonPurple;
        internal static readonly Color FocusBlue = NeonBlue;
        internal static readonly Color Text = Color.FromArgb(248, 244, 255);
        internal static readonly Color SecondaryText = Color.FromArgb(221, 205, 255);
        internal static readonly Color MutedText = Color.FromArgb(167, 139, 250);
        internal static readonly Color Title = Color.FromArgb(196, 125, 255);
        internal static readonly Color Pin = Color.FromArgb(255, 215, 106);
        internal static readonly Color Danger = Color.FromArgb(255, 84, 84);
        internal const int RadiusWindow = 22;
        internal const int RadiusControl = 12;
        internal const int RadiusCard = 16;

        internal static System.Drawing.Drawing2D.ColorBlend NeonBlend(int alpha)
        {
            int safeAlpha = Math.Max(0, Math.Min(255, alpha));
            return new System.Drawing.Drawing2D.ColorBlend(3) {
                Colors = new Color[] { Color.FromArgb(safeAlpha, NeonBlue), Color.FromArgb(safeAlpha, NeonIndigo), Color.FromArgb(safeAlpha, NeonPurple) },
                Positions = new float[] { 0f, .52f, 1f }
            };
        }
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

    internal sealed class GradientLabel : Label
    {
        internal GradientLabel() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Rectangle textArea = new Rectangle(0, 0, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            int gradientWidth = Math.Max(8, Math.Min(textArea.Width, (int)Math.Ceiling(e.Graphics.MeasureString(Text, Font).Width)));
            Rectangle gradientArea = new Rectangle(0, 0, gradientWidth, textArea.Height);
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                using (System.Drawing.Drawing2D.LinearGradientBrush glow = new System.Drawing.Drawing2D.LinearGradientBrush(gradientArea, CollectionTheme.NeonBlue, CollectionTheme.NeonPurple, 0f))
                {
                    glow.InterpolationColors = CollectionTheme.NeonBlend(42);
                    foreach (Point offset in new Point[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
                    {
                        Rectangle glowArea = textArea; glowArea.Offset(offset); e.Graphics.DrawString(Text, Font, glow, glowArea, format);
                    }
                }
                using (System.Drawing.Drawing2D.LinearGradientBrush gradient = new System.Drawing.Drawing2D.LinearGradientBrush(gradientArea, CollectionTheme.NeonBlue, CollectionTheme.NeonPurple, 0f))
                {
                    gradient.InterpolationColors = CollectionTheme.NeonBlend(255); gradient.GammaCorrection = true; e.Graphics.DrawString(Text, Font, gradient, textArea, format);
                }
            }
        }
    }

    internal sealed class HeaderIconButton : Button
    {
        HeaderIconKind kind;
        bool hot;
        bool selected;
        internal HeaderIconKind IconKind { get { return kind; } set { kind = value; Invalidate(); } }
        internal bool Selected { get { return selected; } set { selected = value; AccessibleDescription = value ? "Đang chọn" : ""; Invalidate(); } }
        internal HeaderIconButton(HeaderIconKind iconKind, int width)
        {
            kind = iconKind; Width = width; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = CollectionTheme.Control; ForeColor = CollectionTheme.SecondaryText; Cursor = Cursors.Hand; TabStop = true; Text = ""; AccessibleRole = AccessibleRole.PushButton; AccessibleName = iconKind.ToString();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            Rectangle box = new Rectangle(4, 5, Math.Max(10, Width - 8), Math.Max(10, Height - 10));
            Color background = hot || Focused ? (kind == HeaderIconKind.Close ? Color.FromArgb(58, CollectionTheme.Danger) : Color.FromArgb(126, CollectionTheme.ControlActive)) : (selected ? Color.FromArgb(118, 80, 24, 146) : Color.Transparent);
            Color glyph = selected ? CollectionTheme.Text : (hot ? CollectionTheme.Text : ForeColor);
            if (selected && kind != HeaderIconKind.Close)
            {
                using (System.Drawing.Drawing2D.LinearGradientBrush neon = new System.Drawing.Drawing2D.LinearGradientBrush(box, Color.FromArgb(190, CollectionTheme.FocusBlue), Color.FromArgb(190, CollectionTheme.Focus), 0f)) e.Graphics.FillRoundedRectangle(neon, box, 8);
            }
            else if (background.A > 0) using (Brush fill = new SolidBrush(background)) e.Graphics.FillRoundedRectangle(fill, box, 8);
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
        internal Point To;
        internal PointF Current;
        internal PointF Velocity;
    }

    internal sealed class BufferedPanel : Panel
    {
        internal BufferedPanel() { DoubleBuffered = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true); }
    }

    internal sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
    {
        internal BufferedFlowLayoutPanel() { DoubleBuffered = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true); }
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
        System.Windows.Forms.Timer nestHoverTimer;
        List<TileMotion> reorderMotions;
        readonly Dictionary<string, PointF> reorderVelocities = new Dictionary<string, PointF>(StringComparer.OrdinalIgnoreCase);
        DragTileGhost activeDragGhost;
        int reorderLastFrame;
        bool reorderLayoutSuspended;
        bool gridDragActive;
        bool gridDragCommitted;
        string gridDragSourcePath;
        string gridPreviewTargetPath;
        bool gridPreviewAfter;
        bool gridHasSwapped;
        Point gridLastSwapPointer;
        string nestHoverTargetPath;
        int nestHoverStarted;
        bool nestDropArmed;
        bool ReduceMotionNow { get { return settings.ReduceMotion || SystemInformation.TerminalServerSession; } }

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
                OpenPanels[requested.Id] = new WeakReference(panel); panel.Show(); panel.Promote();
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
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true; BackColor = CollectionTheme.Background; Padding = new Padding(1); Opacity = 1.0; AllowDrop = true; KeyPreview = true; DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            nestHoverTimer = new System.Windows.Forms.Timer { Interval = settings.FolderHoverDelay }; nestHoverTimer.Tick += delegate { ArmNestHover(); };
#if TRACE_DRAG
            ShowInTaskbar = true;
#endif
            Screen sourceScreen = Screen.FromRectangle(source);
            if (sourceScreen == null && source.Width > 0 && source.Height > 0) sourceScreen = Screen.FromPoint(new Point(source.Left + source.Width / 2, source.Top + source.Height / 2));
            if (sourceScreen == null) sourceScreen = Screen.PrimaryScreen;
            Rectangle work = sourceScreen != null ? sourceScreen.WorkingArea : new Rectangle(0, 0, 1920, 1080);
            compactSize = new Size(Math.Min(compactSize.Width, Math.Max(360, work.Width - 30)), Math.Min(compactSize.Height, Math.Max(420, work.Height - 30)));
            expandedSize = new Size(Math.Min(expandedSize.Width, Math.Max(compactSize.Width, work.Width - 30)), Math.Min(expandedSize.Height, Math.Max(compactSize.Height, work.Height - 30)));
            finalSize = compactSize;
            Point desired = CalculateAnchoredLocation(source, finalSize, work);
            finalLocation = FindOpenLocation(desired, finalSize, work);
            Size = finalSize; Location = finalLocation;
            DataStore.LogDrag("PANEL anchor=" + source + " bounds=" + Bounds);

            GradientPanel shell = new GradientPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 9, 10), CornerRadius = CollectionTheme.RadiusWindow };
            Controls.Add(shell);
            Panel header = new BufferedPanel { Dock = DockStyle.Top, Height = 58, BackColor = Color.Transparent };
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

            Panel content = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            shell.Controls.Add(content); content.BringToFront();
            titleLabel = new GradientLabel { Text = group.Name, ForeColor = CollectionTheme.Title, BackColor = Color.Transparent, Font = new Font("Segoe UI", 18, FontStyle.Bold), Location = new Point(0, 0), Width = 190, Height = 54, Cursor = Cursors.IBeam, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false };
            titleEditor = new TextBox { Text = group.Name, Visible = false, BorderStyle = BorderStyle.None, BackColor = CollectionTheme.Control, ForeColor = CollectionTheme.Title, Font = new Font("Segoe UI", 18, FontStyle.Bold), Location = new Point(0, 12), Width = 190 };
            titleLabel.Click += delegate { BeginTitleEdit(); };
            titleEditor.Leave += delegate { CommitTitleEdit(); };
            titleEditor.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { CommitTitleEdit(); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Escape) { EndTitleEdit(false); e.SuppressKeyPress = true; } };
            header.Controls.Add(titleLabel); header.Controls.Add(titleEditor); titleLabel.SendToBack(); titleEditor.SendToBack();
            header.Resize += delegate { int titleWidth = Math.Max(70, header.ClientSize.Width - 132); titleLabel.Width = titleWidth; titleEditor.Width = titleWidth; };

            RoundedPanel searchContainer = new RoundedPanel { Location = new Point(0, 0), Height = 40, Radius = CollectionTheme.RadiusControl, BackColor = CollectionTheme.Control, BorderColor = CollectionTheme.NeonBlue, BorderEndColor = CollectionTheme.NeonPurple, BorderThickness = 2, ShowSearchGlyph = true, GlyphColor = CollectionTheme.NeonPurple, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            search = new TextBox { BorderStyle = BorderStyle.None, BackColor = CollectionTheme.Control, ForeColor = CollectionTheme.Text, Font = new Font("Segoe UI", 10.5f), Location = new Point(46, 10), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Height = 22, AccessibleName = "Tìm trong collection" };
            searchHint = new Label { Text = "Tìm trong collection…", ForeColor = CollectionTheme.MutedText, BackColor = CollectionTheme.Control, Font = new Font("Segoe UI", 10f), AutoSize = true, Location = new Point(48, 11), Cursor = Cursors.IBeam };
            searchHint.Click += delegate { search.Focus(); };
            search.Enter += delegate { searchContainer.BorderColor = CollectionTheme.NeonBlue; searchContainer.BorderEndColor = CollectionTheme.NeonPurple; searchContainer.BorderThickness = 2; searchContainer.GlyphColor = CollectionTheme.NeonBlue; searchContainer.Invalidate(); };
            search.Leave += delegate { searchContainer.BorderColor = CollectionTheme.NeonBlue; searchContainer.BorderEndColor = CollectionTheme.NeonPurple; searchContainer.BorderThickness = 2; searchContainer.GlyphColor = CollectionTheme.NeonPurple; searchContainer.Invalidate(); };
            searchContainer.Controls.Add(search); searchContainer.Controls.Add(searchHint); searchHint.BringToFront();
            content.Controls.Add(searchContainer);

            appsViewport = new BufferedPanel { Location = new Point(0, 54), BackColor = Color.Transparent, AllowDrop = true, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            apps = new BufferedFlowLayoutPanel { Location = Point.Empty, AutoScroll = false, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, BackColor = Color.Transparent, Padding = new Padding(5, 0, 0, 0), AllowDrop = true };
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
            PerformLayout(); shell.PerformLayout(); content.PerformLayout(); layoutContent(); UpdateModeButtons(); RenderApps();
            Shown += delegate { DataStore.LogDrag("PANEL shown=" + group.Name); Opacity = 1.0; };
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
            if (IsDisposed) return; CancelActiveMorph(); Opacity = 1.0; if (!Visible) Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; TopMost = true; BringToFront(); Native.SetForegroundWindow(Handle); Activate();
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
            Promote(); if (!ReduceMotionNow) StartCompositionCue(previous, Bounds);
        }

        Bitmap CaptureSnapshot()
        {
            Bitmap image = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            DrawToBitmap(image, ClientRectangle); return image;
        }
        void StartCompositionCue(Rectangle from, Rectangle to)
        {
            if (IsDisposed || ReduceMotionNow || from == to) return;
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
            if (ReduceMotionNow) { Close(); return; }
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
            Screen sourceScreen = Screen.FromRectangle(source);
            Rectangle work = sourceScreen != null ? sourceScreen.WorkingArea : Screen.PrimaryScreen.WorkingArea;
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
            Screen screen = Screen.FromPoint(desired);
            if (screen == null) screen = Screen.FromRectangle(new Rectangle(desired, size));
            Rectangle targetWork = screen != null ? screen.WorkingArea : (!work.IsEmpty ? work : Screen.PrimaryScreen.WorkingArea);
            List<Point> candidates = new List<Point> { desired, new Point(desired.X + 30, desired.Y + 30), new Point(desired.X - 30, desired.Y + 30), new Point(desired.X + 60, desired.Y + 54), new Point(desired.X - 60, desired.Y + 54), new Point(desired.X + 54, desired.Y - 42), new Point(desired.X - 54, desired.Y - 42) };
            Point best = desired; long bestScore = Int64.MaxValue;
            foreach (Point raw in candidates)
            {
                Point candidate = new Point(Math.Max(targetWork.Left + 15, Math.Min(raw.X, targetWork.Right - size.Width - 15)), Math.Max(targetWork.Top + 15, Math.Min(raw.Y, targetWork.Bottom - size.Height - 15)));
                Rectangle bounds = new Rectangle(candidate, size); long overlap = 0;
                foreach (Rectangle other in occupied) { Rectangle intersection = Rectangle.Intersect(bounds, other); if (intersection.Width > 0 && intersection.Height > 0) overlap += (long)intersection.Width * intersection.Height; }
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
                Screen sourceScreen = Screen.FromRectangle(updated);
                Rectangle work = sourceScreen != null ? sourceScreen.WorkingArea : Screen.PrimaryScreen.WorkingArea;
                Point location = FindOpenLocation(CalculateAnchoredLocation(updated, finalSize, work), finalSize, work, this);
                if (Math.Abs(location.X - Left) <= 2 && Math.Abs(location.Y - Top) <= 2) return;
                Bounds = new Rectangle(location, finalSize); finalLocation = location;
            }));
        }
        static Point CalculateAnchoredLocation(Rectangle anchor, Size size, Rectangle work)
        {
            const int edge = 12, gap = 10;
            Screen anchorScreen = Screen.FromRectangle(anchor);
            if (anchorScreen == null && anchor.Width > 0 && anchor.Height > 0) anchorScreen = Screen.FromPoint(new Point(anchor.Left + anchor.Width / 2, anchor.Top + anchor.Height / 2));
            if (anchorScreen == null && !work.IsEmpty) anchorScreen = Screen.FromRectangle(work);
            if (anchorScreen == null) anchorScreen = Screen.PrimaryScreen;
            Rectangle anchorWork = anchorScreen != null ? anchorScreen.WorkingArea : (!work.IsEmpty ? work : new Rectangle(0, 0, 1920, 1080));
            Screen[] screens = Screen.AllScreens;

            if (screens == null || screens.Length <= 1)
            {
                int x = Math.Max(anchorWork.Left + edge, Math.Min(anchor.Left, anchorWork.Right - size.Width - edge));
                int below = anchor.Bottom + gap, above = anchor.Top - size.Height - gap;
                if (below + size.Height <= anchorWork.Bottom - edge) return new Point(x, below);
                if (above >= anchorWork.Top + edge) return new Point(x, above);
                int centeredY = Math.Max(anchorWork.Top + edge, Math.Min(anchor.Top + (anchor.Height - size.Height) / 2, anchorWork.Bottom - size.Height - edge));
                int right = anchor.Right + gap, left = anchor.Left - size.Width - gap;
                if (right + size.Width <= anchorWork.Right - edge) return new Point(right, centeredY);
                if (left >= anchorWork.Left + edge) return new Point(left, centeredY);
                return new Point(x, centeredY);
            }

            int ax = Math.Max(anchorWork.Left + edge, Math.Min(anchor.Left, anchorWork.Right - size.Width - edge));
            int aBelow = anchor.Bottom + gap;
            if (aBelow + size.Height <= anchorWork.Bottom - edge && aBelow >= anchorWork.Top + edge) return new Point(ax, aBelow);
            int aAbove = anchor.Top - size.Height - gap;
            if (aAbove >= anchorWork.Top + edge && aAbove + size.Height <= anchorWork.Bottom - edge) return new Point(ax, aAbove);
            int aCenteredY = Math.Max(anchorWork.Top + edge, Math.Min(anchor.Top + (anchor.Height - size.Height) / 2, anchorWork.Bottom - size.Height - edge));
            int aRight = anchor.Right + gap;
            if (aRight + size.Width <= anchorWork.Right - edge && aRight >= anchorWork.Left + edge) return new Point(aRight, aCenteredY);
            int aLeft = anchor.Left - size.Width - gap;
            if (aLeft >= anchorWork.Left + edge && aLeft + size.Width <= anchorWork.Right - edge) return new Point(aLeft, aCenteredY);

            List<Rectangle> candidates = new List<Rectangle>();
            Point anchorCenter = new Point(anchor.Left + anchor.Width / 2, anchor.Top + anchor.Height / 2);
            List<Screen> otherScreens = screens.Where(s => anchorScreen == null || !s.DeviceName.Equals(anchorScreen.DeviceName, StringComparison.OrdinalIgnoreCase))
                                               .OrderBy(s => {
                                                   int dx = Math.Max(0, Math.Max(s.WorkingArea.Left - anchorCenter.X, anchorCenter.X - s.WorkingArea.Right));
                                                   int dy = Math.Max(0, Math.Max(s.WorkingArea.Top - anchorCenter.Y, anchorCenter.Y - s.WorkingArea.Bottom));
                                                   return dx * dx + dy * dy;
                                               }).ToList();

            foreach (Screen s in otherScreens)
            {
                Rectangle sw = s.WorkingArea;
                int sRightX = anchor.Right + gap;
                int sRightY = Math.Max(sw.Top + edge, Math.Min(anchor.Top + (anchor.Height - size.Height) / 2, sw.Bottom - size.Height - edge));
                Rectangle sRight = new Rectangle(sRightX, sRightY, size.Width, size.Height);
                if (sRight.Left >= sw.Left + edge && sRight.Right <= sw.Right - edge && sRight.Top >= sw.Top + edge && sRight.Bottom <= sw.Bottom - edge) return sRight.Location;
                candidates.Add(sRight);

                int sLeftX = anchor.Left - size.Width - gap;
                int sLeftY = Math.Max(sw.Top + edge, Math.Min(anchor.Top + (anchor.Height - size.Height) / 2, sw.Bottom - size.Height - edge));
                Rectangle sLeft = new Rectangle(sLeftX, sLeftY, size.Width, size.Height);
                if (sLeft.Left >= sw.Left + edge && sLeft.Right <= sw.Right - edge && sLeft.Top >= sw.Top + edge && sLeft.Bottom <= sw.Bottom - edge) return sLeft.Location;
                candidates.Add(sLeft);

                int sBelowX = Math.Max(sw.Left + edge, Math.Min(anchor.Left, sw.Right - size.Width - edge));
                int sBelowY = anchor.Bottom + gap;
                Rectangle sBelow = new Rectangle(sBelowX, sBelowY, size.Width, size.Height);
                if (sBelow.Left >= sw.Left + edge && sBelow.Right <= sw.Right - edge && sBelow.Top >= sw.Top + edge && sBelow.Bottom <= sw.Bottom - edge) return sBelow.Location;
                candidates.Add(sBelow);

                int sAboveX = Math.Max(sw.Left + edge, Math.Min(anchor.Left, sw.Right - size.Width - edge));
                int sAboveY = anchor.Top - size.Height - gap;
                Rectangle sAbove = new Rectangle(sAboveX, sAboveY, size.Width, size.Height);
                if (sAbove.Left >= sw.Left + edge && sAbove.Right <= sw.Right - edge && sAbove.Top >= sw.Top + edge && sAbove.Bottom <= sw.Bottom - edge) return sAbove.Location;
                candidates.Add(sAbove);
            }

            candidates.Add(new Rectangle(ax, aBelow, size.Width, size.Height));
            candidates.Add(new Rectangle(ax, aAbove, size.Width, size.Height));
            candidates.Add(new Rectangle(aRight, aCenteredY, size.Width, size.Height));
            candidates.Add(new Rectangle(aLeft, aCenteredY, size.Width, size.Height));
            candidates.Add(new Rectangle(ax, aCenteredY, size.Width, size.Height));

            Rectangle bestCandidate = candidates[0];
            long minScore = Int64.MaxValue;
            long totalArea = (long)size.Width * size.Height;
            foreach (Rectangle cand in candidates)
            {
                long visibleArea = 0;
                foreach (Screen s in screens)
                {
                    Rectangle inter = Rectangle.Intersect(cand, s.WorkingArea);
                    if (inter.Width > 0 && inter.Height > 0) visibleArea += (long)inter.Width * inter.Height;
                }
                long clippedArea = Math.Max(0, totalArea - visibleArea);
                long dist = Math.Abs(cand.Left - anchor.Left) + Math.Abs(cand.Top - anchor.Top);
                long score = clippedArea * 1000L + dist;
                if (score < minScore) { minScore = score; bestCandidate = cand; }
            }
            return bestCandidate.Location;
        }
        void BeginTitleEdit() { titleEditor.Text = group.Name; titleLabel.Visible = false; titleEditor.Visible = true; titleEditor.Focus(); titleEditor.SelectAll(); }
        void EndTitleEdit(bool keepText) { if (!keepText) titleEditor.Text = group.Name; titleEditor.Visible = false; titleLabel.Visible = true; }
        void CommitTitleEdit() { if (!titleEditor.Visible) return; RenameGroup(titleEditor.Text); EndTitleEdit(true); }
        void ClearTextFocus() { if (titleEditor.Visible) CommitTitleEdit(); ActiveControl = null; if (IsHandleCreated) Native.SetFocus(Handle); }

        void RenderApps()
        {
            if (apps == null || group == null) return;
            CancelReorderAnimation(); themedScroll.SetValue(0); apps.SuspendLayout();
            foreach (Control obsolete in apps.Controls.Cast<Control>().ToArray()) { apps.Controls.Remove(obsolete); obsolete.Dispose(); }
            apps.Top = 0; apps.Height = 10000; string query = search == null ? "" : search.Text.Trim().ToLowerInvariant();
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
                string path = member.Path; bool pinned = group.Pinned.Contains(path, StringComparer.OrdinalIgnoreCase); VirtualGroup nestedGroup = VirtualLayoutGraph.ResolveMemberGroup(layout, member);
                if (!activePinnedSection.HasValue || activePinnedSection.Value != pinned)
                {
                    if (apps.Controls.Count > 0) apps.SetFlowBreak(apps.Controls[apps.Controls.Count - 1], true);
                    SectionHeaderControl section = new SectionHeaderControl(pinned ? "Đã ghim" : "Tất cả ứng dụng", pinned ? pinnedCount : regularCount, Math.Max(100, usableWidth - apps.Padding.Left - 6));
                    apps.Controls.Add(section); apps.SetFlowBreak(section, true); activePinnedSection = pinned;
                }
                AppTile tile = new AppTile(path, pinned, gridMode, width, ReduceMotionNow) { Margin = gridMode ? new Padding(4, 3, 4, 7) : new Padding(0, 0, 0, 5), AllowDrop = true };
                tooltips.SetToolTip(tile, Path.GetFileNameWithoutExtension(path));
                tile.MouseWheel += delegate(object sender, MouseEventArgs e) { themedScroll.ScrollBy(-(e.Delta / 120) * 70); HandledMouseEventArgs handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true; };
                Point childDragStart = Point.Empty; bool childDidDrag = false;
                tile.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { childDragStart = e.Location; childDidDrag = false; } };
                tile.GiveFeedback += delegate(object sender, GiveFeedbackEventArgs e) { if (activeDragGhost != null && !activeDragGhost.IsDisposed) activeDragGhost.FollowPointer(); e.UseDefaultCursors = true; };
                tile.QueryContinueDrag += delegate { if (activeDragGhost != null && !activeDragGhost.IsDisposed) activeDragGhost.FollowPointer(); };
                tile.MouseMove += delegate(object sender, MouseEventArgs e) {
                    if (e.Button != MouseButtons.Left || childDidDrag) return;
                    if (Math.Abs(e.X - childDragStart.X) < SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y - childDragStart.Y) < SystemInformation.DragSize.Height / 2) return;
                    childDidDrag = true; DataObject data = new DataObject(); data.SetData(SourceGroupFormat, groupId); data.SetData(MemberPathFormat, path);
                    BeginGridDrag(path); activeDragGhost = new DragTileGhost(tile, childDragStart); tile.SetDragVisual(true, false); activeDragGhost.FollowPointer(); activeDragGhost.Show();
                    DragDropEffects effect;
                    try { effect = tile.DoDragDrop(data, DragDropEffects.Link | DragDropEffects.Move); }
                    finally { if (activeDragGhost != null) { activeDragGhost.Close(); activeDragGhost.Dispose(); activeDragGhost = null; } }
                    bool committed = gridDragCommitted; bool droppedOnDesktop = effect != DragDropEffects.Move && ExplorerDesktop.IsPointOnDesktopSurface(Cursor.Position);
                    if (!committed) CancelGridDragPreview(true); else EndGridDrag();
                    AppTile currentTile = apps.Controls.OfType<AppTile>().FirstOrDefault(item => item.ItemPath.Equals(path, StringComparison.OrdinalIgnoreCase));
                    if (currentTile != null && !currentTile.IsDisposed) currentTile.SetDragVisual(false, false);
                    if (droppedOnDesktop) BeginInvoke(new Action(delegate { AppTile visibleTile = apps.Controls.OfType<AppTile>().FirstOrDefault(item => item.ItemPath.Equals(path, StringComparison.OrdinalIgnoreCase)); AnimateMoveOut(visibleTile, path); }));
                };
                Action activateItem = delegate {
                    if (childDidDrag) { childDidDrag = false; return; }
                    if (nestedGroup != null) { FolderPanel.ShowOrActivate(nestedGroup.TilePath, tile.RectangleToScreen(tile.ClientRectangle), settings); return; }
                    try { Process.Start(path); } catch (Exception e) { MessageBox.Show(e.Message, "Desktop Folders"); }
                };
                tile.ItemActivated += delegate { activateItem(); };
                tile.DragEnter += delegate(object sender, DragEventArgs e) { HandleTileDragEnter(tile, path, pinned, e); };
                tile.DragOver += delegate(object sender, DragEventArgs e) { HandleTileDragEnter(tile, path, pinned, e); };
                tile.DragLeave += delegate { tile.SetDragVisual(false, false); };
                tile.DragDrop += delegate(object sender, DragEventArgs e) { HandleTileDrop(tile, path, pinned, e); };
                ContextMenuStrip quickMenu = new ContextMenuStrip { ShowImageMargin = false, BackColor = CollectionTheme.Control, ForeColor = CollectionTheme.Text, Font = new Font("Segoe UI", 9.5f) };
                quickMenu.Items.Add(nestedGroup == null ? "Mở" : "Mở collection", null, delegate { activateItem(); });
                quickMenu.Items.Add(pinned ? "Bỏ ghim ưu tiên" : "Ghim ưu tiên", null, delegate { TogglePin(path); });
                quickMenu.Items.Add("Đưa ra Desktop", null, delegate { AnimateMoveOut(tile, path); });
                quickMenu.Items.Add(new ToolStripSeparator());
                quickMenu.Items.Add("Di chuyển về trước", null, delegate { MoveMemberBy(path, -1); });
                quickMenu.Items.Add("Di chuyển về sau", null, delegate { MoveMemberBy(path, 1); });
                quickMenu.Items.Add(new ToolStripSeparator());
                quickMenu.Items.Add("Tùy chọn Windows…", null, delegate { BeginInvoke(new Action(delegate { if (!IsDisposed) ShellContextMenu.Show(this, path, pinned ? "Bỏ ghim ưu tiên" : "Ghim ưu tiên", delegate { TogglePin(path); }, delegate { AnimateMoveOut(tile, path); }, delegate { MoveMemberBy(path, -1); }, delegate { MoveMemberBy(path, 1); }); })); });
                tile.SetImmediateContextMenu(quickMenu);
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
            string[] externalPaths = String.IsNullOrEmpty(sourcePath) && e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
            string draggedPath = !String.IsNullOrEmpty(sourcePath) ? sourcePath : (externalPaths != null && externalPaths.Length > 0 ? externalPaths[0] : null);
            VirtualGroup targetGroup = VirtualLayoutGraph.ResolveTileGroup(layout, targetPath);
            bool canCreateNested = sourceGroup == groupId && !String.IsNullOrEmpty(sourcePath) && !sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase);
            bool canDropIntoNested = targetGroup != null && !String.IsNullOrEmpty(draggedPath) && !draggedPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase);
            if ((canCreateNested || canDropIntoNested) && IsNestDropZone(targetTile, e))
            {
                if (!String.Equals(nestHoverTargetPath, targetPath, StringComparison.OrdinalIgnoreCase)) { ResetNestHover(); nestHoverTargetPath = targetPath; nestHoverStarted = Environment.TickCount; nestHoverTimer.Start(); }
                nestDropArmed = nestDropArmed || unchecked(Environment.TickCount - nestHoverStarted) >= settings.FolderHoverDelay;
                e.Effect = !String.IsNullOrEmpty(sourceGroup) ? DragDropEffects.Move : DragDropEffects.Link;
                targetTile.SetDragVisual(false, true, nestDropArmed); if (activeDragGhost != null && !activeDragGhost.IsDisposed) activeDragGhost.FollowPointer(); return;
            }
            ResetNestHover();
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
            string[] externalPaths = String.IsNullOrEmpty(sourcePath) && e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
            VirtualGroup targetGroup = VirtualLayoutGraph.ResolveTileGroup(layout, targetPath);
            if (nestDropArmed && String.Equals(nestHoverTargetPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                bool nested = false;
                if (targetGroup != null)
                {
                    if (!String.IsNullOrEmpty(sourceGroup) && !String.IsNullOrEmpty(sourcePath)) nested = TransferMember(sourceGroup, sourcePath, targetGroup.Id);
                    else if (externalPaths != null && externalPaths.Length > 0) { AddMember(externalPaths[0], targetGroup.Id); NotifyLayoutChanged(); nested = true; }
                }
                else if (sourceGroup == groupId && !String.IsNullOrEmpty(sourcePath)) nested = CreateNestedGroup(sourcePath, targetPath);
                ResetNestHover();
                if (nested) { gridDragCommitted = true; gridDragActive = false; e.Effect = String.IsNullOrEmpty(sourceGroup) ? DragDropEffects.Link : DragDropEffects.Move; return; }
            }
            ResetNestHover();
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
            CancelReorderAnimation(); ResetNestHover(); gridDragActive = true; gridDragCommitted = false; gridDragSourcePath = sourcePath; gridPreviewTargetPath = null; gridPreviewAfter = false; gridHasSwapped = false; gridLastSwapPointer = Cursor.Position;
        }
        static bool IsNestDropZone(AppTile targetTile, DragEventArgs e)
        {
            Point local = targetTile.PointToClient(new Point(e.X, e.Y)); Rectangle center = Rectangle.Inflate(targetTile.ClientRectangle, -Math.Max(12, targetTile.Width / 5), -Math.Max(10, targetTile.Height / 5));
            return center.Width > 0 && center.Height > 0 && center.Contains(local);
        }
        void ArmNestHover()
        {
            nestHoverTimer.Stop(); if (String.IsNullOrEmpty(nestHoverTargetPath)) return;
            AppTile target = apps.Controls.OfType<AppTile>().FirstOrDefault(tile => tile.ItemPath.Equals(nestHoverTargetPath, StringComparison.OrdinalIgnoreCase)); if (target == null || target.IsDisposed) { ResetNestHover(); return; }
            Rectangle center = Rectangle.Inflate(target.RectangleToScreen(target.ClientRectangle), -Math.Max(12, target.Width / 5), -Math.Max(10, target.Height / 5));
            if (!center.Contains(Cursor.Position)) { ResetNestHover(); return; }
            nestDropArmed = true; target.SetDragVisual(false, true, true);
        }
        void ResetNestHover() { if (nestHoverTimer != null) nestHoverTimer.Stop(); nestHoverTargetPath = null; nestHoverStarted = 0; nestDropArmed = false; }
        void PreviewGridReorder(string sourcePath, string targetPath, bool insertAfter)
        {
            if (!gridDragActive || String.IsNullOrEmpty(sourcePath) || sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase)) return;
            if (String.Equals(gridPreviewTargetPath, targetPath, StringComparison.OrdinalIgnoreCase) && gridPreviewAfter == insertAfter) return;
            Point pointer = Cursor.Position; int hysteresis = gridMode ? 14 : 10;
            if (gridHasSwapped && Math.Abs(pointer.X - gridLastSwapPointer.X) + Math.Abs(pointer.Y - gridLastSwapPointer.Y) < hysteresis) return;
            AppTile source = apps.Controls.OfType<AppTile>().FirstOrDefault(tile => tile.ItemPath.Equals(sourcePath, StringComparison.OrdinalIgnoreCase)); AppTile target = apps.Controls.OfType<AppTile>().FirstOrDefault(tile => tile.ItemPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
            if (source == null || target == null) return;
            bool sourcePinned = group.Pinned.Contains(sourcePath, StringComparer.OrdinalIgnoreCase), targetPinned = group.Pinned.Contains(targetPath, StringComparer.OrdinalIgnoreCase); if (sourcePinned != targetPinned) return;
            List<AppTile> sectionTiles = apps.Controls.OfType<AppTile>().Where(tile => group.Pinned.Contains(tile.ItemPath, StringComparer.OrdinalIgnoreCase) == sourcePinned).ToList();
            Dictionary<string, Point> previous = sectionTiles.ToDictionary(tile => tile.ItemPath, tile => tile.Location, StringComparer.OrdinalIgnoreCase);
            // Continue from the frame currently visible instead of snapping the
            // preceding motion to its endpoint before targeting the next slot.
            StopReorderAnimation(false);
            sectionTiles.Remove(source); int targetIndex = sectionTiles.FindIndex(tile => tile.ItemPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase)); if (targetIndex < 0) return; if (insertAfter) targetIndex++; sectionTiles.Insert(Math.Max(0, Math.Min(sectionTiles.Count, targetIndex)), source);
            int firstControlIndex = sectionTiles.Min(tile => apps.Controls.GetChildIndex(tile)); apps.SuspendLayout();
            for (int index = 0; index < sectionTiles.Count; index++) apps.Controls.SetChildIndex(sectionTiles[index], firstControlIndex + index);
            apps.ResumeLayout(true); apps.PerformLayout(); gridPreviewTargetPath = targetPath; gridPreviewAfter = insertAfter; gridHasSwapped = true; gridLastSwapPointer = pointer; StartReorderAnimation(previous);
        }
        void CommitGridReorder()
        {
            if (!gridDragActive) return; StopReorderAnimation(true); layout = DataStore.LoadVirtualLayout(); group = layout.Groups.FirstOrDefault(g => g.Id == groupId); if (group == null) { EndGridDrag(); return; }
            List<string> visual = apps.Controls.OfType<AppTile>().Select(tile => tile.ItemPath).ToList(); ApplyVisualSubset(group.Members, visual.Where(path => group.Pinned.Contains(path, StringComparer.OrdinalIgnoreCase)).ToList()); ApplyVisualSubset(group.Members, visual.Where(path => !group.Pinned.Contains(path, StringComparer.OrdinalIgnoreCase)).ToList());
            DataStore.SaveVirtualLayout(layout); VirtualLayoutGraph.RefreshGroupAndAncestors(layout, group.Id); gridDragCommitted = true; gridDragActive = false; gridPreviewTargetPath = null;
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
            ResetNestHover(); gridDragActive = false; gridDragCommitted = false; gridDragSourcePath = null; gridPreviewTargetPath = null; gridPreviewAfter = false; gridHasSwapped = false;
        }
        void StartReorderAnimation(Dictionary<string, Point> previous)
        {
            if (ReduceMotionNow || previous == null || previous.Count == 0) { reorderVelocities.Clear(); return; }
            List<TileMotion> motions = new List<TileMotion>();
            foreach (AppTile tile in apps.Controls.OfType<AppTile>())
            {
                Point from; if (!previous.TryGetValue(tile.ItemPath, out from) || from == tile.Location) continue;
                PointF velocity; if (!reorderVelocities.TryGetValue(tile.ItemPath, out velocity)) velocity = PointF.Empty;
                motions.Add(new TileMotion { Tile = tile, Current = new PointF(from.X, from.Y), Velocity = velocity, To = tile.Location });
            }
            if (motions.Count == 0) return;
            apps.SuspendLayout(); reorderLayoutSuspended = true; reorderMotions = motions;
            foreach (TileMotion motion in motions) motion.Tile.Location = Point.Round(motion.Current);
            if (reorderAnimation == null) { reorderAnimation = new System.Windows.Forms.Timer { Interval = 8 }; reorderAnimation.Tick += delegate { AnimateReorderFrame(); }; }
            reorderLastFrame = Environment.TickCount; reorderAnimation.Start();
        }
        void AnimateReorderFrame()
        {
            if (reorderMotions == null || reorderMotions.Count == 0) { CancelReorderAnimation(); return; }
            int now = Environment.TickCount; float seconds = Math.Max(1, Math.Min(40, unchecked(now - reorderLastFrame))) / 1000f; reorderLastFrame = now;
            // Exact critically-damped spring step. It stays stable after a slow UI
            // frame and preserves velocity when the pointer reverses direction.
            const float omega = 28f; float decay = (float)Math.Exp(-omega * seconds); bool settled = true;
            foreach (TileMotion motion in reorderMotions)
            {
                if (motion.Tile.IsDisposed) continue;
                float errorX = motion.Current.X - motion.To.X, errorY = motion.Current.Y - motion.To.Y;
                float coupledX = motion.Velocity.X + omega * errorX, coupledY = motion.Velocity.Y + omega * errorY;
                float nextErrorX = (errorX + coupledX * seconds) * decay, nextErrorY = (errorY + coupledY * seconds) * decay;
                float vx = (motion.Velocity.X - omega * coupledX * seconds) * decay, vy = (motion.Velocity.Y - omega * coupledY * seconds) * decay;
                motion.Current = new PointF(motion.To.X + nextErrorX, motion.To.Y + nextErrorY); motion.Velocity = new PointF(vx, vy);
                if (Math.Abs(nextErrorX) < .7f && Math.Abs(nextErrorY) < .7f && Math.Abs(vx) < 7f && Math.Abs(vy) < 7f) { motion.Current = new PointF(motion.To.X, motion.To.Y); motion.Velocity = PointF.Empty; }
                else settled = false;
                motion.Tile.Location = Point.Round(motion.Current); reorderVelocities[motion.Tile.ItemPath] = motion.Velocity;
            }
            if (settled) CancelReorderAnimation();
        }
        void CancelReorderAnimation()
        {
            StopReorderAnimation(true);
        }
        void StopReorderAnimation(bool settle)
        {
            if (reorderAnimation != null) reorderAnimation.Stop();
            if (reorderMotions != null) foreach (TileMotion motion in reorderMotions)
            {
                if (settle) { if (!motion.Tile.IsDisposed) motion.Tile.Location = motion.To; reorderVelocities.Remove(motion.Tile.ItemPath); }
                else reorderVelocities[motion.Tile.ItemPath] = motion.Velocity;
            }
            if (settle) reorderVelocities.Clear();
            reorderMotions = null;
            if (reorderLayoutSuspended && apps != null && !apps.IsDisposed) { reorderLayoutSuspended = false; apps.ResumeLayout(false); if (settle) apps.PerformLayout(); }
        }
        void AnimateMoveOut(AppTile tile, string path)
        {
            if (ReduceMotionNow || tile == null || tile.IsDisposed) { MoveOut(path); return; }
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
            DataStore.SaveVirtualLayout(layout); VirtualLayoutGraph.RefreshGroupAndAncestors(layout, group.Id); NotifyLayoutChanged();
        }
        void MoveMemberBy(string path, int delta)
        {
            layout = DataStore.LoadVirtualLayout(); group = layout.Groups.FirstOrDefault(g => g.Id == groupId); if (group == null || delta == 0) return;
            bool pinned = group.Pinned.Contains(path, StringComparer.OrdinalIgnoreCase); List<string> section = group.Members.Where(member => group.Pinned.Contains(member.Path, StringComparer.OrdinalIgnoreCase) == pinned).Select(member => member.Path).ToList();
            int current = section.FindIndex(item => item.Equals(path, StringComparison.OrdinalIgnoreCase)); int requested = Math.Max(0, Math.Min(section.Count - 1, current + Math.Sign(delta))); if (current < 0 || current == requested) return;
            string moving = section[current]; section.RemoveAt(current); section.Insert(requested, moving); ApplyVisualSubset(group.Members, section); DataStore.SaveVirtualLayout(layout); VirtualLayoutGraph.RefreshGroupAndAncestors(layout, group.Id); NotifyLayoutChanged();
        }
        void MoveOut(string path)
        {
            layout = DataStore.LoadVirtualLayout(); group = layout.Groups.FirstOrDefault(g => g.Id == groupId); if (group == null) return;
            VirtualMember member = group.Members.FirstOrDefault(m => m.Path.Equals(path, StringComparison.OrdinalIgnoreCase)); if (member == null) return;
            List<string> oldPinned = new List<string>(group.Pinned); Point preferredPlacement = PreferredDesktopPoint(group, this);
            try { File.SetAttributes(member.Path, (FileAttributes)member.OriginalAttributes); group.Members.Remove(member); group.Pinned.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)); ExplorerDesktop.NotifyPathChanged(member.Path); }
            catch (Exception e) { try { File.SetAttributes(member.Path, File.GetAttributes(member.Path) | FileAttributes.Hidden); } catch { } if (!group.Members.Contains(member)) group.Members.Add(member); group.Pinned = oldPinned; MessageBox.Show(e.Message, "Desktop Folders"); return; }
            if (settings.DissolveSingleAppGroup && group.Members.Count <= 1)
            {
                List<string> affected = DissolveGroupRecord(layout, group, this); DataStore.SaveVirtualLayout(layout);
                foreach (string affectedId in affected) VirtualLayoutGraph.RefreshGroupAndAncestors(layout, affectedId);
                ExplorerDesktop.QueuePlacement(member.Path, preferredPlacement); NotifyLayoutChanged(); Close();
            }
            else { DataStore.SaveVirtualLayout(layout); VirtualLayoutGraph.RefreshGroupAndAncestors(layout, group.Id); ExplorerDesktop.QueuePlacement(member.Path, preferredPlacement); NotifyLayoutChanged(); }
        }
        void DissolveGroup()
        {
            layout = DataStore.LoadVirtualLayout(); group = layout.Groups.FirstOrDefault(g => g.Id == groupId); if (group == null) return;
            List<string> affected = DissolveGroupRecord(layout, group, this); DataStore.SaveVirtualLayout(layout);
            foreach (string affectedId in affected) VirtualLayoutGraph.RefreshGroupAndAncestors(layout, affectedId);
            NotifyLayoutChanged(); Close();
        }
        static List<string> DissolveGroupRecord(VirtualLayout current, VirtualGroup dissolving, FolderPanel panel = null)
        {
            List<string> affected = new List<string>(); List<VirtualMember> desktopRestores = new List<VirtualMember>(); List<VirtualGroup> parents = VirtualLayoutGraph.ParentsOf(current, dissolving.Id); Point preferredPlacement = PreferredDesktopPoint(dissolving, panel);
            if (parents.Count == 0)
            {
                foreach (VirtualMember remaining in dissolving.Members) try { if (File.Exists(remaining.Path)) { File.SetAttributes(remaining.Path, (FileAttributes)remaining.OriginalAttributes); ExplorerDesktop.NotifyPathChanged(remaining.Path); desktopRestores.Add(remaining); } } catch { }
            }
            else
            {
                foreach (VirtualGroup parent in parents)
                {
                    List<VirtualMember> links = parent.Members.Where(member => String.Equals(member.GroupId, dissolving.Id, StringComparison.OrdinalIgnoreCase) || (!String.IsNullOrEmpty(member.Path) && member.Path.Equals(dissolving.TilePath, StringComparison.OrdinalIgnoreCase))).ToList();
                    int insertion = links.Count == 0 ? parent.Members.Count : parent.Members.IndexOf(links[0]); bool wasPinned = links.Any(link => parent.Pinned.Contains(link.Path, StringComparer.OrdinalIgnoreCase));
                    foreach (VirtualMember link in links) { parent.Members.Remove(link); parent.Pinned.RemoveAll(path => path.Equals(link.Path, StringComparison.OrdinalIgnoreCase)); }
                    foreach (VirtualMember remaining in dissolving.Members)
                    {
                        bool duplicate = parent.Members.Any(member => (!String.IsNullOrEmpty(remaining.GroupId) && String.Equals(member.GroupId, remaining.GroupId, StringComparison.OrdinalIgnoreCase)) || member.Path.Equals(remaining.Path, StringComparison.OrdinalIgnoreCase));
                        if (!duplicate) { parent.Members.Insert(Math.Min(insertion++, parent.Members.Count), remaining); if (wasPinned && !parent.Pinned.Contains(remaining.Path, StringComparer.OrdinalIgnoreCase)) parent.Pinned.Add(remaining.Path); }
                    }
                    affected.Add(parent.Id);
                }
            }
            try { if (File.Exists(dissolving.TilePath)) File.Delete(dissolving.TilePath); } catch { }
            ExplorerDesktop.NotifyPathDeleted(dissolving.TilePath);
            try { if (!String.IsNullOrEmpty(dissolving.IconPath) && File.Exists(dissolving.IconPath)) File.Delete(dissolving.IconPath); } catch { }
            foreach (VirtualMember restored in desktopRestores) ExplorerDesktop.QueuePlacement(restored.Path, preferredPlacement);
            current.Groups.Remove(dissolving); return affected;
        }
        static Point PreferredDesktopPoint(VirtualGroup sourceGroup, FolderPanel panel = null)
        {
            Point pointer = Cursor.Position; if (ExplorerDesktop.IsPointOnDesktopSurface(pointer)) return pointer;
            Rectangle tileBounds;
            if (sourceGroup != null && !String.IsNullOrEmpty(sourceGroup.TilePath) && ExplorerDesktop.TryGetIconBounds(sourceGroup.TilePath, out tileBounds) && tileBounds.Width > 0 && tileBounds.Height > 0)
                return new Point(tileBounds.Left + tileBounds.Width / 2, tileBounds.Top + tileBounds.Height / 2);
            if (panel == null && sourceGroup != null)
            {
                lock (OpenPanelsLock)
                {
                    WeakReference wr;
                    if (OpenPanels.TryGetValue(sourceGroup.Id, out wr) && wr != null) panel = wr.Target as FolderPanel;
                }
            }
            Screen monitor = null;
            if (panel != null && !panel.IsDisposed && panel.Visible)
            {
                monitor = Screen.FromControl(panel);
                if (monitor == null) monitor = Screen.FromRectangle(panel.Bounds);
            }
            if (monitor == null && sourceGroup != null && !String.IsNullOrEmpty(sourceGroup.TilePath) && ExplorerDesktop.TryGetIconBounds(sourceGroup.TilePath, out tileBounds))
            {
                monitor = Screen.FromRectangle(tileBounds);
            }
            if (monitor == null) monitor = Screen.FromPoint(pointer);
            if (monitor != null)
            {
                Rectangle work = monitor.WorkingArea;
                return new Point(work.Left + 48, work.Top + 48);
            }
            Screen primary = Screen.PrimaryScreen;
            if (primary != null)
            {
                Rectangle work = primary.WorkingArea;
                return new Point(work.Left + 48, work.Top + 48);
            }
            return new Point(48, 48);
        }
        void RenameGroup(string value)
        {
            string requested = value.Trim(); if (requested.Length == 0 || requested == group.Name) { titleLabel.Text = group.Name; return; }
            foreach (char invalid in Path.GetInvalidFileNameChars()) requested = requested.Replace(invalid.ToString(), ""); if (requested.Length == 0) return;
            layout = DataStore.LoadVirtualLayout(); group = layout.Groups.FirstOrDefault(g => g.Id == groupId); if (group == null) return;
            string newPath = Path.Combine(ExplorerDesktop.Desktop, requested + ".lnk"); if (File.Exists(newPath)) { titleLabel.Text = group.Name; titleEditor.Text = group.Name; return; }
            string oldPath = group.TilePath, oldName = group.Name;
            try { File.Move(oldPath, newPath); ExplorerDesktop.NotifyPathDeleted(oldPath); group.TilePath = newPath; group.Name = requested; VirtualLayoutGraph.ReplaceTilePath(layout, group.Id, oldPath, newPath); DataStore.SaveVirtualLayout(layout); VirtualLayoutGraph.RefreshGroupAndAncestors(layout, group.Id); titleLabel.Text = requested; NotifyLayoutChanged(); }
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
        bool TransferMember(string sourceGroupId, string path, string destinationGroupId = null)
        {
            VirtualLayout latest = DataStore.LoadVirtualLayout(); VirtualGroup from = latest.Groups.FirstOrDefault(g => g.Id == sourceGroupId); VirtualGroup to = latest.Groups.FirstOrDefault(g => g.Id == (destinationGroupId ?? groupId));
            if (from == null || to == null || to.Members.Any(m => m.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return false;
            VirtualMember member = from.Members.FirstOrDefault(m => m.Path.Equals(path, StringComparison.OrdinalIgnoreCase)); if (member == null) return false;
            VirtualGroup nested = VirtualLayoutGraph.ResolveMemberGroup(latest, member);
            if (nested != null && VirtualLayoutGraph.WouldCreateCycle(latest, nested.Id, to.Id)) { MessageBox.Show("Không thể đặt một collection vào chính nó hoặc vào collection con của nó.", "Desktop Folders"); return false; }
            from.Members.Remove(member); from.Pinned.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)); to.Members.Add(member);
            bool dissolveSource = settings.DissolveSingleAppGroup && from.Members.Count <= 1;
            List<string> affected = dissolveSource ? DissolveGroupRecord(latest, from, this) : new List<string>();
            DataStore.SaveVirtualLayout(latest); VirtualLayoutGraph.RefreshGroupAndAncestors(latest, to.Id);
            if (dissolveSource)
            {
                foreach (string affectedId in affected) VirtualLayoutGraph.RefreshGroupAndAncestors(latest, affectedId);
            }
            else VirtualLayoutGraph.RefreshGroupAndAncestors(latest, from.Id);
            NotifyLayoutChanged(); return true;
        }
        void AddMember(string path, string destinationGroupId = null)
        {
            if (!File.Exists(path)) return; layout = DataStore.LoadVirtualLayout(); VirtualGroup destination = layout.Groups.FirstOrDefault(g => g.Id == (destinationGroupId ?? groupId)); if (destination == null || destination.Members.Any(m => m.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
            VirtualGroup nested = VirtualLayoutGraph.ResolveTileGroup(layout, path);
            if (nested != null)
            {
                if (VirtualLayoutGraph.WouldCreateCycle(layout, nested.Id, destination.Id)) throw new InvalidOperationException("Không thể đặt một collection vào chính nó hoặc vào collection con của nó.");
                VirtualGroup existingParent = VirtualLayoutGraph.ParentsOf(layout, nested.Id).FirstOrDefault();
                if (existingParent != null) throw new InvalidOperationException("Collection này đã nằm trong \"" + existingParent.Name + "\". Hãy kéo nó trực tiếp từ collection đó để di chuyển.");
            }
            FileAttributes attributes = File.GetAttributes(path); VirtualMember added = new VirtualMember { Path = path, OriginalAttributes = (int)attributes, GroupId = nested == null ? null : nested.Id };
            try { File.SetAttributes(path, attributes | FileAttributes.Hidden); destination.Members.Add(added); DataStore.SaveVirtualLayout(layout); ExplorerDesktop.NotifyPathChanged(path); }
            catch { destination.Members.Remove(added); try { File.SetAttributes(path, attributes); } catch { } throw; }
            VirtualLayoutGraph.RefreshGroupAndAncestors(layout, destination.Id);
        }
        bool CreateNestedGroup(string sourcePath, string targetPath)
        {
            VirtualLayout latest = DataStore.LoadVirtualLayout(); VirtualGroup parent = latest.Groups.FirstOrDefault(g => g.Id == groupId); if (parent == null) return false;
            VirtualMember sourceMember = parent.Members.FirstOrDefault(member => member.Path.Equals(sourcePath, StringComparison.OrdinalIgnoreCase));
            VirtualMember targetMember = parent.Members.FirstOrDefault(member => member.Path.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
            if (sourceMember == null || targetMember == null || Object.ReferenceEquals(sourceMember, targetMember)) return false;
            int insertion = Math.Min(parent.Members.IndexOf(sourceMember), parent.Members.IndexOf(targetMember)); bool pinNested = parent.Pinned.Contains(sourcePath, StringComparer.OrdinalIgnoreCase) || parent.Pinned.Contains(targetPath, StringComparer.OrdinalIgnoreCase);
            string name = ExplorerDesktop.NewGroupName(), nestedId = Guid.NewGuid().ToString("N"), tilePath = Path.Combine(ExplorerDesktop.Desktop, name + ".lnk");
            VirtualGroup nested = new VirtualGroup { Id = nestedId, Name = name, TilePath = tilePath }; nested.Members.Add(targetMember); nested.Members.Add(sourceMember);
            try
            {
                GroupTileFactory.CreateOrUpdate(nested); FileAttributes tileAttributes = File.GetAttributes(tilePath); File.SetAttributes(tilePath, tileAttributes | FileAttributes.Hidden);
                parent.Members.Remove(sourceMember); parent.Members.Remove(targetMember); parent.Pinned.RemoveAll(path => path.Equals(sourcePath, StringComparison.OrdinalIgnoreCase) || path.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
                VirtualMember nestedMember = new VirtualMember { Path = tilePath, OriginalAttributes = (int)tileAttributes, GroupId = nestedId }; parent.Members.Insert(Math.Min(insertion, parent.Members.Count), nestedMember); if (pinNested) parent.Pinned.Add(tilePath);
                latest.Groups.Add(nested); DataStore.SaveVirtualLayout(latest); VirtualLayoutGraph.RefreshGroupAndAncestors(latest, nested.Id); ExplorerDesktop.NotifyPathChanged(tilePath); NotifyLayoutChanged(); return true;
            }
            catch (Exception error)
            {
                try { if (File.Exists(tilePath)) File.Delete(tilePath); } catch { } try { if (!String.IsNullOrEmpty(nested.IconPath) && File.Exists(nested.IconPath)) File.Delete(nested.IconPath); } catch { }
                MessageBox.Show("Không thể tạo collection lồng: " + error.Message, "Desktop Folders"); return false;
            }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Native.WM_MOUSEACTIVATE) { Native.SetForegroundWindow(Handle); message.Result = (IntPtr)Native.MA_ACTIVATE; return; }
            base.WndProc(ref message);
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e); if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) return;
            Rectangle rounded = new Rectangle(0, 0, Math.Max(1, ClientSize.Width - 1), Math.Max(1, ClientSize.Height - 1));
            using (System.Drawing.Drawing2D.GraphicsPath path = DrawExtensions.RoundPath(rounded, CollectionTheme.RadiusWindow)) { Region old = Region; Region = new Region(path); if (old != null) old.Dispose(); }
        }
        protected override void Dispose(bool disposing) { if (disposing) { if (activeDragGhost != null) activeDragGhost.Dispose(); if (releaseTopMost != null) releaseTopMost.Dispose(); if (reorderAnimation != null) reorderAnimation.Dispose(); if (nestHoverTimer != null) nestHoverTimer.Dispose(); if (tooltips != null) tooltips.Dispose(); } base.Dispose(disposing); }
    }

    internal static class CollectionArtwork
    {
        static readonly Bitmap background = LoadBackground();
        internal static Image Background { get { return background; } }
        static Bitmap LoadBackground()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DesktopFolders.CollectionBackground.png"))
                using (Image source = stream == null ? null : Image.FromStream(stream)) if (source != null) return new Bitmap(source);
            }
            catch { }
            return null;
        }
    }

    internal sealed class GradientPanel : Panel
    {
        int cornerRadius;
        Bitmap renderedBackground;
        Size renderedSize;
        internal int CornerRadius { get { return cornerRadius; } set { cornerRadius = value; UpdateRoundedRegion(); } }
        internal GradientPanel()
        {
            DoubleBuffered = true; BackColor = CollectionTheme.Background;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnResize(EventArgs eventArgs) { base.OnResize(eventArgs); UpdateRoundedRegion(); RebuildBackground(); }
        void UpdateRoundedRegion()
        {
            if (cornerRadius <= 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            Rectangle rounded = new Rectangle(0, 0, Math.Max(1, ClientSize.Width - 1), Math.Max(1, ClientSize.Height - 1));
            using (System.Drawing.Drawing2D.GraphicsPath path = DrawExtensions.RoundPath(rounded, cornerRadius)) { Region old = Region; Region = new Region(path); if (old != null) old.Dispose(); }
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle area = ClientRectangle; if (area.Width <= 0 || area.Height <= 0) return;
            if (renderedBackground == null || renderedSize != area.Size) RebuildBackground();
            if (renderedBackground != null) e.Graphics.DrawImageUnscaled(renderedBackground, Point.Empty); else using (Brush fallback = new SolidBrush(CollectionTheme.Background)) e.Graphics.FillRectangle(fallback, area);
        }
        void RebuildBackground()
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return; if (renderedBackground != null) { renderedBackground.Dispose(); renderedBackground = null; }
            renderedSize = ClientSize; renderedBackground = new Bitmap(ClientSize.Width, ClientSize.Height); using (Graphics graphics = Graphics.FromImage(renderedBackground)) RenderBackground(graphics, new Rectangle(Point.Empty, ClientSize)); Invalidate();
        }
        static void RenderBackground(Graphics graphics, Rectangle area)
        {
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality; graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            Image artwork = CollectionArtwork.Background;
            if (artwork != null)
            {
                double destinationAspect = area.Width / (double)Math.Max(1, area.Height), sourceAspect = artwork.Width / (double)Math.Max(1, artwork.Height); Rectangle source;
                if (destinationAspect >= sourceAspect)
                {
                    int cropHeight = Math.Max(1, Math.Min(artwork.Height, (int)Math.Round(artwork.Width / destinationAspect))); source = new Rectangle(0, artwork.Height - cropHeight, artwork.Width, cropHeight);
                }
                else
                {
                    int cropWidth = Math.Max(1, Math.Min(artwork.Width, (int)Math.Round(artwork.Height * destinationAspect))); source = new Rectangle((artwork.Width - cropWidth) / 2, 0, cropWidth, artwork.Height);
                }
                graphics.DrawImage(artwork, area, source, GraphicsUnit.Pixel);
                using (System.Drawing.Drawing2D.LinearGradientBrush veil = new System.Drawing.Drawing2D.LinearGradientBrush(area, Color.FromArgb(142, 2, 1, 13), Color.FromArgb(50, 18, 5, 48), 90f)) graphics.FillRectangle(veil, area);
            }
            else using (Brush fallback = new SolidBrush(CollectionTheme.Background)) graphics.FillRectangle(fallback, area);
            Rectangle glow = new Rectangle(-area.Width / 4, -area.Height / 2, area.Width + area.Width / 2, Math.Max(180, area.Height));
            using (System.Drawing.Drawing2D.LinearGradientBrush ambient = new System.Drawing.Drawing2D.LinearGradientBrush(glow, Color.FromArgb(20, CollectionTheme.Focus), Color.FromArgb(0, CollectionTheme.Focus), 90f)) graphics.FillEllipse(ambient, glow);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (ClientSize.Width <= 2 || ClientSize.Height <= 2) return; e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle border = new Rectangle(1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
            using (System.Drawing.Drawing2D.GraphicsPath path = DrawExtensions.RoundPath(border, Math.Max(4, cornerRadius - 1)))
            using (System.Drawing.Drawing2D.LinearGradientBrush neon = new System.Drawing.Drawing2D.LinearGradientBrush(border, Color.FromArgb(205, CollectionTheme.FocusBlue), Color.FromArgb(220, CollectionTheme.Focus), 0f))
            using (Pen pen = new Pen(neon, 1.4f)) e.Graphics.DrawPath(pen, path);
        }
        protected override void Dispose(bool disposing) { if (disposing && renderedBackground != null) renderedBackground.Dispose(); base.Dispose(disposing); }
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
            maximum = Math.Max(0, content.Height - viewport.ClientSize.Height); value = Math.Max(0, Math.Min(maximum, value)); MoveContent(-value);
            Visible = maximum > 0 && Height > 20; Invalidate();
        }
        internal void SetValue(int requested) { value = Math.Max(0, Math.Min(maximum, requested)); MoveContent(-value); Invalidate(); }
        internal void ScrollBy(int delta) { SyncFromTarget(); SetValue(value + delta); }
        void MoveContent(int requestedTop)
        {
            if (content.Top == requestedTop) return; content.Top = requestedTop; content.Invalidate(true); viewport.Invalidate(true); viewport.Update();
        }
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
        internal Color BorderEndColor { get; set; }
        internal int BorderThickness { get; set; }
        internal bool ShowSearchGlyph { get; set; }
        internal Color GlyphColor { get; set; }
        internal RoundedPanel() { Radius = 16; BorderColor = Color.Transparent; BorderEndColor = Color.Transparent; BorderThickness = 0; GlyphColor = CollectionTheme.MutedText; DoubleBuffered = true; Resize += delegate { UpdateRoundedRegion(); }; }
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
                if (BorderEndColor.A > 0)
                {
                    using (System.Drawing.Drawing2D.LinearGradientBrush glowGradient = new System.Drawing.Drawing2D.LinearGradientBrush(border, BorderColor, BorderEndColor, 0f))
                    {
                        glowGradient.InterpolationColors = CollectionTheme.NeonBlend(52);
                        using (Pen glowPen = new Pen(glowGradient, BorderThickness + 2f)) e.Graphics.DrawRoundedRectangle(glowPen, border, Math.Max(3, Radius - inset));
                    }
                    using (System.Drawing.Drawing2D.LinearGradientBrush gradient = new System.Drawing.Drawing2D.LinearGradientBrush(border, BorderColor, BorderEndColor, 0f))
                    {
                        gradient.InterpolationColors = CollectionTheme.NeonBlend(255); gradient.GammaCorrection = true;
                        using (Pen pen = new Pen(gradient, BorderThickness)) e.Graphics.DrawRoundedRectangle(pen, border, Math.Max(3, Radius - inset));
                    }
                }
                else using (Pen pen = new Pen(BorderColor, BorderThickness)) e.Graphics.DrawRoundedRectangle(pen, border, Math.Max(3, Radius - inset));
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
        enum DragOwnership { Windows, HoverPending, MergeArmed, Cancelled }
        readonly object cacheLock = new object();
        DesktopItem[] cache = new DesktopItem[0];
        int scanInProgress;
        int inactiveTicks;
        volatile bool exiting;
        System.Threading.Timer scanner;
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
        System.Windows.Forms.Timer pointerMonitor;
        bool leftButtonWasDown;
        DesktopItem pendingSource;
        DesktopItem pendingTarget;
        Action pending;
        SingleInstanceCommandWindow commandWindow;
        readonly object openRequestLock = new object();
        readonly HashSet<string> pendingOpenRequests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int settingsActivationRequested;
        int requestFileTicks;
        internal AppSettings Settings { get; private set; }

        internal DirectDesktopController(string requestedGroupId, bool showSettingsOnLaunch)
        {
            Settings = DataStore.LoadSettings();
            commandWindow = new SingleInstanceCommandWindow(delegate(string command) {
                if (String.Equals(command, SingleInstanceCommandWindow.ActivateCommand, StringComparison.Ordinal)) RequestSettingsActivation();
                else EnqueueOpenGroup(command);
            });
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
            commandPump.Tick += delegate { DrainCommands(); };
            commandPump.Start();
            pointerMonitor = new System.Windows.Forms.Timer { Interval = 15 };
            pointerMonitor.Tick += delegate { PollDesktopPointer(); };
            pointerMonitor.Start();

            Icon appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            tray = new NotifyIcon { Icon = appIcon, Visible = false, Text = "Desktop Folders" };
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Desktop Folders đang hoạt động").Enabled = false;
            menu.Items.Add("Settings…", null, delegate { OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { OpenSettings(); }; tray.Visible = true;

            if (!String.IsNullOrEmpty(requestedGroupId)) EnqueueOpenGroup(requestedGroupId);
            if (showSettingsOnLaunch) RequestSettingsActivation();
            StartBackgroundInitialization();
        }

        void StartBackgroundInitialization()
        {
            Thread initialization = new Thread(new ThreadStart(delegate {
                try
                {
#if !TEST
                    if (!exiting) SyncStartupRegistration(false);
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

        void RequestSettingsActivation() { Interlocked.Exchange(ref settingsActivationRequested, 1); }

        void DrainCommands()
        {
            if (Interlocked.Exchange(ref settingsActivationRequested, 0) != 0) OpenSettings();
            DrainOpenGroupRequests();
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
                    VirtualLayoutGraph.ReplaceTilePath(layout, group.Id, oldTile, candidate);
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

        void PollDesktopPointer()
        {
            if (exiting) return; Point point = Cursor.Position; bool leftDown = (Control.MouseButtons & MouseButtons.Left) != 0;
            if (leftDown && !leftButtonWasDown) BeginDesktopPointer(point);
            else if (leftDown && source != null) ContinueDesktopPointer(point);
            else if (!leftDown && leftButtonWasDown && source != null) EndDesktopPointer(point);
            leftButtonWasDown = leftDown;
        }

        void BeginDesktopPointer(Point point)
        {
            source = null; dragging = false; down = point; hoverTarget = null; armedTarget = null; dragOwnership = DragOwnership.Windows; hoverArm.Stop();
            if (!ExplorerDesktop.IsDesktopForeground() || !ExplorerDesktop.IsPointOnDesktopSurface(point)) return;
            FolderPanel.NotifyDesktopClick(point); DesktopItem[] snapshot; lock (cacheLock) snapshot = cache; source = ExplorerDesktop.HitTest(snapshot, point, null);
            if (source != null) DataStore.LogDrag("POLL_DOWN source=" + source.Name);
        }

        void ContinueDesktopPointer(Point point)
        {
            int thresholdX = Math.Max(2, SystemInformation.DragSize.Width / 2), thresholdY = Math.Max(2, SystemInformation.DragSize.Height / 2);
            if (!dragging && (Math.Abs(point.X - down.X) >= thresholdX || Math.Abs(point.Y - down.Y) >= thresholdY)) dragging = true;
            if (!dragging || dragOwnership == DragOwnership.Cancelled) return;
            if (dragOwnership == DragOwnership.MergeArmed)
            {
                if (armedTarget != null && Rectangle.Inflate(armedTarget.Bounds, 8, 8).Contains(point)) { if (!folderPreview.Visible) folderPreview.Arm(armedTarget, true); }
                else { armedTarget = null; hoverTarget = null; dragOwnership = DragOwnership.Cancelled; hoverArm.Stop(); folderPreview.HideAnimated(); }
                return;
            }
            DesktopItem target = Hit(point);
            if (target != null && target.Path != source.Path)
            {
                if (hoverTarget == null || hoverTarget.Path != target.Path)
                {
                    hoverTarget = target; hoverStarted = Environment.TickCount; dragOwnership = DragOwnership.HoverPending; folderPreview.HideAnimated(); hoverArm.Stop(); hoverArm.Interval = Settings.FolderHoverDelay; hoverArm.Start();
                    DataStore.LogDrag("POLL_HOVER target=" + target.Name + " delay=" + Settings.FolderHoverDelay);
                }
            }
            else CancelHover();
        }

        void EndDesktopPointer(Point point)
        {
            DesktopItem start = source, target = armedTarget; bool wasDragging = dragging, wasCancelled = dragOwnership == DragOwnership.Cancelled;
            bool folderReady = wasDragging && dragOwnership == DragOwnership.MergeArmed && target != null && Rectangle.Inflate(target.Bounds, 8, 8).Contains(point);
            DataStore.LogDrag("POLL_UP commit=" + folderReady + (target == null ? "" : " target=" + target.Name)); ResetDrag();
            if (!wasDragging)
            {
                if (start.IsGroup) Queue(delegate { FolderPanel.ShowOrActivate(start.Path, start.Bounds, Settings); });
                return;
            }
            if (folderReady) { pendingSource = start; pendingTarget = target; postDrop.Stop(); postDrop.Start(); }
            else if (!wasCancelled) { refreshAfterWindowsDrop.Stop(); refreshAfterWindowsDrop.Start(); }
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
            if ((Control.MouseButtons & MouseButtons.Left) == 0 || !Rectangle.Inflate(hoverTarget.Bounds, 8, 8).Contains(Cursor.Position)) { CancelHover(); return; }
            // Cancel Explorer's OLE drag through messages sent only to its Desktop
            // window. This replaces the former global mouse hook and key injection.
            IntPtr list = ExplorerDesktop.CachedListView;
            if (list != IntPtr.Zero)
            {
                Native.SendMessage(list, Native.WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero);
                IntPtr root = Native.GetAncestor(list, Native.GA_ROOT); if (root != IntPtr.Zero) Native.SendMessage(root, Native.WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero);
                Native.PostMessage(list, Native.WM_KEYDOWN, (IntPtr)Native.VK_ESCAPE, IntPtr.Zero); Native.PostMessage(list, Native.WM_KEYUP, (IntPtr)Native.VK_ESCAPE, IntPtr.Zero);
            }
            armedTarget = hoverTarget; dragOwnership = DragOwnership.MergeArmed;
            folderPreview.Arm(armedTarget, Settings.ReduceMotion || SystemInformation.TerminalServerSession);
            DataStore.LogDrag("POLL_ARMED target=" + armedTarget.Name + " elapsed=" + unchecked(Environment.TickCount - hoverStarted));
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
                    VirtualGroup nested = first.IsGroup ? VirtualLayoutGraph.ResolveTileGroup(layout, first.Path) : null;
                    if (nested != null)
                    {
                        if (VirtualLayoutGraph.WouldCreateCycle(layout, nested.Id, existing.Id)) throw new InvalidOperationException("Không thể đặt một collection vào chính nó hoặc vào collection con của nó.");
                        VirtualGroup oldParent = VirtualLayoutGraph.ParentsOf(layout, nested.Id).FirstOrDefault();
                        if (oldParent != null) throw new InvalidOperationException("Collection này đã nằm trong \"" + oldParent.Name + "\".");
                    }
                    FileAttributes original = File.GetAttributes(first.Path); VirtualMember added = new VirtualMember { Path = first.Path, OriginalAttributes = (int)original, GroupId = nested == null ? null : nested.Id };
                    try { File.SetAttributes(first.Path, original | FileAttributes.Hidden); existing.Members.Add(added); DataStore.SaveVirtualLayout(layout); ExplorerDesktop.NotifyPathChanged(first.Path); }
                    catch { existing.Members.Remove(added); try { File.SetAttributes(first.Path, original); } catch { } throw; }
                    VirtualLayoutGraph.RefreshGroupAndAncestors(layout, existing.Id);
                    FolderPanel.NotifyLayoutChanged();
                    return;
                }
                FileAttributes firstAttributes = File.GetAttributes(first.Path), secondAttributes = File.GetAttributes(second.Path);
                string name = ExplorerDesktop.NewGroupName(), groupId = Guid.NewGuid().ToString("N");
                string tilePath = Path.Combine(ExplorerDesktop.Desktop, name + ".lnk");
                VirtualGroup group = new VirtualGroup { Id = groupId, Name = name, TilePath = tilePath };
                group.Members.Add(new VirtualMember { Path = second.Path, OriginalAttributes = (int)secondAttributes });
                VirtualGroup nestedFirst = first.IsGroup ? VirtualLayoutGraph.ResolveTileGroup(layout, first.Path) : null;
                if (nestedFirst != null && VirtualLayoutGraph.ParentsOf(layout, nestedFirst.Id).Count > 0) throw new InvalidOperationException("Collection này đã nằm trong một collection khác.");
                group.Members.Add(new VirtualMember { Path = first.Path, OriginalAttributes = (int)firstAttributes, GroupId = nestedFirst == null ? null : nestedFirst.Id });
                try
                {
                    File.SetAttributes(second.Path, secondAttributes | FileAttributes.Hidden);
                    File.SetAttributes(first.Path, firstAttributes | FileAttributes.Hidden);
                    layout.Groups.Add(group); DataStore.SaveVirtualLayout(layout); VirtualLayoutGraph.RefreshGroupAndAncestors(layout, group.Id); ExplorerDesktop.NotifyPathChanged(first.Path); ExplorerDesktop.NotifyPathChanged(second.Path);
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
            SyncStartupRegistration(true);
        }

        void SyncStartupRegistration(bool showError)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (key == null) throw new InvalidOperationException("Không thể mở khóa Windows Startup.");
                    if (Settings.StartWithWindows) key.SetValue("DesktopFolders", "\"" + Application.ExecutablePath + "\" --startup");
                    else key.DeleteValue("DesktopFolders", false);
                }
            }
            catch { if (showError) MessageBox.Show("Windows không cho phép thay đổi Startup.", "Desktop Folders"); }
        }

        protected override void ExitThreadCore()
        {
            exiting = true;
            if (scanner != null) scanner.Dispose(); if (pointerMonitor != null) pointerMonitor.Dispose(); if (hoverArm != null) hoverArm.Dispose(); if (refreshAfterWindowsDrop != null) refreshAfterWindowsDrop.Dispose(); if (commandPump != null) commandPump.Dispose(); if (commandWindow != null) commandWindow.Dispose();
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
            if (args != null && args.Length >= 5 && args[0] == "--test-place-pair")
            {
                Point preferred = new Point(Int32.Parse(args[3]), Int32.Parse(args[4])); bool firstPlaced = false, secondPlaced = false;
                for (int attempt = 0; attempt < 15 && !firstPlaced; attempt++) { ExplorerDesktop.NotifyPathChanged(args[1]); firstPlaced = ExplorerDesktop.TryPlaceAtNearestFreeSlot(args[1], preferred); if (!firstPlaced) Thread.Sleep(60); }
                for (int attempt = 0; attempt < 15 && !secondPlaced; attempt++) { ExplorerDesktop.NotifyPathChanged(args[2]); secondPlaced = ExplorerDesktop.TryPlaceAtNearestFreeSlot(args[2], preferred); if (!secondPlaced) Thread.Sleep(60); }
                Rectangle firstBounds, secondBounds; bool distinct = ExplorerDesktop.TryGetIconBounds(args[1], out firstBounds) && ExplorerDesktop.TryGetIconBounds(args[2], out secondBounds) && firstBounds.Location != secondBounds.Location;
                Environment.Exit(firstPlaced && secondPlaced && distinct ? 0 : 5); return;
            }
            if (args != null && args.Length >= 4 && args[0] == "--test-place")
            {
                string testPath = args[1]; Point preferred = new Point(Int32.Parse(args[2]), Int32.Parse(args[3])); bool placed = false;
                for (int attempt = 0; attempt < 15 && !placed; attempt++) { ExplorerDesktop.NotifyPathChanged(testPath); placed = ExplorerDesktop.TryPlaceAtNearestFreeSlot(testPath, preferred); if (!placed) Thread.Sleep(60); }
                Environment.Exit(placed ? 0 : 4); return;
            }
            if (args != null && args.Length >= 1 && args[0] == "--test-layout-graph")
            {
                VirtualGroup a = new VirtualGroup { Id = "a", Name = "A", TilePath = "A.lnk" };
                VirtualGroup b = new VirtualGroup { Id = "b", Name = "B", TilePath = "B.lnk" };
                VirtualGroup c = new VirtualGroup { Id = "c", Name = "C", TilePath = "C.lnk" };
                a.Members.Add(new VirtualMember { Path = "B.lnk" }); b.Members.Add(new VirtualMember { Path = "C.lnk", GroupId = "c" });
                VirtualLayout graph = new VirtualLayout(); graph.Groups.Add(a); graph.Groups.Add(b); graph.Groups.Add(c); VirtualLayoutGraph.Normalize(graph);
                bool valid = a.Members[0].GroupId == "b" && VirtualLayoutGraph.ContainsGroup(graph, "a", "c") && VirtualLayoutGraph.WouldCreateCycle(graph, "a", "c") && !VirtualLayoutGraph.WouldCreateCycle(graph, "c", "a");
                VirtualLayoutGraph.ReplaceTilePath(graph, "b", "B.lnk", "B2.lnk"); valid = valid && a.Members[0].Path == "B2.lnk";
                Environment.Exit(valid ? 0 : 3); return;
            }
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
            bool startupLaunch = args != null && args.Any(arg => arg.Equals("--startup", StringComparison.OrdinalIgnoreCase));
            bool showSettingsOnLaunch = !startupLaunch && String.IsNullOrEmpty(requestedGroupId);
            string mutexName = "DesktopFolders.Direct.SingleInstance";
#if TEST
            mutexName = "DesktopFolders.Direct.TestInstance";
#endif
            using (Mutex instance = new Mutex(true, mutexName, out created))
            {
                if (!created)
                {
                    if (!String.IsNullOrEmpty(requestedGroupId))
                    {
                        if (!SingleInstanceCommandWindow.SendOpenGroup(requestedGroupId)) DataStore.SaveOpenRequest(requestedGroupId);
                    }
                    else if (showSettingsOnLaunch) SingleInstanceCommandWindow.SendActivate();
                    return;
                }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new DirectDesktopController(requestedGroupId, showSettingsOnLaunch));
            }
        }
    }
}
