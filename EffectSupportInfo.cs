using PaintDotNet;
using System;
using System.Reflection;

namespace DistortionPlugins;

internal sealed class EffectSupportInfoAttribute : PluginSupportInfoAttribute
{
    public EffectSupportInfoAttribute()
    {
        Assembly assembly = typeof(EffectSupportInfoAttribute).Assembly;
        DisplayName = assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product;
        Author = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company;
        Copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright;
        Version = assembly.GetName().Version!;
        WebsiteUri = new Uri("https://github.com/joelst/pdn-liquified");
    }
}
