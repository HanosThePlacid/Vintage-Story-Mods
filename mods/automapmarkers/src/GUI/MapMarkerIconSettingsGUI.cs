using Egocarib.AutoMapMarkers.Settings;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Egocarib.AutoMapMarkers.GUI
{
    using AutoMapMarkerSetting = MapMarkerConfig.Settings.AutoMapMarkerSetting;

    public class MapMarkerIconSettingsGUI : GuiDialogGeneric
    {
        private int[] colors;
        private readonly string[] icons;
        private readonly string iconTitle;
        private readonly AutoMapMarkerSetting settings;
        private const string GuiPrefix = "auto-markers-single-icon";
        private int selectedIconIndex = -1;
        private int selectedColorIndex = -1;
        public override double DrawOrder => 0.2;
        public override bool DisableMouseGrab => true;

        public MapMarkerIconSettingsGUI(ICoreClientAPI capi, string iconTitle, AutoMapMarkerSetting iconSettings, string[] icons, int[] colors)
            : base("", capi)
        {
            // On some servers/setups the game's lists can come back empty; never open a picker
            // with nothing in it (saving would index an empty array).
            this.icons = icons != null && icons.Length > 0
                ? icons
                : new[] { string.IsNullOrEmpty(iconSettings?.MarkerIcon) ? "circle" : iconSettings.MarkerIcon };
            this.colors = colors != null && colors.Length > 0
                ? colors
                : Egocarib.AutoMapMarkers.Utilities.WaypointUtil.GetDefaultWaypointColorOptions();
            this.iconTitle = iconTitle;
            this.settings = iconSettings;
            ComposeDialog();
        }

        private void ComposeDialog()
        {
            try
            {
                ComposeDialogInternal();
            }
            catch (System.Exception e)
            {
                // A broken dialog must never take the game down with it.
                Egocarib.AutoMapMarkers.Utilities.MessageUtil.LogError($"Could not open the marker icon/color editor: {e}");
                capi.ShowChatMessage("[Auto Map Markers] Could not open the icon/color editor, see client-main.log.");
                SingleComposer?.Dispose();
                SingleComposer = null;
            }
        }

        public override bool TryOpen()
        {
            ComposeDialog();
            return SingleComposer != null && base.TryOpen();
        }

        private void ComposeDialogInternal()
        {
            ElementBounds leftColumn = ElementBounds.Fixed(0.0, 28.0, 160.0, 25.0);
            ElementBounds rightColumn = leftColumn.RightCopy();
            ElementBounds buttonRow = ElementBounds.Fixed(0.0, 28.0, 650.0, 25.0);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(leftColumn, rightColumn);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedAlignmentOffset(0.0 - GuiStyle.DialogToScreenPadding, 0.0);
            if (SingleComposer != null)
                SingleComposer.Dispose();
            int colorIconSize = 22;
            selectedIconIndex = settings.MarkerIcon == null ? -1 : icons.IndexOf(settings.MarkerIcon);
            if (selectedIconIndex < 0)
                selectedIconIndex = 0;
            int? currentColor = settings.MarkerColorIntegerNoAlpha;
            selectedColorIndex = currentColor == null ? 0 : colors.IndexOf(currentColor.Value);
            if (selectedColorIndex < 0 && currentColor != null)
            {
                colors = ArrayExtensions.Append(colors, currentColor.Value);
                selectedColorIndex = colors.Length - 1;
            }
            SingleComposer = capi.Gui.CreateCompo($"{GuiPrefix}-config", dialogBounds)
                .AddShadedDialogBG(bgBounds, withTitleBar: false)
                .AddDialogTitleBar(iconTitle, delegate { TryClose(); })
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get("Name"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.FlatCopy())
                    .AddTextInput(rightColumn = rightColumn.FlatCopy().WithFixedWidth(400.0), OnNameChanged, CairoFont.TextInput(), $"{GuiPrefix}-nameInput")
                    .AddStaticText(Lang.Get("egocarib-mapmarkers:should-pin"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0.0, 18.0))
                    .AddSwitch(OnPinnedToggled, rightColumn = rightColumn.BelowCopy(0.0, 14.0).WithFixedWidth(400.0), $"{GuiPrefix}-pinnedSwitch")
                    .AddRichtext(Lang.Get("waypoint-color"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0.0, 14.0))
                    .AddColorListPicker(colors, OnColorSelected, leftColumn = leftColumn.BelowCopy(0.0, 5.0).WithFixedSize(colorIconSize, colorIconSize), 555, $"{GuiPrefix}-colorPicker")
                    .AddStaticText(Lang.Get("Icon"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.WithFixedPosition(0.0, leftColumn.fixedY + leftColumn.fixedHeight).WithFixedWidth(100.0).BelowCopy())
                    .AddIconListPicker(icons, OnIconSelected, leftColumn = leftColumn.BelowCopy(0.0, 5.0).WithFixedSize(colorIconSize + 5, colorIconSize + 5), 555, $"{GuiPrefix}-iconPicker")
                    .AddSmallButton(Lang.Get("Cancel"), OnCancel, buttonRow.FlatCopy().FixedUnder(leftColumn).WithFixedWidth(100.0))
                    .AddSmallButton(Lang.Get("Save"), OnSave, buttonRow.FlatCopy().FixedUnder(leftColumn).WithFixedWidth(100.0).WithAlignment(EnumDialogArea.RightFixed), EnumButtonStyle.Normal, $"{GuiPrefix}-saveButton")
                .EndChildElements()
                .Compose();
            SingleComposer.ColorListPickerSetValue($"{GuiPrefix}-colorPicker", selectedColorIndex);
            SingleComposer.IconListPickerSetValue($"{GuiPrefix}-iconPicker", selectedIconIndex);
            SingleComposer.GetTextInput($"{GuiPrefix}-nameInput").SetValue(settings.MarkerTitle ?? "");
            SingleComposer.GetSwitch($"{GuiPrefix}-pinnedSwitch").SetValue(settings.MarkerPinned);
            // Coverage radius lives on the marker's row in the main settings list, not here.
        }

        private void OnIconSelected(int index)
        {
            if (index >= 0 && index < icons.Length)
                selectedIconIndex = index;
        }

        private void OnColorSelected(int index)
        {
            if (index >= 0 && index < colors.Length)
                selectedColorIndex = index;
        }

        // Intentionally empty — the pinned value is read directly from the switch when saving
        private void OnPinnedToggled(bool t1)
        {
        }

        private bool OnSave()
        {
            try
            {
                if (selectedIconIndex >= 0 && selectedIconIndex < icons.Length)
                    settings.MarkerIcon = icons[selectedIconIndex];
                if (selectedColorIndex >= 0 && selectedColorIndex < colors.Length)
                    settings.MarkerColor = ColorUtil.Int2Hex(colors[selectedColorIndex]);
                string title = SingleComposer?.GetTextInput($"{GuiPrefix}-nameInput")?.GetText();
                if (!string.IsNullOrWhiteSpace(title))
                    settings.MarkerTitle = title;
                var pinned = SingleComposer?.GetSwitch($"{GuiPrefix}-pinnedSwitch");
                if (pinned != null)
                    settings.MarkerPinned = pinned.On;
            }
            catch (System.Exception e)
            {
                Egocarib.AutoMapMarkers.Utilities.MessageUtil.LogError($"Could not save marker icon/color: {e}");
                capi.ShowChatMessage("[Auto Map Markers] Could not save the icon/color change, see client-main.log.");
            }
            TryClose();
            return true;
        }

        private bool OnCancel()
        {
            TryClose();
            return true;
        }

        private void OnNameChanged(string t1)
        {
            var saveButton = SingleComposer?.GetButton($"{GuiPrefix}-saveButton");
            if (saveButton != null)
                saveButton.Enabled = (t1 ?? "").Trim() != "";
        }

        public override bool CaptureAllInputs()
        {
            return IsOpened();
        }

        public override void OnMouseDown(MouseEvent args)
        {
            base.OnMouseDown(args);
            args.Handled = true;
        }

        public override void OnMouseUp(MouseEvent args)
        {
            base.OnMouseUp(args);
            args.Handled = true;
        }

        public override void OnMouseMove(MouseEvent args)
        {
            base.OnMouseMove(args);
            args.Handled = true;
        }

        public override void OnMouseWheel(MouseWheelEventArgs args)
        {
            base.OnMouseWheel(args);
            args.SetHandled();
        }
    }
}
