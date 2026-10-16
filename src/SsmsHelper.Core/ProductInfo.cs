using System.Reflection;

namespace SsmsHelper.Core;

/// <summary>Product identity shared by the extension and its tests.</summary>
public static class ProductInfo
{
    public const string Name = "SSMS Helper";

    /// <summary>Informational version of the Core assembly, without source-link metadata.</summary>
    public static string Version
    {
        get
        {
            var informational = typeof(ProductInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (informational is null || informational.Length == 0)
            {
                return typeof(ProductInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            }

            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational.Substring(0, plus);
        }
    }
}
