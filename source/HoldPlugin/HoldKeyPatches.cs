using System.Configuration;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using CommunityToolkit.Mvvm.Messaging;
using HarmonyLib;
using HoldPlugin.Contracts;
using vatsys;
using vatSysToolbox;

namespace HoldPlugin;

/// <summary>
///     Harmony patches for the hold key. The key initiates or cancels a hold on the ASD. The
///     Keyboard Settings window shows the key, and the user can change it there.
/// </summary>
public static class HoldKeyPatches
{
    const string HoldKeyButtonName = "holdPluginHoldKeyToggleButton";

    static readonly Type KeyboardSettingsWindowType = AccessTools.TypeByName("vatsys.KeyboardSettingsWindow");
    // SetText has two overloads: (ToggleButton, Keys) and (ToggleButton, InputMap).
    static readonly MethodInfo SetTextMethod = AccessTools.Method(
        KeyboardSettingsWindowType,
        "SetText",
        [AccessTools.TypeByName("vatsys.ToggleButton"), typeof(Keys)]);

    // The patches run on the GUI thread.
    static KeySettings _keySettings = null!;
    static IErrorReporter _errorReporter = null!;

    // True while the user resets the keys to the defaults in the Keyboard Settings window.
    static bool _isResettingKeys;

    // Call this before HoldPatches.Apply, which applies all the patches in the assembly.
    public static void Initialize(KeySettings keySettings, IErrorReporter errorReporter)
    {
        _keySettings = keySettings;
        _errorReporter = errorReporter;
    }

    /// <summary>
    ///     When the user presses the hold key on the ASD with a track designated, initiate or
    ///     cancel a hold at the cursor position.
    /// </summary>
    [HarmonyPatch]
    static class AsdPreviewKeyDownPatch
    {
        static readonly FieldInfo CursorLatLongField = AccessTools.Field("vatsys.ASDControlDX:CursorLatLong");
        static readonly MethodInfo GetRangeMethod = AccessTools.Method("vatsys.ASDControlDX:GetRange");

        static MethodBase TargetMethod() => AccessTools.Method("vatsys.ASDControlDX:ASD_PreviewKeyDown");

        static bool Prefix(Control __instance, PreviewKeyDownEventArgs e)
        {
            try
            {
                if (_keySettings.HoldKey == Keys.None || e.KeyData != _keySettings.HoldKey)
                    return true;

                var fdr = MMI.SelectedTrack?.GetFDR();
                if (fdr is null)
                    return false;

                if (CursorLatLongField.GetValue(__instance) is not Coordinate cursorPosition)
                    return false;

                // The same distance as the graphical reroute tool: 6 pixels at the current range.
                var range = (int)GetRangeMethod.Invoke(__instance, null);
                var distanceTolerance = range / (double)__instance.ClientSize.Width * 6.0;

                WeakReferenceMessenger.Default.Send(new HoldKeyPressedCommand(fdr.Callsign, cursorPosition, distanceTolerance));
                return false;
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
                return true;
            }
        }
    }

    /// <summary>
    ///     Add a row for the hold key to the Keyboard Settings window.
    /// </summary>
    [HarmonyPatch]
    static class KeyboardSettingsWindowConstructorPatch
    {
        static MethodBase TargetMethod() => AccessTools.Constructor(KeyboardSettingsWindowType, Type.EmptyTypes);

        static void Postfix(Form __instance)
        {
            try
            {
                AddHoldKeyRow(__instance);
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        }
    }

    /// <summary>
    ///     The Default button resets the vatSys keys, then loads them again. Reset the hold key too.
    /// </summary>
    [HarmonyPatch]
    static class DefaultButtonClickPatch
    {
        static MethodBase TargetMethod() => AccessTools.Method(KeyboardSettingsWindowType, "defaultButton_Click");

        static void Prefix() => _isResettingKeys = true;

        static void Postfix() => _isResettingKeys = false;
    }

    /// <summary>
    ///     vatSys calls LoadKeys only after the user confirms the reset.
    /// </summary>
    [HarmonyPatch]
    static class LoadKeysPatch
    {
        static MethodBase TargetMethod() => AccessTools.Method(KeyboardSettingsWindowType, "LoadKeys");

        static void Postfix(Form __instance)
        {
            try
            {
                if (!_isResettingKeys)
                    return;

                _keySettings.HoldKey = KeySettings.DefaultHoldKey;

                if (__instance.Controls.Find(HoldKeyButtonName, true).FirstOrDefault() is CheckBox button)
                    SetText(__instance, button, _keySettings.HoldKey);
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        }
    }

    // Copy the style of the Reroute row. Add the new row below the last row of the right column.
    static void AddHoldKeyRow(Form window)
    {
        var table = (TableLayoutPanel)AccessTools.Field(KeyboardSettingsWindowType, "tableLayoutPanel2").GetValue(window);
        var rerouteLabel = (TextLabel)AccessTools.Field(KeyboardSettingsWindowType, "textLabel24").GetValue(window);
        var rerouteButton = (CheckBox)AccessTools.Field(KeyboardSettingsWindowType, "rerouteToggleButton").GetValue(window);
        var defaultButton = (Control)AccessTools.Field(KeyboardSettingsWindowType, "defaultButton").GetValue(window);

        var label = new TextLabel
        {
            Anchor = rerouteLabel.Anchor,
            AutoSize = true,
            Font = rerouteLabel.Font,
            ForeColor = rerouteLabel.ForeColor,
            HasBorder = rerouteLabel.HasBorder,
            InteractiveText = rerouteLabel.InteractiveText,
            Margin = rerouteLabel.Margin,
            Text = "Hold",
            TextAlign = rerouteLabel.TextAlign
        };

        // ToggleButton is internal in vatSys.
        var button = (CheckBox)AccessTools.CreateInstance(rerouteButton.GetType());
        button.Name = HoldKeyButtonName;
        button.AutoCheck = false;
        button.Anchor = rerouteButton.Anchor;
        button.Font = rerouteButton.Font;
        button.Margin = rerouteButton.Margin;
        button.Size = rerouteButton.Size;
        button.TextAlign = rerouteButton.TextAlign;
        button.UseVisualStyleBackColor = true;
        SetText(window, button, _keySettings.HoldKey);
        AddKeyCapture(window, button);

        var row = table.RowCount;
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle());
        table.Controls.Add(label, 0, row);
        table.Controls.Add(button, 1, row);

        // Make space for the new row.
        var rowHeight = button.Height + button.Margin.Vertical;
        window.ClientSize = new Size(window.ClientSize.Width, window.ClientSize.Height + rowHeight);
        defaultButton.Top += rowHeight;
    }

    // The same behaviour as the vatSys key buttons. A left click starts or stops the key capture,
    // and a middle click clears the key.
    static void AddKeyCapture(Form window, CheckBox button)
    {
        var keyValue = Keys.None;

        void OnPreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            e.IsInputKey = true;
            keyValue = e.KeyCode;
        }

        void OnKeyUp(object sender, KeyEventArgs e)
        {
            try
            {
                e.SuppressKeyPress = true;

                if (e.KeyCode is Keys.Menu or Keys.Alt or Keys.LWin or Keys.RWin)
                {
                    Errors.Add(new Exception($"Cannot use {e.KeyCode} as a command."), "Keyboard Settings");
                    return;
                }

                var keys = keyValue == Keys.None ? Keys.None : e.Modifiers | keyValue;
                if (TryFindVatSysCommand(keys, out var commandName))
                {
                    Errors.Add(new Exception("Key command already selected for " + commandName), "Keyboard Settings");
                    return;
                }

                _keySettings.HoldKey = keys;
                SetText(window, button, keys);
                button.Checked = false;
                keyValue = Keys.None;
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        }

        void OnLostFocus(object sender, EventArgs e)
        {
            Cursor.Clip = Rectangle.Empty;
            button.Checked = false;
        }

        button.CheckedChanged += (_, _) =>
        {
            if (button.Checked)
            {
                Cursor.Clip = button.RectangleToScreen(button.ClientRectangle);
                button.LostFocus += OnLostFocus;
                button.KeyUp += OnKeyUp;
                button.PreviewKeyDown += OnPreviewKeyDown;
            }
            else
            {
                button.LostFocus -= OnLostFocus;
                button.KeyUp -= OnKeyUp;
                button.PreviewKeyDown -= OnPreviewKeyDown;
                Cursor.Clip = Rectangle.Empty;
            }
        };

        button.MouseUp += (_, e) =>
        {
            try
            {
                if (e.Button == MouseButtons.Middle)
                {
                    _keySettings.HoldKey = Keys.None;
                    SetText(window, button, Keys.None);
                    button.Checked = false;
                }
                else if (e.Button == MouseButtons.Left)
                {
                    button.Checked = !button.Checked;
                }
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        };
    }

    // The vatSys keys are in vatsys.Properties.Keys, which is internal.
    static bool TryFindVatSysCommand(Keys keys, out string commandName)
    {
        commandName = string.Empty;

        var keysType = AccessTools.TypeByName("vatsys.Properties.Keys");
        var vatSysKeys = (ApplicationSettingsBase)AccessTools.Property(keysType, "Default").GetValue(null);
        foreach (SettingsProperty property in vatSysKeys.Properties)
        {
            if ((Keys)vatSysKeys[property.Name] != keys)
                continue;

            commandName = property.Name;
            return true;
        }

        return false;
    }

    // Use the vatSys text format for keys, e.g. "Shift F8".
    static void SetText(Form window, CheckBox button, Keys keys) =>
        SetTextMethod.Invoke(window, [button, keys]);
}
