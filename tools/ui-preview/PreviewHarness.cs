// Optional .NET Framework developer harness. Never invokes Program.Main or a controller.
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class PreviewHarness
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly application;
    static string runDirectory;
    static string fixtureTile;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            try { SetProcessDPIAware(); } catch { }
            if (args.Length == 0)
            {
                string baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                args = new string[] { "--assembly", Path.Combine(baseDirectory, "DesktopFolders-preview.exe"), "--output", baseDirectory, "--show", "compact", "--language", "vi" };
            }
            Dictionary<string, string> options = ParseOptions(args);
            string assemblyPath = Path.GetFullPath(Required(options, "assembly"));
            string outputRoot = Path.GetFullPath(Required(options, "output"));
            string mode = options.ContainsKey("show") ? options["show"] : "compact";
            string language = options.ContainsKey("language") ? options["language"] : "en";
            if (language != "en" && language != "vi") throw new ArgumentException("Language must be en or vi.");
            ValidateMode(mode);
            if (!File.Exists(assemblyPath)) throw new FileNotFoundException("Build the app first.", assemblyPath);
            runDirectory = Path.Combine(outputRoot, "run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(runDirectory);
            File.WriteAllText(Path.Combine(runDirectory, "PREVIEW-FIXTURE.txt"), "Disposable UI preview only. No original files are included.\r\n", Encoding.UTF8);
            application = Assembly.LoadFrom(assemblyPath);
            RedirectDataStore();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Console.WriteLine("PREVIEW_OUTPUT=" + runDirectory);
            if (options.ContainsKey("render-all"))
            {
                foreach (string lang in new string[] { "en", "vi" })
                {
                    foreach (string scenario in new string[] { "settings", "compact", "expanded", "list", "empty", "nomatch" })
                        using (Form form = CreatePreview(scenario, lang)) Render(form, lang + "-" + scenario);
                    VerifyInteractions(lang);
                }
            }
            else
            {
                using (Form form = CreatePreview(mode, language))
                {
                    form.Shown += delegate { form.BeginInvoke(new Action(delegate { Capture(form, language + "-" + mode); })); };
                    Application.Run(form);
                }
            }
            Console.WriteLine("Preview completed; fixture directory retained for inspection.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.ToString());
            return 1;
        }
    }

    static Dictionary<string, string> ParseOptions(string[] args)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--render-all") { result.Add("render-all", "true"); continue; }
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 == args.Length) throw new ArgumentException("Expected --assembly <exe> --output <directory> [--render-all | --show <mode> --language <en|vi>].");
            string key = args[i].Substring(2);
            if (key != "assembly" && key != "output" && key != "show" && key != "language") throw new ArgumentException("Unknown option: " + args[i]);
            result.Add(key, args[++i]);
        }
        return result;
    }

    static string Required(Dictionary<string, string> options, string name)
    {
        string value;
        if (!options.TryGetValue(name, out value) || String.IsNullOrWhiteSpace(value)) throw new ArgumentException("Missing --" + name);
        return value;
    }

    static void ValidateMode(string mode)
    {
        if (Array.IndexOf(new string[] { "settings", "compact", "expanded", "list", "empty", "nomatch" }, mode) < 0)
            throw new ArgumentException("Mode must be settings, compact, expanded, list, empty, or nomatch.");
    }

    static Type AppType(string name) { return application.GetType("DesktopFoldersDirect." + name, true); }
    static object New(string name, params object[] args) { return Activator.CreateInstance(AppType(name), Members, null, args, null); }
    static object Get(object target, string field) { return target.GetType().GetField(field, Members).GetValue(target); }
    static void Set(object target, string field, object value) { target.GetType().GetField(field, Members).SetValue(target, value); }
    static object Call(object target, string method, params object[] args) { return target.GetType().GetMethod(method, Members).Invoke(target, args); }

    static void RedirectDataStore()
    {
        Type store = AppType("DataStore");
        // Run only the field initializers, then replace every path before any store method is used/JITted.
        RuntimeHelpers.RunClassConstructor(store.TypeHandle);
        string data = Path.Combine(runDirectory, "data");
        Dictionary<string, string> redirects = new Dictionary<string, string> {
            { "DataDirectory", data }, { "SettingsPath", Path.Combine(data, "settings.json") },
            { "VirtualLayoutPath", Path.Combine(data, "virtual-layout.json") },
            { "DragDiagnosticPath", Path.Combine(data, "drag-diagnostic.log") },
            { "OpenRequestDirectory", Path.Combine(data, "open-requests") }
        };
        foreach (KeyValuePair<string, string> path in redirects)
        {
            FieldInfo field = store.GetField(path.Key, Members);
            if (field == null || field.FieldType != typeof(string)) throw new InvalidOperationException("DataStore changed: " + path.Key);
            field.SetValue(null, path.Value);
            if (!String.Equals((string)field.GetValue(null), path.Value, StringComparison.Ordinal)) throw new InvalidOperationException("Path redirection failed: " + path.Key);
        }
        foreach (FieldInfo field in store.GetFields(Members))
        {
            if (!field.IsStatic || field.FieldType != typeof(string)) continue;
            string value = field.GetValue(null) as string;
            if (!String.IsNullOrEmpty(value) && Path.IsPathRooted(value) && !value.StartsWith(runDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unisolated DataStore path: " + field.Name);
        }
        Directory.CreateDirectory(data);
    }

    static Form CreatePreview(string mode, string language)
    {
        AppType("Loc").GetMethod("SetLanguage", Members).Invoke(null, new object[] { language });
        object settings = New("AppSettings");
        Set(settings, "ReduceMotion", true);
        Set(settings, "DissolveSingleAppGroup", false);
        Set(settings, "Language", language);
        WriteFixture(language, mode == "empty");
        Form form;
        if (mode == "settings")
        {
            // Null controller deliberately makes Apply/Refresh incapable of registry or desktop changes.
            AppType("ExplorerDesktop").GetField("ScanStatus", Members).SetValue(null, language == "vi" ? "Bản xem trước giao diện · Dữ liệu minh họa\r\n12 biểu tượng · 3 mục đã ghim" : "UI preview · Sample data\r\n12 icons · 3 pinned items");
            form = (Form)New("SettingsForm", new object[] { null });
            ((ComboBox)Get(form, "langCombo")).SelectedIndex = language == "vi" ? 2 : 1;
        }
        else
        {
            form = (Form)New("FolderPanel", fixtureTile, new Rectangle(80, 80, 80, 80), settings);
            GuardControl(form);
            // Disable title commits, whose production path updates Shell shortcuts.
            RemoveControlEvent((Control)Get(form, "titleLabel"), "EventClick");
            RemoveControlEvent((Control)Get(form, "titleEditor"), "EventLeave");
            RemoveControlEvent((Control)Get(form, "titleEditor"), "EventKeyDown");
            ((TextBox)Get(form, "titleEditor")).ReadOnly = true;
            RenameKeyFilter filter = new RenameKeyFilter(form);
            Application.AddMessageFilter(filter);
            form.Disposed += delegate { Application.RemoveMessageFilter(filter); };
            if (mode == "expanded") Call(form, "ToggleExpanded");
            if (mode == "list") { Set(form, "gridMode", false); Call(form, "UpdateModeButtons"); Call(form, "RenderApps"); }
            if (mode == "nomatch") ((TextBox)Get(form, "search")).Text = "No matching fixture 12345";
        }
        form.ShowInTaskbar = true;
        form.TopMost = false;
        form.StartPosition = FormStartPosition.Manual;
        Rectangle work = Screen.PrimaryScreen.WorkingArea;
        form.Location = new Point(work.Left + Math.Max(0, (work.Width - form.Width) / 2), work.Top + Math.Max(0, (work.Height - form.Height) / 2));
        return form;
    }

    static void WriteFixture(string language, bool empty)
    {
        string membersRoot = Path.Combine(runDirectory, "members");
        Directory.CreateDirectory(membersRoot);
        fixtureTile = Path.Combine(runDirectory, "Preview collection.lnk");
        string[] names = language == "vi" ? new string[] {
            "Visual Studio Code.lnk", "Tài liệu thiết kế giao diện và trải nghiệm người dùng.pdf", "Báo cáo tháng chín.xlsx",
            "Figma.lnk", "Hình nền bộ sưu tập.png", "Microsoft Edge.lnk", "Ghi chú cuộc họp.txt", "Ứng dụng kiểm thử.exe",
            "Kế hoạch phát triển DesktopFolders quý tiếp theo.docx", "Nhạc thư giãn.mp3", "DirectDesktopFolders.cs", "Bản trình bày dự án.pptx"
        } : new string[] {
            "Visual Studio Code.lnk", "Interface design and user experience reference guide.pdf", "September report.xlsx",
            "Figma.lnk", "Collection wallpaper.png", "Microsoft Edge.lnk", "Meeting notes.txt", "Test application.exe",
            "DesktopFolders development roadmap for the next quarter.docx", "Focus music.mp3", "DirectDesktopFolders.cs", "Project presentation.pptx"
        };
        List<object> members = new List<object>();
        List<string> pinned = new List<string>();
        if (!empty) for (int i = 0; i < names.Length; i++)
        {
            string path = Path.Combine(membersRoot, names[i]);
            // Inert local placeholders: no shortcuts are registered or executable payloads written.
            if (!File.Exists(path)) File.WriteAllBytes(path, new byte[0]);
            members.Add(new { Path = path, OriginalAttributes = (int)File.GetAttributes(path), OriginalShellVisibility = -1 });
            if (i < 3) pinned.Add(path);
        }
        object layout = new { Version = 4, Groups = new object[] { new {
            Id = "ui-preview-fixture", Name = language == "vi" ? "Không gian làm việc sáng tạo" : "Creative workspace collection",
            TilePath = fixtureTile, IconPath = Path.Combine(runDirectory, "Preview.ico"), Members = members, Pinned = pinned
        } } };
        string layoutPath = (string)AppType("DataStore").GetField("VirtualLayoutPath", Members).GetValue(null);
        File.WriteAllText(layoutPath, Json.Serialize(layout), Encoding.UTF8);
        // Exercise the real loader and verify it read the redirected fixture, before constructing UI.
        object loaded = AppType("DataStore").GetMethod("LoadVirtualLayout", Members).Invoke(null, null);
        IList groups = (IList)Get(loaded, "Groups");
        if (groups.Count != 1 || (string)Get(groups[0], "Id") != "ui-preview-fixture") throw new InvalidOperationException("Fixture isolation check failed.");
    }

    static void GuardControl(Control control)
    {
        control.AllowDrop = false;
        control.ControlAdded += delegate(object sender, ControlEventArgs e) { GuardControl(e.Control); };
        if (control.GetType().Name == "AppTile")
        {
            // Preserve painting, hover, focus and scrolling; remove actions that can escape a preview.
            Set(control, "ItemActivated", null);
            RemoveControlEvent(control, "EventMouseMove");
            ContextMenuStrip menu = Get(control, "immediateContextMenu") as ContextMenuStrip;
            Call(control, "SetImmediateContextMenu", new object[] { null });
            if (menu != null) menu.Dispose();
        }
        foreach (Control child in control.Controls) GuardControl(child);
    }

    static void RemoveControlEvent(Control control, string keyName)
    {
        FieldInfo field = typeof(Control).GetField(keyName, BindingFlags.NonPublic | BindingFlags.Static);
        PropertyInfo property = typeof(Component).GetProperty("Events", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null || property == null) throw new InvalidOperationException("Unsupported WinForms event implementation: " + keyName);
        object key = field.GetValue(null);
        EventHandlerList events = (EventHandlerList)property.GetValue(control, null);
        Delegate current = events[key];
        if (current != null) events.RemoveHandler(key, current);
    }

    sealed class RenameKeyFilter : IMessageFilter
    {
        readonly Form owner;
        internal RenameKeyFilter(Form form) { owner = form; }
        public bool PreFilterMessage(ref Message message)
        {
            if ((message.Msg != 0x100 && message.Msg != 0x104) || (Keys)message.WParam.ToInt32() != Keys.F2) return false;
            Control target = Control.FromChildHandle(message.HWnd);
            return target != null && target.FindForm() == owner;
        }
    }

    static void Render(Form form, string name)
    {
        form.Show();
        Application.DoEvents();
        form.PerformLayout();
        form.Refresh();
        Application.DoEvents();
        Capture(form, name);
        form.Close();
        Application.DoEvents();
    }

    static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }

    static void Require(bool result, string check)
    {
        if (!result) throw new InvalidOperationException("UI verification failed: " + check);
        Console.WriteLine("PASS " + check);
    }

    static ulong RenderSignature(Control control)
    {
        using (Bitmap image = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height)))
        {
            control.DrawToBitmap(image, control.ClientRectangle); return BitmapSignature(image);
        }
    }

    static ulong BitmapSignature(Bitmap image)
    {
        ulong hash = 1469598103934665603UL;
        for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++) { hash ^= unchecked((uint)image.GetPixel(x, y).ToArgb()); hash *= 1099511628211UL; }
        return hash;
    }

    static void DirectPaint(Control control, Bitmap image)
    {
        using (Graphics graphics = Graphics.FromImage(image))
        using (PaintEventArgs paint = new PaintEventArgs(graphics, control.ClientRectangle)) Call(control, "OnPaint", paint);
    }

    static bool TransitionClears(Control control, Action firstState, Action finalState)
    {
        using (Bitmap stacked = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height)))
        using (Bitmap clean = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height)))
        {
            using (Graphics graphics = Graphics.FromImage(stacked)) graphics.Clear(control.BackColor); firstState(); DirectPaint(control, stacked); finalState(); DirectPaint(control, stacked);
            using (Graphics graphics = Graphics.FromImage(clean)) graphics.Clear(control.BackColor); DirectPaint(control, clean);
            return BitmapSignature(stacked) == BitmapSignature(clean);
        }
    }

    static bool StableRender(Control control, int repaintCount)
    {
        ulong signature = RenderSignature(control);
        for (int repaint = 0; repaint < repaintCount; repaint++) { control.Invalidate(); control.Update(); if (RenderSignature(control) != signature) return false; }
        return true;
    }

    static void VerifyInteractions(string language)
    {
        using (Form form = CreatePreview("compact", language))
        {
            form.Show(); Application.DoEvents();
            TextBox search = (TextBox)Get(form, "search");
            Require(search.Focused, language + ": search receives opening focus");
            Require(search.Parent.Height == 40 && search.Parent.Width >= form.ClientSize.Width - 50, language + ": compact search proportions");
            Require(Math.Abs((float)search.Parent.GetType().GetProperty("BorderThickness", Members).GetValue(search.Parent, null) - 2f) < .01f && (int)search.Parent.GetType().GetProperty("Radius", Members).GetValue(search.Parent, null) == 12, language + ": restored search geometry with focused color");
            Require(System.Linq.Enumerable.All(Descendants(form), item => item.GetType().Name != "HeaderIconButton" || (item.Parent != null && item.Parent.ClientRectangle.Contains(item.Bounds))), language + ": collection buttons stay inside their containers");
            Control actionGroup = (Control)Get(form, "modes"); Rectangle actionBounds = form.RectangleToClient(actionGroup.RectangleToScreen(actionGroup.ClientRectangle)); Rectangle searchBounds = form.RectangleToClient(search.Parent.RectangleToScreen(search.Parent.ClientRectangle));
            Require(actionGroup.Size == new Size(140, 44) && System.Linq.Enumerable.Count(Descendants(actionGroup), item => item.GetType().Name == "HeaderIconButton" && item.Size == new Size(32, 36)) == 4 && searchBounds.Top - actionBounds.Bottom >= 6, language + ": four collection actions have a framed group separated from search");
            Require(Math.Abs(actionBounds.Right - searchBounds.Right) <= 1, language + ": action strip right edge aligns with search bar");
            Control title = (Control)Get(form, "titleLabel"); Rectangle titleBounds = form.RectangleToClient(title.RectangleToScreen(title.ClientRectangle));
            Require(titleBounds.Right <= actionBounds.Left && Math.Abs((titleBounds.Top + titleBounds.Height / 2) - (actionBounds.Top + actionBounds.Height / 2)) <= 1, language + ": title aligns with actions without overlap");
            System.Drawing.Drawing2D.ColorBlend titleBlend = (System.Drawing.Drawing2D.ColorBlend)AppType("CollectionTheme").GetMethod("TitleBlend", Members).Invoke(null, new object[] { 255 });
            Require(titleBlend.Colors.Length == 2 && titleBlend.Colors[0].R == 192 && titleBlend.Colors[0].G == 120 && titleBlend.Colors[0].B == 255 && titleBlend.Colors[1].R == 103 && titleBlend.Colors[1].G == 199 && titleBlend.Colors[1].B == 255, language + ": title uses saturated purple and blue");
            Require((bool)search.Parent.GetType().GetProperty("ContinuousPerimeterGradient", Members).GetValue(search.Parent, null), language + ": search uses continuous perimeter colors");
            Require(!Descendants(form).Any(item => String.Equals(item.Text, "Ctrl K", StringComparison.OrdinalIgnoreCase)), language + ": reference shortcut chip is absent");
            foreach (Control action in Descendants(actionGroup).Where(item => item.GetType().Name == "HeaderIconButton"))
            {
                Require(StableRender(action, 20), language + ": stable repeated paint for " + action.AccessibleName);
                Set(action, "hot", true); action.Invalidate(); Require(StableRender(action, 20), language + ": stable hover paint for " + action.AccessibleName);
                Set(action, "pressed", true); action.Invalidate(); Require(StableRender(action, 20), language + ": stable pressed paint for " + action.AccessibleName);
                Set(action, "pressed", false); Set(action, "hot", false); action.Enabled = false; action.Invalidate(); Require(StableRender(action, 20), language + ": stable disabled paint for " + action.AccessibleName); action.Enabled = true;
                Require(TransitionClears(action, delegate { Set(action, "hot", true); Set(action, "pressed", true); }, delegate { Set(action, "pressed", false); Set(action, "hot", false); }), language + ": state transition clears old pixels for " + action.AccessibleName);
            }
            Control expandTransition = (Control)Get(form, "expandButton"); PropertyInfo iconKind = expandTransition.GetType().GetProperty("IconKind", Members); object originalKind = iconKind.GetValue(expandTransition, null), expandKind = Enum.Parse(iconKind.PropertyType, "Expand"), collapseKind = Enum.Parse(iconKind.PropertyType, "Collapse");
            Require(TransitionClears(expandTransition, delegate { iconKind.SetValue(expandTransition, expandKind, null); }, delegate { iconKind.SetValue(expandTransition, collapseKind, null); }), language + ": expand-to-collapse transition clears the previous glyph"); iconKind.SetValue(expandTransition, originalKind, null);
            Control content = search.Parent.Parent; Rectangle beforeMove = form.Bounds;
            Require(content.Cursor == Cursors.SizeAll && search.Cursor == Cursors.IBeam, language + ": background and search retain distinct interaction surfaces");
            Rectangle workingArea = Screen.FromControl(form).WorkingArea; int dragX = beforeMove.Left - workingArea.Left > 40 ? -30 : 30, dragY = beforeMove.Top - workingArea.Top > 40 ? -24 : 24;
            form.Location = new Point(beforeMove.Left + dragX, beforeMove.Top + dragY); Call(form, "RecordTemporaryPosition", beforeMove.Location); Application.DoEvents();
            Require(form.Location != beforeMove.Location && (bool)Get(form, "temporarilyPositioned"), language + ": native background move records a temporary collection position");
            Rectangle manuallyMoved = form.Bounds; Call(form, "QueueAnchorUpdate", new Rectangle(beforeMove.Left + 4, beforeMove.Top + 4, 64, 64)); Application.DoEvents(); Require(form.Bounds == manuallyMoved, language + ": live anchor refresh does not cancel temporary position");
            Control firstTile = System.Linq.Enumerable.First(Descendants(form), item => item.GetType().Name == "AppTile");
            Panel viewportGeometry = (Panel)Get(form, "appsViewport"); Control sectionGeometry = System.Linq.Enumerable.First(Descendants(form), item => item.GetType().Name == "SectionHeaderControl");
            Require(firstTile.Height == 106 && viewportGeometry.Top == 54 && sectionGeometry.Height == 28, language + ": tile and content spacing keep baseline geometry");
            Call(firstTile, "OnMouseEnter", EventArgs.Empty); Application.DoEvents(); Capture(form, language + "-tile-hover"); Call(firstTile, "OnMouseLeave", EventArgs.Empty);
            Control closeButton = System.Linq.Enumerable.First(Descendants(form), item => item.GetType().Name == "HeaderIconButton" && item.AccessibleName == (string)AppType("Loc").GetMethod("Get", Members, null, new Type[] { typeof(string), typeof(object[]) }, null).Invoke(null, new object[] { "panel.close_accessible", new object[0] }));
            Set(closeButton, "hot", true); closeButton.Invalidate(); Application.DoEvents(); Capture(form, language + "-button-hover");
            Set(closeButton, "pressed", true); closeButton.Invalidate(); Application.DoEvents(); Capture(form, language + "-button-pressed");
            Set(closeButton, "pressed", false); Set(closeButton, "hot", false); closeButton.Enabled = false; closeButton.Invalidate(); Application.DoEvents(); Capture(form, language + "-button-disabled"); closeButton.Enabled = true;
            Cursor.Position = new Point(Math.Max(0, form.Left - 20), Math.Max(0, form.Top - 20)); firstTile.Focus(); Application.DoEvents(); Require(Math.Abs((float)search.Parent.GetType().GetProperty("BorderThickness", Members).GetValue(search.Parent, null) - 2f) < .01f, language + ": idle search keeps original border geometry"); Capture(form, language + "-search-idle");
            Call(search, "OnMouseEnter", EventArgs.Empty); Require(Math.Abs((float)search.Parent.GetType().GetProperty("BorderThickness", Members).GetValue(search.Parent, null) - 2f) < .01f, language + ": hover changes no search geometry"); Application.DoEvents(); Capture(form, language + "-search-hover"); Call(search, "OnMouseLeave", EventArgs.Empty);
            Control scrollBar = System.Linq.Enumerable.First(Descendants(form), item => item.GetType().Name == "DarkScrollBar");
            Rectangle idleThumb = (Rectangle)Call(scrollBar, "ThumbRectangle"); Require(idleThumb.Width == 4, language + ": idle scrollbar thumb width");
            Call(scrollBar, "OnMouseEnter", EventArgs.Empty); Application.DoEvents(); Rectangle activeThumb = (Rectangle)Call(scrollBar, "ThumbRectangle"); Require(activeThumb.Width == 5, language + ": active scrollbar thumb width"); Capture(form, language + "-scroll-hover"); Call(scrollBar, "OnMouseLeave", EventArgs.Empty);
            Control list = (Control)Get(form, "listButton"), grid = (Control)Get(form, "gridButton");
            list.Focus(); Call(list, "OnKeyDown", new KeyEventArgs(Keys.Space)); Call(list, "OnKeyUp", new KeyEventArgs(Keys.Space)); Application.DoEvents();
            Require(!(bool)Get(form, "gridMode") && (list.AccessibilityObject.State & AccessibleStates.Pressed) != 0, language + ": list selection is exposed");
            Call(grid, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, grid.Width / 2, grid.Height / 2, 0)); Call(grid, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, grid.Width / 2, grid.Height / 2, 0)); Application.DoEvents();
            Require((bool)Get(form, "gridMode"), language + ": grid selection");
            Set(grid, "hot", true); Set(grid, "pressed", true); grid.Invalidate(); Application.DoEvents(); Capture(form, language + "-selected-button-pressed"); Set(grid, "pressed", false); Set(grid, "hot", false); grid.Invalidate();
            Call(form, "ToggleExpanded"); Application.DoEvents();
            Control expand = (Control)Get(form, "expandButton");
            Require(search.Parent.Height == 40 && search.Parent.Width >= form.ClientSize.Width - 50 && (bool)Get(form, "expanded") && (bool)search.Parent.GetType().GetProperty("ContinuousPerimeterGradient", Members).GetValue(search.Parent, null), language + ": expanded search keeps geometry and continuous perimeter colors");
            Require(expand.GetType().GetProperty("IconKind", Members).GetValue(expand, null).ToString() == "Collapse", language + ": collapse button rendering state");
            Call(form, "ToggleExpanded"); Application.DoEvents();
            search.Text = "12345-absent"; Application.DoEvents();
            Require(!System.Linq.Enumerable.Any(Descendants(form), item => item.GetType().Name == "AppTile"), language + ": search filters items");
            foreach (Control child in Descendants(form))
                if (child is Button && child.Text == (string)AppType("Loc").GetMethod("Get", Members, null, new Type[] { typeof(string), typeof(object[]) }, null).Invoke(null, new object[] { "panel.search_clear", new object[0] }))
                { ((Button)child).PerformClick(); break; }
            Require(search.TextLength == 0 && search.Focused && System.Linq.Enumerable.Count(Descendants(form), item => item.GetType().Name == "AppTile") == 12, language + ": clear search restores all fixtures");
            Control lastTile = System.Linq.Enumerable.Last(Descendants(form), item => item.GetType().Name == "AppTile");
            lastTile.Focus(); Application.DoEvents();
            Panel viewport = (Panel)Get(form, "appsViewport");
            Rectangle tileBounds = viewport.RectangleToClient(lastTile.RectangleToScreen(lastTile.ClientRectangle));
            Require(viewport.ClientRectangle.Contains(tileBounds), language + ": keyboard focus scrolls last tile into view");
            Capture(form, language + "-focused-last-item");
            form.Close(); Application.DoEvents();
        }
        using (Form form = CreatePreview("settings", language))
        {
            form.Show(); Application.DoEvents();
            Control slider = (Control)Get(form, "delaySlider");
            Call(slider, "OnKeyDown", new KeyEventArgs(Keys.Right));
            Require((int)slider.GetType().GetProperty("Value", Members).GetValue(slider, null) == 300, language + ": slider arrow step");
            Call(slider, "OnKeyDown", new KeyEventArgs(Keys.End));
            Require((int)slider.GetType().GetProperty("Value", Members).GetValue(slider, null) == 800, language + ": slider upper bound");
            Control toggle = (Control)Get(form, "startupSwitch");
            PropertyInfo checkedProperty = toggle.GetType().GetProperty("Checked", Members);
            Call(toggle, "OnMouseClick", new MouseEventArgs(MouseButtons.Right, 1, 4, 4, 0));
            Require(!(bool)checkedProperty.GetValue(toggle, null), language + ": right click leaves toggle unchanged");
            Call(toggle, "OnKeyDown", new KeyEventArgs(Keys.Space));
            Require((bool)checkedProperty.GetValue(toggle, null), language + ": Space toggles setting");
            toggle.Enabled = false; Call(toggle, "OnKeyDown", new KeyEventArgs(Keys.Space));
            Require((bool)checkedProperty.GetValue(toggle, null), language + ": disabled toggle ignores input");
            Capture(form, language + "-settings-disabled");
            toggle.Enabled = true;
            Control scroll = System.Linq.Enumerable.First(Descendants(form), item => item.GetType().Name == "DarkScrollBar");
            Control status = (Control)Get(form, "targetStatus");
            Button refresh = System.Linq.Enumerable.First(System.Linq.Enumerable.OfType<Button>(status.Parent.Controls), button => button.GetType().Name == "ModernButton");
            refresh.Focus(); Application.DoEvents();
            Panel viewport = (Panel)Get(scroll, "viewport");
            Require(viewport.ClientRectangle.Contains(viewport.RectangleToClient(refresh.RectangleToScreen(refresh.ClientRectangle))), language + ": diagnostics keyboard reachability");
            Capture(form, language + "-settings-scrolled");
            form.Height = 520; Application.DoEvents(); refresh.Focus(); Call(scroll, "SetValue", Int32.MaxValue); Application.DoEvents();
            Require(viewport.ClientRectangle.Contains(viewport.RectangleToClient(refresh.RectangleToScreen(refresh.ClientRectangle))), language + ": diagnostics remains reachable in short window");
            Capture(form, language + "-settings-short");
            form.Close(); Application.DoEvents();
        }
    }

    static void Capture(Form form, string name)
    {
        using (Bitmap bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
        {
            form.DrawToBitmap(bitmap, form.ClientRectangle);
            bitmap.Save(Path.Combine(runDirectory, name + ".png"), ImageFormat.Png);
        }
        List<object> controls = new List<object>();
        InspectBounds(form, "form", controls);
        float dpi;
        using (Graphics graphics = form.CreateGraphics()) dpi = graphics.DpiX;
        File.WriteAllText(Path.Combine(runDirectory, name + ".layout.json"), Json.Serialize(new { Scenario = name, Dpi = dpi, Width = form.ClientSize.Width, Height = form.ClientSize.Height, Controls = controls }), Encoding.UTF8);
        Console.WriteLine(name + ": " + form.ClientSize.Width + "x" + form.ClientSize.Height + " at " + dpi + " DPI");
    }

    static void InspectBounds(Control control, string path, List<object> results)
    {
        bool scrollContent = control is FlowLayoutPanel;
        bool outside = control.Parent != null && control.Visible && !scrollContent && !control.Parent.ClientRectangle.Contains(control.Bounds);
        Label label = control as Label;
        results.Add(new { Path = path, Type = control.GetType().Name, Text = control.Text, AccessibleName = control.AccessibleName,
            X = control.Left, Y = control.Top, Width = control.Width, Height = control.Height, Visible = control.Visible,
            OutsideParent = outside, IntentionalScrollContent = scrollContent, AutoEllipsis = label != null && label.AutoEllipsis });
        int index = 0;
        foreach (Control child in control.Controls) InspectBounds(child, path + "/" + index++, results);
    }
}
