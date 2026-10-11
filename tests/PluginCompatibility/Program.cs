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
                form.CreateControl();
                if (name is "GridWarpPlugin" or "DocumentRectifyPlugin")
                {
                    formType.GetMethod("OnUpdateDialogFromToken", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(form, new[] { token });
                    Control canvas = (Control)formType.GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
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
                foreach (System.Drawing.Size size in new[] { new System.Drawing.Size(1280, 900), new System.Drawing.Size(900, 700) })
                {
                    form.ClientSize = size;
                    form.PerformLayout();
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
