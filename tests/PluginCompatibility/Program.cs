using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: PluginCompatibility <plugin-directory>");
            return 2;
        }

        string directory = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };

        try
        {
            foreach (string name in new[] { "LiquifiedPlugin", "GridWarpPlugin", "DocumentRectifyPlugin" })
            {
                Assembly plugin = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory, name + ".dll"));
                Type[] types = plugin.GetTypes();
                Type tokenType = types.Single(type => type.Name.EndsWith("ConfigToken", StringComparison.Ordinal));
                object token = Activator.CreateInstance(tokenType)!;
                foreach (PropertyInfo property in tokenType.GetProperties().Where(property => property.CanWrite && property.PropertyType.IsArray))
                {
                    if (property.GetValue(token) is Array original && original.Length > 0)
                    {
                        object arrayCloneToken = tokenType.GetMethod("Clone")!.Invoke(token, null)!;
                        if (ReferenceEquals(original, property.GetValue(arrayCloneToken)))
                        {
                            throw new InvalidOperationException(name + " shares mutable token array " + property.Name + ".");
                        }
                    }
                }
                object clone = tokenType.GetMethod("Clone")!.Invoke(token, null)!;
                if (ReferenceEquals(token, clone) || clone.GetType() != tokenType)
                {
                    throw new InvalidOperationException(name + " did not clone its token.");
                }
                Type effectType = types.Single(type => type.Name is "LiquifiedEffect" or "GridWarpEffect" or "DocumentRectifyEffect");
                using IDisposable effect = (IDisposable)Activator.CreateInstance(effectType, nonPublic: true)!;
                Type formType = types.Single(type => type.Name.EndsWith("ConfigForm", StringComparison.Ordinal));
                using Form form = (Form)Activator.CreateInstance(formType, nonPublic: true)!;
                form.WindowState = FormWindowState.Normal;
                form.CreateControl();
                if (name == "LiquifiedPlugin")
                {
                    Type modeType = types.Single(type => type.Name == "LiquifiedMode");
                    Type sharedUi = types.Single(type => type.Name == "DialogUi");
                    MethodInfo drawLock = sharedUi.GetMethod("DrawFreezeGlyph", BindingFlags.Static | BindingFlags.NonPublic)!;
                    MethodInfo createIcon = formType.GetMethod("CreateModeIcon", BindingFlags.Static | BindingFlags.NonPublic)!;
                    foreach (string modeName in new[] { "Freeze", "Unfreeze" })
                    {
                        using var actual = (System.Drawing.Bitmap)createIcon.Invoke(null,
                            new object[] { Enum.Parse(modeType, modeName), 24, System.Drawing.Color.White })!;
                        using var expected = new System.Drawing.Bitmap(24, 24);
                        using var graphics = System.Drawing.Graphics.FromImage(expected);
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1.8f)
                        {
                            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
                            StartCap = System.Drawing.Drawing2D.LineCap.Round,
                            EndCap = System.Drawing.Drawing2D.LineCap.Round
                        };
                        drawLock.Invoke(null, new object[] { graphics, pen, modeName == "Unfreeze" });
                        for (int y = 0; y < 24; y++)
                            for (int x = 0; x < 24; x++)
                                if (actual.GetPixel(x, y) != expected.GetPixel(x, y))
                                    throw new InvalidOperationException("Liquified freeze icons differ from the shared grid lock glyphs.");
                    }
                }
                string displayName = name switch
                {
                    "GridWarpPlugin" => "Grid Warpified",
                    "DocumentRectifyPlugin" => "Doc Recitified",
                    _ => "Liquified"
                };
                Type effectInterface = effectType.GetInterfaces().Single(type => type.FullName == "PaintDotNet.Effects.IEffect");
                object effectInfo = effectInterface.GetProperty("EffectInfo")!.GetValue(effect)!;
                object supportInfo = effectInfo.GetType().GetProperty("SupportInfo")!.GetValue(effectInfo)
                    ?? throw new InvalidOperationException(name + " has no Paint.NET plugin support information.");
                object? SupportProperty(string property) => supportInfo.GetType().GetProperty(property)!.GetValue(supportInfo);
                if (!Equals(SupportProperty("DisplayName"), displayName) ||
                    !Equals(SupportProperty("Author"), plugin.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company) ||
                    !Equals(SupportProperty("Copyright"), plugin.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright) ||
                    !Equals(SupportProperty("Version"), plugin.GetName().Version) ||
                    !Equals(SupportProperty("WebsiteUri"), new Uri("https://github.com/joelst/pdn-liquify5")) ||
                    string.IsNullOrWhiteSpace(SupportProperty("Author") as string))
                    throw new InvalidOperationException(name + " has incomplete or inconsistent menu-hover support information.");
                if (!Equals(effectInfo.GetType().GetProperty("Name")!.GetValue(effectInfo), displayName) ||
                    !form.Text.StartsWith(displayName, StringComparison.Ordinal) ||
                    plugin.GetCustomAttribute<AssemblyProductAttribute>()?.Product != displayName ||
                    plugin.GetCustomAttribute<AssemblyTitleAttribute>()?.Title != displayName)
                    throw new InvalidOperationException(name + " has inconsistent public branding.");
                if (name == "LiquifiedPlugin")
                {
                    foreach (string field in new[] { "showMeshCheck", "showMaskCheck", "showBackdropCheck" })
                    {
                        Control toggle = (Control)formType.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                        Control info = toggle.Parent!.Controls.Cast<Control>().Single(control => control.Name == "controlInfo");
                        if (!info.AccessibleDescription!.Contains("final", StringComparison.OrdinalIgnoreCase) ||
                            info.AccessibleName != toggle.AccessibleName + " information")
                            throw new InvalidOperationException("Liquified view toggle lacks adjacent explanatory information: " + field);
                    }
                    if ((double)tokenType.GetProperty("Strength")!.GetValue(token)! != 0.25 ||
                        (double)tokenType.GetProperty("Density")!.GetValue(token)! != 0.25)
                        throw new InvalidOperationException("Liquified factory pressure/density are not 25%.");
                    MethodInfo sync = formType.GetMethod("SyncUIFromToken", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    sync.Invoke(form, new[] { token });
                    foreach (string field in new[] { "strengthSlider", "densitySlider" })
                    {
                        TrackBar slider = (TrackBar)formType.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                        if (slider.Value != 25 || slider.Parent!.Parent!.Name != "commonSettingsBar")
                            throw new InvalidOperationException("Liquified common brush settings are not initialized on the top bar.");
                    }
                    tokenType.GetProperty("Strength")!.SetValue(token, 0.35);
                    tokenType.GetProperty("Density")!.SetValue(token, 0.2);
                    tokenType.GetProperty("MeshSize")!.SetValue(token, 48);
                    tokenType.GetProperty("ShowMesh")!.SetValue(token, true);
                    tokenType.GetProperty("SidebarCollapsed")!.SetValue(token, true);
                    sync.Invoke(form, new[] { token });
                    if (Descendants(form).OfType<ComboBox>().Any())
                        throw new InvalidOperationException("Liquified retains a redundant tools dropdown.");
                    MethodInfo selectMode = formType.GetMethod("SetSelectedMode", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    selectMode.Invoke(form, new object[] { Enum.ToObject(tokenType.GetProperty("Mode")!.PropertyType, 2), false });
                    tokenType.GetProperty("Mode")!.SetValue(token, Enum.ToObject(tokenType.GetProperty("Mode")!.PropertyType, 2));
                    object roundTrip = Activator.CreateInstance(tokenType)!;
                    formType.GetMethod("OnUpdateTokenFromDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new[] { roundTrip });
                    foreach (string property in new[] { "Strength", "Density", "MeshSize", "ShowMesh", "SidebarCollapsed", "Mode" })
                    {
                        PropertyInfo setting = tokenType.GetProperty(property)!;
                        if (!Equals(setting.GetValue(token), setting.GetValue(roundTrip)) ||
                            !Equals(setting.GetValue(token), setting.GetValue(tokenType.GetMethod("Clone")!.Invoke(token, null)!)))
                            throw new InvalidOperationException("Liquified lost token preference " + property + ".");
                    }
                    Button sidebar = (Button)formType.GetField("sidebarButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    if (sidebar.Text.Length != 0 || !sidebar.AccessibleName!.Contains("Show"))
                        throw new InvalidOperationException("Liquified collapsed sidebar cannot be reopened accessibly.");
                    formType.GetMethod("SetSidebarCollapsed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { false });
                    if (sidebar.Text.Length != 0 || !sidebar.AccessibleName!.Contains("Hide"))
                        throw new InvalidOperationException("Liquified sidebar did not reopen.");
                    Control actions = Descendants(form).Single(control => control.Name == "actionsBar");
                    if (!Descendants(actions).Contains((Control)form.AcceptButton!) ||
                        !Descendants(actions).Contains((Control)form.CancelButton!))
                        throw new InvalidOperationException("Liquified confirmation buttons depend on the collapsible sidebar.");
                    Type preferences = types.Single(type => type.Name == "LiquifiedPreferences");
                    string settingsPath = Path.Combine(Path.GetTempPath(), "liquified-preferences-" + Guid.NewGuid().ToString("N") + ".json");
                    try
                    {
                        tokenType.GetProperty("ReconstructAmount")!.SetValue(token, 0.8);
                        preferences.GetMethod("Save", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { token, settingsPath });
                        object restored = preferences.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { settingsPath })!;
                        foreach (string property in new[] { "Strength", "Density", "MeshSize", "ShowMesh", "SidebarCollapsed", "Mode" })
                        {
                            PropertyInfo setting = tokenType.GetProperty(property)!;
                            if (!Equals(setting.GetValue(token), setting.GetValue(restored)))
                                throw new InvalidOperationException("Saved defaults lost " + property + ".");
                        }
                        if ((double)tokenType.GetProperty("ReconstructAmount")!.GetValue(restored)! != 0 ||
                            ((System.Collections.IList)tokenType.GetProperty("Strokes")!.GetValue(restored)!).Count != 0 ||
                            ((Array)tokenType.GetProperty("FrozenMask")!.GetValue(restored)!).Length != 0)
                            throw new InvalidOperationException("Saved defaults included deformation or protection.");
                        File.WriteAllText(settingsPath, "{\"Strength\":5}");
                        try
                        {
                            preferences.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { settingsPath });
                            throw new InvalidOperationException("Malformed defaults were silently accepted.");
                        }
                        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { }
                        File.Delete(settingsPath);
                        MethodInfo startup = preferences.GetMethod("LoadStartup", BindingFlags.Static | BindingFlags.NonPublic)!;
                        string missing = settingsPath + ".missing";
                        object factory = startup.Invoke(null, new object[] { missing, settingsPath })!;
                        if ((double)tokenType.GetProperty("Strength")!.GetValue(factory)! != 0.25)
                            throw new InvalidOperationException("Missing last-used/default preferences did not preserve factory defaults.");
                        preferences.GetMethod("Save", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { token, settingsPath });
                        object automatic = startup.Invoke(null, new object[] { missing, settingsPath })!;
                        if ((double)tokenType.GetProperty("Strength")!.GetValue(automatic)! != 0.35)
                            throw new InvalidOperationException("Automatic last-used preferences were not loaded.");
                    }
                    finally { File.Delete(settingsPath); }
                    var rail = (FlowLayoutPanel)formType.GetField("modeButtonsPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    if (rail.Name != "modeRail" || rail.Parent != form || rail.Controls.Count != 10 || !rail.AutoScroll)
                        throw new InvalidOperationException("Liquified mode icons are not in the persistent scrollable rail.");
                    MethodInfo shortcut = formType.GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    for (int i = 0; i < 10; i++)
                    {
                        Keys key = Keys.Alt | (i == 9 ? Keys.D0 : Keys.D1 + i);
                        object[] shortcutArgs = { new Message(), key };
                        if (!(bool)shortcut.Invoke(form, shortcutArgs)! ||
                            Convert.ToInt32(formType.GetField("selectedMode", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)) != i ||
                            !rail.Controls[i].AccessibleDescription!.Contains("Alt+" + ((i + 1) % 10)))
                            throw new InvalidOperationException("Liquified mode shortcut or accessible hint is incorrect.");
                    }
                    foreach (Button action in Descendants(form).OfType<Button>().Where(button =>
                        button.AccessibleName is "Mask None" or "Mask All" or "Invert Mask" or "Reconstruct All" or "Restore All" or "Undo" or "Redo"))
                    {
                        ToolTip tips = (ToolTip)formType.GetField("uiToolTip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                        if (action.Image is null || string.IsNullOrWhiteSpace(tips.GetToolTip(action)) ||
                            string.IsNullOrWhiteSpace(action.AccessibleDescription))
                            throw new InvalidOperationException("Liquified action has no icon, tooltip, or accessible description.");
                    }
                    MethodInfo disposeEffects = effectType.GetMethod("DisposeEffects", BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(typeof(IDisposable));
                    foreach (bool partial in new[] { false, true })
                    {
                        TrackingDisposable first = new();
                        TrackingDisposable last = new();
                        object?[] parameters = { new IDisposable?[] { first, partial ? null : new TrackingDisposable(), last } };
                        disposeEffects.Invoke(null, parameters);
                        if (parameters[0] is not null || first.DisposeCount != 1 || last.DisposeCount != 1)
                            throw new InvalidOperationException("Liquified did not dispose and clear its complete or partial effect array.");
                        disposeEffects.Invoke(null, parameters);
                        if (first.DisposeCount != 1 || last.DisposeCount != 1)
                            throw new InvalidOperationException("Liquified disposed effects twice after clearing the array.");
                    }
                    Type icons = types.Single(type => type.Name == "EffectMenuIcons");
                    string iconName = name == "LiquifiedPlugin" ? "Liquified" : name == "GridWarpPlugin" ? "GridWarp" : "DocumentRectify";
                    System.Drawing.Bitmap icon = (System.Drawing.Bitmap)icons.GetProperty(iconName, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                    if (icon.Width != 32 || icon.Height != 32 || icon.GetPixel(0, 0).A != 0)
                        throw new InvalidOperationException(name + " menu icon is missing or lacks a transparent background.");
                }
                if (name == "DocumentRectifyPlugin")
                {
                    PropertyInfo aspectMode = tokenType.GetProperty("AspectMode")!;
                    Type modeType = aspectMode.PropertyType;
                    int expected = Convert.ToInt32(Enum.Parse(modeType, "PreserveDetected"));
                    ComboBox aspectCombo = (ComboBox)formType.GetField("aspectCombo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    foreach (int value in new[] { -1, 999 }.Concat(Enum.GetValues(modeType).Cast<object>().Select(Convert.ToInt32)))
                    {
                        aspectMode.SetValue(token, Enum.ToObject(modeType, value));
                        formType.GetMethod("OnUpdateDialogFromToken", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(form, new[] { token });
                        int selected = Enum.IsDefined(modeType, value) ? value : expected;
                        if (aspectCombo.SelectedIndex != selected)
                            throw new InvalidOperationException("Doc Recitified changed the valid mode or used an inconsistent malformed-mode fallback.");
                    }
                }
                if (name is "GridWarpPlugin" or "DocumentRectifyPlugin")
                {
                    formType.GetMethod("OnUpdateDialogFromToken", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(form, new[] { token });
                    Control canvas = (Control)formType.GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    ToolTip tooltip = (ToolTip)formType.GetField("uiToolTip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    string help = Descendants(form).Single(control =>
                        control.Name == "controlInfo" && control.AccessibleDescription!.Contains("F6:", StringComparison.Ordinal)).AccessibleDescription!;
                    foreach (string guidance in new[] { form.Text, help, tooltip.GetToolTip(canvas) ?? string.Empty })
                    {
                        if (!guidance.Contains("Ctrl+Page Up/Down", StringComparison.Ordinal) ||
                            !guidance.Contains("Tab", StringComparison.Ordinal) ||
                            !guidance.Contains("focus", StringComparison.OrdinalIgnoreCase) ||
                            guidance.Contains("Tab-select", StringComparison.OrdinalIgnoreCase) ||
                            guidance.Contains("Tab: select", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException(name + " has stale or incomplete visible keyboard guidance.");
                    }
                    MethodInfo isInputKey = canvas.GetType().GetMethod("IsInputKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    MethodInfo keyDown = formType.GetMethod("Canvas_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    foreach (Keys keys in new[] { Keys.Tab, Keys.Shift | Keys.Tab })
                    {
                        if ((bool)isInputKey.Invoke(canvas, new object[] { keys })!)
                            throw new InvalidOperationException(name + " consumes focus traversal keys.");
                        KeyEventArgs tab = new(keys);
                        keyDown.Invoke(form, new object[] { canvas, tab });
                        if (tab.Handled || tab.SuppressKeyPress)
                            throw new InvalidOperationException(name + " traps Tab in its canvas handler.");
                    }
                    KeyEventArgs next = new(Keys.Control | Keys.PageDown);
                    keyDown.Invoke(form, new object[] { canvas, next });
                    FieldInfo selectedIndex = formType.GetField(
                        name == "GridWarpPlugin" ? "selectedGridPointIndex" : "selectedCornerIndex",
                        BindingFlags.Instance | BindingFlags.NonPublic)!;
                    if ((int)selectedIndex.GetValue(form)! != 1)
                        throw new InvalidOperationException(name + " did not select the next point.");
                    KeyEventArgs previous = new(Keys.Control | Keys.PageUp);
                    keyDown.Invoke(form, new object[] { canvas, previous });
                    if ((int)selectedIndex.GetValue(form)! != 0 || !next.Handled || !previous.Handled ||
                        !canvas.AccessibleDescription!.Contains("Tab") || !canvas.AccessibleDescription.Contains("F6"))
                        throw new InvalidOperationException(name + " is missing accessible point navigation or escape guidance.");
                }
                if (name == "GridWarpPlugin")
                {
                    if (Descendants(form).OfType<Button>().Any(button =>
                        button.Text is "Freeze Selected" or "Unfreeze Selected" or "Link Selected" or "Unlink Selected") ||
                        Descendants(form).OfType<Button>().Count(button => button.Name.StartsWith("pointMode", StringComparison.Ordinal)) != 5)
                        throw new InvalidOperationException("Grid Warpified has duplicate constraint workflows or missing mode buttons.");
                    Control toolbar = Descendants(form).Single(control => control.Name == "commonSettingsBar");
                    ComboBox[] dropdowns = new[] { "gridWidthCombo", "gridHeightCombo", "qualityCombo", "gridColorCombo" }
                        .Select(field => (ComboBox)formType.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!).ToArray();
                    if (dropdowns.Any(combo => combo.DropDownStyle != ComboBoxStyle.DropDownList || !Descendants(toolbar).Contains(combo)) ||
                        dropdowns[0].Items.Count != 49 || dropdowns[1].Items.Count != 49 || dropdowns[2].Items.Count != 7)
                        throw new InvalidOperationException("Grid Warpified top-bar dropdowns omit supported values.");
                    Button gridToggle = (Button)formType.GetField("showGridButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    if (!Descendants(toolbar).Contains(gridToggle))
                        throw new InvalidOperationException("Grid visibility toggle is not on the top bar.");
                    MethodInfo toggleClick = typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    toggleClick.Invoke(gridToggle, new object[] { EventArgs.Empty });
                    if ((bool)formType.GetField("isGridVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)! ||
                        gridToggle.AccessibleName != "Show grid" || gridToggle.Text.Length != 0)
                        throw new InvalidOperationException("Grid visibility toggle does not hide the overlay.");
                    toggleClick.Invoke(gridToggle, new object[] { EventArgs.Empty });
                    if (gridToggle.AccessibleName != "Hide grid" ||
                        !(bool)gridToggle.GetType().GetProperty("IsSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(gridToggle)!)
                        throw new InvalidOperationException("Grid icon toggle does not restore its selected state.");
                    dropdowns[0].SelectedIndex = 48;
                    dropdowns[1].SelectedIndex = 0;
                    dropdowns[2].SelectedIndex = 6;
                    dropdowns[3].SelectedIndex = dropdowns[3].Items.Count - 1;
                    object gridRoundTrip = Activator.CreateInstance(tokenType)!;
                    formType.GetMethod("OnUpdateTokenFromDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new[] { gridRoundTrip });
                    if ((int)tokenType.GetProperty("GridWidth")!.GetValue(gridRoundTrip)! != 50 ||
                        (int)tokenType.GetProperty("GridHeight")!.GetValue(gridRoundTrip)! != 2 ||
                        (int)tokenType.GetProperty("Quality")!.GetValue(gridRoundTrip)! != 7 ||
                        ((float[])tokenType.GetProperty("Displacements")!.GetValue(gridRoundTrip)!).Length != 51 * 3 * 2 ||
                        (int)formType.GetField("selectedGridColorIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)! != dropdowns[3].SelectedIndex)
                        throw new InvalidOperationException("Grid Warpified dropdowns do not update mesh, quality or color.");
                    FieldInfo displacementField = formType.GetField("displacements", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    dropdowns[1].SelectedIndex = 48;
                    float[] displacement = new float[51 * 51 * 2];
                    displacementField.SetValue(form, displacement);
                    MethodInfo recordUndo = formType.GetMethod("RecordUndo", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    System.Collections.IList undo = (System.Collections.IList)formType.GetField("undoStack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    System.Collections.IList redo = (System.Collections.IList)formType.GetField("redoStack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    undo.Clear();
                    for (int i = 0; i < 300; i++)
                    {
                        displacement[0] = i;
                        recordUndo.Invoke(form, null);
                    }
                    float[] SnapshotDisplacements(object snapshot) => (float[])snapshot.GetType().GetProperty("Displacements")!.GetValue(snapshot)!;
                    if (undo.Count != 256 || SnapshotDisplacements(undo[0]!)[0] != 44 ||
                        SnapshotDisplacements(undo[255]!)[0] != 299 || ReferenceEquals(SnapshotDisplacements(undo[255]!), displacement))
                        throw new InvalidOperationException("Grid Warpified history is unbounded, ordered incorrectly, or shares mutable arrays.");
                    MethodInfo addSnapshot = formType.GetMethod("AddHistorySnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    for (int i = 0; i < 300; i++)
                        addSnapshot.Invoke(form, new object[] { redo });
                    if (redo.Count != 256)
                        throw new InvalidOperationException("Grid Warpified redo history is unbounded.");
                    recordUndo.Invoke(form, null);
                    if (redo.Count != 0 || undo.Count != 256)
                        throw new InvalidOperationException("Grid Warpified new edits do not clear redo while preserving the history limit.");
                    dropdowns[0].SelectedIndex = 0;
                    if (undo.Count != 0 || redo.Count != 0 ||
                        ((float[])displacementField.GetValue(form)!).Any(value => value != 0))
                        throw new InvalidOperationException("Changing grid dimensions did not reset deformation and history.");
                    dropdowns[1].SelectedIndex = 0;
                    HashSet<int> selection = (HashSet<int>)formType.GetField("selectedPoints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    selection.Clear();
                    selection.UnionWith(new[] { 0, 1, 2 });
                    MethodInfo linkPoints = formType.GetMethod("LinkSelectedPoints", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    MethodInfo freezePoints = formType.GetMethod("SetSelectedFrozen", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    MethodInfo moveGroup = formType.GetMethod("MovePointGroup", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    linkPoints.Invoke(form, null);
                    selection.Clear();
                    selection.Add(1);
                    freezePoints.Invoke(form, new object[] { true });
                    moveGroup.Invoke(form, new object[] { 0, 3f, 5f });
                    displacement = (float[])displacementField.GetValue(form)!;
                    if (displacement[0] != 3 || displacement[1] != 5 || displacement[2] != 0 ||
                        displacement[3] != 0 || displacement[4] != 3 || displacement[5] != 5)
                        throw new InvalidOperationException("Linked points do not move together while respecting frozen members.");
                    moveGroup.Invoke(form, new object[] { 1, 10f, 10f });
                    if (displacement[0] != 3 || displacement[2] != 0)
                        throw new InvalidOperationException("A frozen point can move its group.");
                    object constraints = Activator.CreateInstance(tokenType)!;
                    formType.GetMethod("OnUpdateTokenFromDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new[] { constraints });
                    object constraintClone = tokenType.GetMethod("Clone")!.Invoke(constraints, null)!;
                    foreach (string property in new[] { "FrozenPoints", "LinkedGroups" })
                        if (ReferenceEquals(tokenType.GetProperty(property)!.GetValue(constraints), tokenType.GetProperty(property)!.GetValue(constraintClone)))
                            throw new InvalidOperationException("Grid constraints share mutable token data.");
                    formType.GetMethod("Undo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
                    if (((bool[])formType.GetField("frozenPoints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!)[1])
                        throw new InvalidOperationException("Undo does not restore freeze constraints.");
                    formType.GetMethod("Redo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
                    if (!((bool[])formType.GetField("frozenPoints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!)[1])
                        throw new InvalidOperationException("Redo does not restore freeze constraints.");
                    formType.GetMethod("UnlinkSelectedPoints", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
                    int[] links = (int[])formType.GetField("linkedGroups", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    if (links[1] != 0 || links[0] == 0 || links[0] != links[2])
                        throw new InvalidOperationException("Unlink removes unrelated group members.");
                    FieldInfo sourceField = formType.GetField("origSurface", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    using (IDisposable source = (IDisposable)Activator.CreateInstance(sourceField.FieldType, new object[] { 200, 100 })!)
                    {
                        FieldInfo fittedField = formType.GetField("initialFitDone", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        object fittedBefore = fittedField.GetValue(form)!;
                        fittedField.SetValue(form, true);
                        sourceField.SetValue(form, source);
                        formType.GetField("previewDisposed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
                        try
                        {
                        MethodInfo selectPoint = formType.GetMethod("SelectGridPoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        selectPoint.Invoke(form, new object[] { 1 });
                        NumericUpDown xEditor = (NumericUpDown)formType.GetField("pointX", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                        if (xEditor.Enabled)
                            throw new InvalidOperationException("Frozen point coordinates remain editable.");
                        formType.GetMethod("NudgeSelectedGridPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { Keys.Right, 10f });
                        if (((float[])displacementField.GetValue(form)!)[2] != 0)
                            throw new InvalidOperationException("Keyboard input moves a frozen point.");
                        selectPoint.Invoke(form, new object[] { 0 });
                        xEditor.Value = 10;
                        displacement = (float[])displacementField.GetValue(form)!;
                        if (displacement[0] != 10 || displacement[4] != 10 || displacement[2] != 0)
                            throw new InvalidOperationException("Numeric editing does not move linked unfrozen members.");
                        selection.Clear();
                        selection.Add(0);
                        freezePoints.Invoke(form, new object[] { true });
                        formType.GetMethod("ResetGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
                        displacement = (float[])displacementField.GetValue(form)!;
                        if (displacement[0] != 10 || displacement[4] != 0)
                            throw new InvalidOperationException("Reset ignores frozen-point constraints.");
                        freezePoints.Invoke(form, new object[] { false });
                        MethodInfo mouseDown = formType.GetMethod("Canvas_MouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        MethodInfo mouseMove = formType.GetMethod("Canvas_MouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        MethodInfo mouseUp = formType.GetMethod("Canvas_MouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        Control gridCanvas = (Control)formType.GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                        mouseDown.Invoke(form, new object[] { gridCanvas, new MouseEventArgs(MouseButtons.Left, 1, 100, 0, 0) });
                        if ((int)formType.GetField("dragPointIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)! != -1)
                            throw new InvalidOperationException("Mouse dragging can begin on a frozen point.");
                        mouseDown.Invoke(form, new object[] { gridCanvas, new MouseEventArgs(MouseButtons.Left, 1, 10, 5, 0) });
                        mouseMove.Invoke(form, new object[] { gridCanvas, new MouseEventArgs(MouseButtons.Left, 0, 30, 15, 0) });
                        mouseUp.Invoke(form, new object[] { gridCanvas, new MouseEventArgs(MouseButtons.Left, 1, 30, 15, 0) });
                        displacement = (float[])displacementField.GetValue(form)!;
                        if (displacement[0] != 30 || displacement[1] != 15 || displacement[4] != 20 || displacement[5] != 10)
                            throw new InvalidOperationException("Mouse dragging does not move linked members by the same delta.");
                        MethodInfo command = formType.GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        void SwitchMode(Keys shortcut)
                        {
                            object[] arguments = { default(Message), shortcut };
                            if (!(bool)command.Invoke(form, arguments)!)
                                throw new InvalidOperationException("Point mode shortcut was not handled.");
                        }
                        float[] beforeModes = (float[])displacement.Clone();
                        Label modeBanner = (Label)formType.GetField("modeStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                        Button returnToMove = (Button)formType.GetField("returnToMoveBtn", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                        Type ui = types.Single(type => type.Name == "DialogUi");
                        MethodInfo theme = ui.GetMethod("ApplyTheme", BindingFlags.Static | BindingFlags.NonPublic)!;
                        foreach (Keys shortcut in new[] { Keys.D2, Keys.D3, Keys.D4, Keys.D5 })
                        {
                            SwitchMode(Keys.Alt | shortcut);
                            Button activeButton = Descendants(form).OfType<Button>().Single(button =>
                                button.Name.StartsWith("pointMode", StringComparison.Ordinal) &&
                                button.AccessibleDescription!.Contains(". Selected."));
                            foreach (bool dark in new[] { false, true })
                            {
                                theme.Invoke(null, new object[] { form, gridCanvas, dark });
                                if (activeButton.BackColor != modeBanner.BackColor ||
                                    activeButton.ForeColor != modeBanner.ForeColor ||
                                    !modeBanner.Text.Contains("Esc returns to Move") || !returnToMove.Enabled)
                                    throw new InvalidOperationException("Active constraint mode loses its contrasting state or exit reminder.");
                            }
                            SwitchMode(Keys.Escape);
                            if (returnToMove.Enabled || Equals(modeBanner.Tag, true))
                                throw new InvalidOperationException("Escape leaves the constraint-mode banner active.");
                        }
                        MethodInfo drawOverlay = formType.GetMethod("DrawGridOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        MethodInfo selectColor = formType.GetMethod("SelectGridColor", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        int presetCount = dropdowns[3].Items.Count;
                        FieldInfo zoomField = formType.GetField("zoom", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        FieldInfo panField = formType.GetField("pan", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        object savedZoom = zoomField.GetValue(form)!;
                        object savedPan = panField.GetValue(form)!;
                        zoomField.SetValue(form, 1f);
                        panField.SetValue(form, System.Drawing.PointF.Empty);
                        try
                        {
                        MethodInfo drawFrozen = formType.GetMethod("DrawFrozenPoints", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        FieldInfo frozenField = formType.GetField("frozenPoints", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        bool[] savedFrozen = (bool[])frozenField.GetValue(form)!;
                        var testFrozen = new bool[savedFrozen.Length];
                        testFrozen[0] = true;
                        frozenField.SetValue(form, testFrozen);
                        try
                        {
                            foreach (Keys shortcut in new[] { Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5 })
                            {
                                SwitchMode(Keys.Alt | shortcut);
                                foreach (bool dark in new[] { false, true })
                                foreach (int preset in Enumerable.Range(0, presetCount))
                                {
                                    selectColor.Invoke(form, new object[] { preset });
                                    using var locks = new System.Drawing.Bitmap(240, 120);
                                    using var lockGraphics = System.Drawing.Graphics.FromImage(locks);
                                    lockGraphics.Clear(dark ? System.Drawing.Color.Black : System.Drawing.Color.White);
                                    drawFrozen.Invoke(form, new object[] { lockGraphics });
                                    var pixel = locks.GetPixel(30, 21);
                                    if (!SystemInformation.HighContrast && !(pixel.R < 100 && pixel.G > 150 && pixel.B > 230))
                                        throw new InvalidOperationException("Frozen-point padlock disappears after mode or grid-color changes.");
                                }
                            }
                            Array.Clear(testFrozen);
                            using var cleared = new System.Drawing.Bitmap(240, 120);
                            using var clearedGraphics = System.Drawing.Graphics.FromImage(cleared);
                            clearedGraphics.Clear(System.Drawing.Color.Black);
                            drawFrozen.Invoke(form, new object[] { clearedGraphics });
                            if (cleared.GetPixel(30, 21).ToArgb() != System.Drawing.Color.Black.ToArgb())
                                throw new InvalidOperationException("Unfrozen points retain a padlock.");
                        }
                        finally
                        {
                            frozenField.SetValue(form, savedFrozen);
                            SwitchMode(Keys.Alt | Keys.D1);
                        }
                        foreach (bool dark in new[] { false, true })
                        {
                            foreach (int preset in Enumerable.Range(0, presetCount))
                            {
                                selectColor.Invoke(form, new object[] { preset });
                                using var overlay = new System.Drawing.Bitmap(240, 120);
                                using var graphics = System.Drawing.Graphics.FromImage(overlay);
                                graphics.Clear(dark ? System.Drawing.Color.Black : System.Drawing.Color.White);
                                drawOverlay.Invoke(form, new object[] { graphics });
                                bool accentFound = false;
                                for (int x = 50; x < 180; x++)
                                    for (int y = 8; y < 20; y++)
                                    {
                                        var pixel = overlay.GetPixel(x, y);
                                        if (pixel.R > 170 && pixel.G < 40 && pixel.B > 95) accentFound = true;
                                    }
                                if (!SystemInformation.HighContrast && !accentFound)
                                    throw new InvalidOperationException($"Linked connection highlight disappears for preset {preset}, dark={dark}, links={string.Join(',', (int[])formType.GetField("linkedGroups", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!)}.");
                            }
                        }
                        }
                        finally
                        {
                            zoomField.SetValue(form, savedZoom);
                            panField.SetValue(form, savedPan);
                        }
                        SwitchMode(Keys.Alt | Keys.D2);
                        mouseDown.Invoke(form, new object[] { gridCanvas, new MouseEventArgs(MouseButtons.Left, 1, 30, 15, 0) });
                        mouseMove.Invoke(form, new object[] { gridCanvas, new MouseEventArgs(MouseButtons.Left, 0, 60, 50, 0) });
                        mouseUp.Invoke(form, new object[] { gridCanvas, new MouseEventArgs(MouseButtons.Left, 1, 60, 50, 0) });
                        if (!((bool[])formType.GetField("frozenPoints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!)[0] ||
                            !beforeModes.SequenceEqual((float[])displacementField.GetValue(form)!) || xEditor.Enabled)
                            throw new InvalidOperationException("Freeze mode moves points or leaves numeric editing enabled.");
                        SwitchMode(Keys.Alt | Keys.D3);
                        selectPoint.Invoke(form, new object[] { 0 });
                        formType.GetMethod("Canvas_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form,
                            new object[] { gridCanvas, new KeyEventArgs(Keys.Space) });
                        if (((bool[])formType.GetField("frozenPoints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!)[0])
                            throw new InvalidOperationException("Keyboard Unfreeze mode did not release the point.");
                        SwitchMode(Keys.Alt | Keys.D4);
                        MethodInfo applyMode = formType.GetMethod("ApplyModeToPoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        applyMode.Invoke(form, new object[] { 0 });
                        applyMode.Invoke(form, new object[] { 3 });
                        links = (int[])formType.GetField("linkedGroups", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                        if (links[0] == 0 || links[0] != links[3])
                            throw new InvalidOperationException("Link mode did not join points to its anchor.");
                        SwitchMode(Keys.Alt | Keys.D5);
                        applyMode.Invoke(form, new object[] { 3 });
                        links = (int[])formType.GetField("linkedGroups", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                        if (links[3] != 0 || links[0] == 0)
                            throw new InvalidOperationException("Unlink mode changed unrelated members.");
                        formType.GetMethod("Canvas_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form,
                            new object[] { gridCanvas, new KeyEventArgs(Keys.Right) });
                        if (!beforeModes.SequenceEqual((float[])displacementField.GetValue(form)!))
                            throw new InvalidOperationException("Constraint modes allow accidental arrow movement.");
                        SwitchMode(Keys.Escape);
                        if (Convert.ToInt32(formType.GetField("editMode", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)) != 0 ||
                            form.DialogResult != DialogResult.None)
                            throw new InvalidOperationException("Escape failed to restore Move without closing the dialog.");
                        }
                        finally
                        {
                        sourceField.SetValue(form, null);
                        fittedField.SetValue(form, fittedBefore);
                        formType.GetField("previewDisposed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, false);
                        }
                        MethodInfo selectPointAfterDispose = formType.GetMethod("SelectGridPoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        selectPointAfterDispose.Invoke(form, new object[] { 0 });
                    }
                    formType.GetMethod("OnUpdateDialogFromToken", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new[] { constraints });
                    bool[] reopenedFrozen = (bool[])formType.GetField("frozenPoints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    int[] reopenedLinks = (int[])formType.GetField("linkedGroups", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    if (!reopenedFrozen[1] || reopenedLinks[0] == 0 || reopenedLinks[0] != reopenedLinks[1] || reopenedLinks[1] != reopenedLinks[2])
                        throw new InvalidOperationException("Reopening a token loses freeze/link editing constraints.");
                }
                Control[] information = Descendants(form).Where(control => control.Name == "controlInfo").ToArray();
                if (information.Length < 2 || information.Any(control => control.Text.Length != 0 ||
                    string.IsNullOrWhiteSpace(control.AccessibleDescription)))
                    throw new InvalidOperationException(name + " has missing compact info icons.");
                if (Descendants(form).OfType<Label>().Any(label => label.Text.Contains('\n')))
                    throw new InvalidOperationException(name + " retains screen-consuming explanatory paragraphs.");
                Type suiteUi = types.Single(type => type.Name == "DialogUi");
                MethodInfo suiteTheme = suiteUi.GetMethod("ApplyTheme", BindingFlags.Static | BindingFlags.NonPublic)!;
                Control suiteCanvas = (Control)formType.GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                Control suiteActions = Descendants(form).Single(control => control.Name == "actionsBar");
                if (!Descendants(suiteActions).Contains((Control)form.AcceptButton!) ||
                    !Descendants(suiteActions).Contains((Control)form.CancelButton!))
                    throw new InvalidOperationException(name + " confirmation buttons disappear with settings.");
                foreach (System.Drawing.Size size in new[] { new System.Drawing.Size(1280, 900), new System.Drawing.Size(900, 700), new System.Drawing.Size(600, 400) })
                {
                    form.ClientSize = size;
                    form.PerformLayout();
                    foreach (bool dark in new[] { false, true })
                    {
                        suiteTheme.Invoke(null, new object[] { form, suiteCanvas, dark });
                        if (!SystemInformation.HighContrast && (suiteActions.BackColor.GetBrightness() < 0.5f) != dark)
                            throw new InvalidOperationException(name + " did not apply the shared light/dark palette.");
                    }
                    if (name != "LiquifiedPlugin")
                    {
                        Button collapse = Descendants(form).OfType<Button>().Single(button => button.Name == "collapseSettings");
                        MethodInfo click = typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        click.Invoke(collapse, new object[] { EventArgs.Empty });
                        form.PerformLayout();
                        if (collapse.AccessibleName != "Expand settings")
                            throw new InvalidOperationException(name + " settings could not collapse.");
                        if (name == "GridWarpPlugin" && Descendants(form).OfType<ComboBox>().Any(combo =>
                            !Descendants(Descendants(form).Single(control => control.Name == "commonSettingsBar")).Contains(combo)))
                            throw new InvalidOperationException("Grid Warpified common settings disappear when the sidebar collapses.");
                        click.Invoke(collapse, new object[] { EventArgs.Empty });
                        form.PerformLayout();
                        if (collapse.AccessibleName != "Collapse settings" || suiteCanvas.Width < 100 || suiteCanvas.Height < 100 ||
                            suiteCanvas.Bottom > suiteActions.Top)
                            throw new InvalidOperationException(name + " settings could not expand or overlap the canvas.");
                        if (name == "GridWarpPlugin")
                        {
                            MethodInfo setMode = formType.GetMethod("SetEditMode", BindingFlags.Instance | BindingFlags.NonPublic)!;
                            Type modeType = setMode.GetParameters()[0].ParameterType;
                            foreach (int mode in Enumerable.Range(1, 4))
                            {
                                setMode.Invoke(form, new[] { Enum.ToObject(modeType, mode) });
                                form.PerformLayout();
                                if (suiteCanvas.Height < 100 || suiteCanvas.Bottom > suiteActions.Top)
                                    throw new InvalidOperationException("Active mode reminder obscures the canvas at minimum size.");
                                Control banner = Descendants(form).Single(control => control.Name == "pointModeStatus");
                                Control moveButton = Descendants(form).Single(control => control.Name == "returnToMove");
                                if (banner.Bounds.IntersectsWith(moveButton.Bounds) ||
                                    banner.Margin.Right < 1 || banner.Margin.Top != moveButton.Margin.Top ||
                                    (moveButton.Left >= banner.Right && moveButton.Top != banner.Top))
                                    throw new InvalidOperationException("Constraint mode banner and exit button lack aligned, separated layout.");
                            }
                            string? modeCapture = System.Environment.GetEnvironmentVariable("LIQUIFIED_UI_CAPTURE_DIRECTORY");
                            if (!string.IsNullOrWhiteSpace(modeCapture) && size.Width == 1280)
                                CaptureControls(form, Path.Combine(modeCapture, "GridWarpPlugin-active-mode.png"));
                            setMode.Invoke(form, new[] { Enum.ToObject(modeType, 0) });
                        }
                        string? capture = System.Environment.GetEnvironmentVariable("LIQUIFIED_UI_CAPTURE_DIRECTORY");
                        if (!string.IsNullOrWhiteSpace(capture) && size.Width == 1280)
                        {
                            foreach (bool dark in new[] { false, true })
                            {
                                suiteTheme.Invoke(null, new object[] { form, suiteCanvas, dark });
                                CaptureControls(form, Path.Combine(capture, name + (dark ? "-dark.png" : "-light.png")));
                            }
                        }
                    }
                    if (name == "LiquifiedPlugin")
                    {
                        Control top = Descendants(form).Single(control => control.Name == "commonSettingsBar");
                        Control bottom = Descendants(form).Single(control => control.Name == "actionsBar");
                        Control canvas = (Control)formType.GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                        MethodInfo collapse = formType.GetMethod("SetSidebarCollapsed", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        foreach (bool collapsed in new[] { false, true })
                        {
                            collapse.Invoke(form, new object[] { collapsed });
                            form.PerformLayout();
                            if (canvas.Height < 100 || canvas.Width < 100 || canvas.Top < top.Bottom ||
                                canvas.Bottom > bottom.Top || bottom.Bottom > form.ClientSize.Height)
                                throw new InvalidOperationException("Liquified bars overlap or leave no usable canvas at " + size + ".");
                            foreach (Control group in top.Controls)
                            {
                                if (group.Right > top.ClientSize.Width || group.Bottom > top.ClientSize.Height)
                                    throw new InvalidOperationException("Liquified top-bar controls are clipped at " + size + ".");
                            }
                        }
                        var row = Descendants(bottom).Single(control => control.Name == "footerButtons");
                        Button[] footerButtons = row.Controls.OfType<Button>().ToArray();
                        if (footerButtons.Select(button => button.Height).Distinct().Count() != 1 ||
                            footerButtons.GroupBy(button => button.Top).Any(group => group.Select(button => button.Bottom).Distinct().Count() != 1))
                            throw new InvalidOperationException("Liquified footer buttons do not align.");
                        foreach (Button button in footerButtons.Where(button => button.Text.Length > 0))
                        {
                            int captionWidth = TextRenderer.MeasureText(button.Text, button.Font, System.Drawing.Size.Empty, TextFormatFlags.SingleLine).Width;
                            if (captionWidth + button.Padding.Horizontal + 4 > button.Width)
                                throw new InvalidOperationException("Liquified button caption is clipped.");
                        }
                        Label status = (Label)formType.GetField("statusLabel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                        status.Text = "Mode: Forward Warp | Brush: 80px | Rate: 12/s | Zoom: 113% | Frozen: 0 | Position: -58, 1485 | Pressure: full (mouse/unsupported driver) | Mesh: forward deformation | Undo: 17/256";
                        form.PerformLayout();
                        if (status.Top < row.Bottom)
                            throw new InvalidOperationException("Liquified status overlaps the footer buttons.");
                        MethodInfo palette = formType.GetMethod("ApplyPalette", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        foreach (bool dark in new[] { false, true })
                        {
                            palette.Invoke(form, new object[] { dark });
                            if (!SystemInformation.HighContrast && (top.BackColor.GetBrightness() < 0.5f) != dark)
                                throw new InvalidOperationException("Liquified did not apply the light/dark host palette.");
                            string? capture = System.Environment.GetEnvironmentVariable("LIQUIFIED_UI_CAPTURE_DIRECTORY");
                            if (!string.IsNullOrWhiteSpace(capture) && size.Width == 1280)
                            {
                                Directory.CreateDirectory(capture);
                                formType.GetMethod("SetSidebarCollapsed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { false });
                                CaptureControls(form, Path.Combine(capture, dark ? "liquified-dark.png" : "liquified-light.png"));
                            }
                        }
                    }
                    foreach (Control control in Descendants(form))
                    {
                        if (control.Width < 0 || control.Height < 0)
                        {
                            throw new InvalidOperationException(name + " has invalid control dimensions after resize.");
                        }
                        if (control is Button { Image: not null } button && string.IsNullOrWhiteSpace(button.Text) && string.IsNullOrWhiteSpace(button.AccessibleName))
                        {
                            throw new InvalidOperationException(name + " has an unnamed image-only button.");
                        }
                    }
                }
                int prepared = 0;
                foreach (Type type in new[] { tokenType, effectType, formType })
                {
                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (!method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() is not null)
                        {
                            RuntimeHelpers.PrepareMethod(method.MethodHandle);
                            prepared++;
                        }
                        if (name == "LiquifiedPlugin")
                        {
                            Type strokeType = types.Single(type => type.Name == "BrushStroke");
                            System.Collections.IList strokes = (System.Collections.IList)tokenType.GetProperty("Strokes")!.GetValue(token)!;
                            object stroke = Activator.CreateInstance(strokeType)!;
                            System.Collections.IList points = (System.Collections.IList)strokeType.GetProperty("Points")!.GetValue(stroke)!;
                            float[] point = new[] { 10f, 20f };
                            points.Add(point);
                            strokes.Add(stroke);
                            object populatedClone = tokenType.GetMethod("Clone")!.Invoke(token, null)!;
                            System.Collections.IList clonedStrokes = (System.Collections.IList)tokenType.GetProperty("Strokes")!.GetValue(populatedClone)!;
                            System.Collections.IList clonedPoints = (System.Collections.IList)strokeType.GetProperty("Points")!.GetValue(clonedStrokes[0])!;
                            if (ReferenceEquals(strokes, clonedStrokes) || ReferenceEquals(stroke, clonedStrokes[0]) || ReferenceEquals(point, clonedPoints[0]))
                            {
                                throw new InvalidOperationException("Liquified shares mutable stroke history across token clones.");
                            }
                        }
                    }

                }
                Console.WriteLine($"PASS {name}: {types.Length} types loaded; {prepared} methods JIT-checked; token cloned; effect and configuration dialog constructed/disposed.");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            if (exception is ReflectionTypeLoadException loadException)
            {
                foreach (Exception? loaderException in loadException.LoaderExceptions)
                {
                    Console.Error.WriteLine(loaderException);
                }
            }
            return 1;
        }
    }

    private static void CaptureControls(Form form, string path)
    {
        Control[] controls = form.Controls.Cast<Control>().ToArray();
        using var preview = new Form
        {
            ClientSize = form.ClientSize, StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(-20000, -20000), ShowInTaskbar = false,
            Text = "Liquified layout capture"
        };
        try
        {
            preview.Controls.AddRange(controls);
            preview.Show();
            preview.PerformLayout();
            using var bitmap = new System.Drawing.Bitmap(preview.Width, preview.Height);
            preview.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, preview.Size));
            bitmap.Save(path);
        }
        finally
        {
            preview.Controls.Clear();
            form.Controls.AddRange(controls);
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
