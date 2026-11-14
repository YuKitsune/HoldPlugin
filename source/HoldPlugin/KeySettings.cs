using System.IO;
using System.Windows.Forms;

namespace HoldPlugin;

/// <summary>
///     The key that initiates and cancels a hold. The key is saved in a file next to the plugin,
///     thus each profile has its own key.
/// </summary>
public class KeySettings
{
    public static readonly Keys DefaultHoldKey = Keys.F8;

    readonly string _filePath = Path.Combine(
        Path.GetDirectoryName(typeof(KeySettings).Assembly.Location)!,
        "HoldKey.txt");

    Keys _holdKey;

    public KeySettings()
    {
        _holdKey = Load();
    }

    public Keys HoldKey
    {
        get => _holdKey;
        set
        {
            _holdKey = value;
            File.WriteAllText(_filePath, value.ToString());
        }
    }

    Keys Load()
    {
        if (!File.Exists(_filePath))
            return DefaultHoldKey;

        return Enum.TryParse<Keys>(File.ReadAllText(_filePath).Trim(), out var key)
            ? key
            : DefaultHoldKey;
    }
}
