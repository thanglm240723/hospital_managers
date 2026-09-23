using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace CleanArchCqrs.API.Logging;

/// Khi một object được log bằng {@...}, thay giá trị các property nhạy cảm bằng "***".
public sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy
{
    private const string Mask = "***";

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "CurrentPassword", "NewPassword", "TemporaryPassword",
        "AccessToken", "RefreshToken", "PasswordHash", "TokenHash",
    };

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        result = null;
        var type = value.GetType();
        if (value is string or IEnumerable || type.IsPrimitive || type.IsEnum)
            return false;

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToList();
        if (!properties.Any(p => SensitiveNames.Contains(p.Name)))
            return false;

        result = new StructureValue(
            properties.Select(p => new LogEventProperty(p.Name,
                SensitiveNames.Contains(p.Name)
                    ? new ScalarValue(Mask)
                    : propertyValueFactory.CreatePropertyValue(p.GetValue(value), destructureObjects: true))),
            type.Name);
        return true;
    }
}
