using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Yarp.ServiceDiscovery;

// mainly copy from YARP because needed for own ServiceDiscoveryProxyConfigProvider to load 
// the config from the appsettings.json
internal static class ConfigurationReadingExtensions
{
    public static int? ReadInt32(this IConfiguration configuration, string name)
    {
        var s = configuration[name];
        return s == null
            ? new int?()
            : int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    public static double? ReadDouble(this IConfiguration configuration, string name)
    {
        var s = configuration[name];
        return s == null ? new double?() : double.Parse(s, CultureInfo.InvariantCulture);
    }

    public static TimeSpan? ReadTimeSpan(this IConfiguration configuration, string name)
    {
        var input = configuration[name];
        return input == null
            ? new TimeSpan?()
            : TimeSpan.ParseExact(input, "c", CultureInfo.InvariantCulture);
    }

    public static Uri? ReadUri(this IConfiguration configuration, string name)
    {
        var uriString = configuration[name];
        return uriString == null ? null : new Uri(uriString);
    }

    public static TEnum? ReadEnum<TEnum>(this IConfiguration configuration, string name) where TEnum : struct
    {
        var str = configuration[name];
        return str == null ? new TEnum?() : Enum.Parse<TEnum>(str, true);
    }

    public static bool? ReadBool(this IConfiguration configuration, string name)
    {
        var str = configuration[name];
        return str == null ? new bool?() : bool.Parse(str);
    }

    public static Version? ReadVersion(this IConfiguration configuration, string name)
    {
        var str = configuration[name];
        return str == null || string.IsNullOrEmpty(str)
            ? null
            : Version.Parse(str + (str.Contains('.') ? "" : ".0"));
    }

    public static IReadOnlyDictionary<string, string>? ReadStringDictionary(
        this IConfigurationSection section)
    {
        IEnumerable<IConfigurationSection> children = section.GetChildren();
        // ReSharper disable PossibleMultipleEnumeration
        return !children.Any()
            ? null
            : (IReadOnlyDictionary<string, string>)new ReadOnlyDictionary<string, string>(
                children.ToDictionary<IConfigurationSection, string, string>(
                    s => s.Key,
                    s => s.Value,
                    StringComparer.OrdinalIgnoreCase));
        // ReSharper restore PossibleMultipleEnumeration
    }

    public static string[]? ReadStringArray(this IConfigurationSection section)
    {
        IEnumerable<IConfigurationSection> children = section.GetChildren();
        // ReSharper disable PossibleMultipleEnumeration
        return !children.Any()
            ? null
            : children.Select<IConfigurationSection, string>(s => s.Value)
                .ToArray();
        // ReSharper restore PossibleMultipleEnumeration
    }
}
