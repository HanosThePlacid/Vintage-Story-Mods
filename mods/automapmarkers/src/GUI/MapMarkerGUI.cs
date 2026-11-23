using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Egocarib.AutoMapMarkers.Settings;
using Egocarib.AutoMapMarkers.Utilities;
using Vintagestory.API.Common;
using System.Text.RegularExpressions;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.API.MathTools;

namespace Egocarib.AutoMapMarkers.GUI
{
    using AutoMapMarkerSetting = MapMarkerConfig.Settings.AutoMapMarkerSetting;

    /// <summary>
    /// The Auto Map Marker configuration menu
    /// </summary>
    public class MapMarkerGUI : GuiDialog
    {
        public const string HotkeyCode = "egocarib_MapMarkerGUI";
        public const string DialogID = "egocarib-mapmarkers-config-menu";
        private const double ScrollAreaHeight = 565;
        private const double ScrollAreaPadding = 10;
        public MapMarkerConfig.Settings ModSettings;
        public MarkerSettingLayout Layout;
        public readonly string ExtraSettingsTabName = Lang.Get("egocarib-mapmarkers:ui");
        public string CurrentTab;

        private ElementBounds scrollContentBounds;
        private ElementBounds scrollClipBounds;
        private float currentScrollY;
        private Action RegisterCustomHotkeys;
        private Action RegisterDeleteHotkey;
        private Action RegisterDetectHotkey;
        public string[] icons;
        public string[] iconsVTML;
        public int[] colors;
        private List<IAsset> loadedIconAssets;
        MapMarkerIconSettingsGUI iconConfigPopup;

        public override string ToggleKeyCombinationCode { get { return HotkeyCode; } }

        public override bool DisableMouseGrab { get { return true; } }

        public MapMarkerGUI(ICoreClientAPI capi, Action onRegisterCustomHotkeys, Action onRegisterDeleteHotkey, Action onRegisterDetectHotkey) : base(capi)
        {
            RegisterCustomHotkeys = onRegisterCustomHotkeys;
            RegisterDeleteHotkey = onRegisterDeleteHotkey;
            RegisterDetectHotkey = onRegisterDetectHotkey;
        }

        public override bool CaptureAllInputs()
        {
            return IsOpened();
        }

        /// <summary>
        /// Called when the GUI opens. Loads the player's current map marker settings and then calls SetupDialog() to compose the GUI.
        /// </summary>
        public override void OnGuiOpened()
        {
            try
            {
                ModSettings = MapMarkerConfig.GetSettings(capi);
                Layout = ModSettings.GetMapMarkerSettingLayout();
                if (Layout == null)
                {
                    MapMarkerMod.CoreAPI.Logger.Error("Map Marker Mod: Marker definitions not loaded. Cannot open settings GUI.");
                    return;
                }
                LoadIconTextures();
                LoadColorOptions();

                SetupDialog();
            }
            catch (Exception e)
            {
                MapMarkerMod.CoreAPI.Logger.Error("Map Marker Mod: Failed to initialize settings GUI. (" + e.ToString() + ")");
            }
            base.OnGuiOpened();
        }

        private string GetWaypointIconName(string iconName)
        {
            return $"wp{(string.IsNullOrEmpty(iconName) ? "circle" : iconName).UcFirst()}";
        }

        private void LoadColorOptions()
        {
            List<MapLayer> mapLayers = capi.ModLoader?.GetModSystem<WorldMapManager>()?.MapLayers;
            WaypointMapLayer wml = mapLayers?.FirstOrDefault((MapLayer l) => l is WaypointMapLayer) as WaypointMapLayer;
            if (wml != null)
            {
                colors = wml.WaypointColors.ToArray();
            }
            else
            {
                MessageUtil.Log("Unable to load map color options from the game. Falling back to hard-coded defaults...");
                colors = WaypointUtil.GetDefaultWaypointColorOptions();
            }
        }

        /// <summary>
        /// Loads all the world map waypoint icons, which typically are loaded from the following directory:
        /// %appdata%\Vintagestory\assets\survival\textures\icons\worldmap
        /// Also loads modded icons from the same asset directory of each mod
        /// </summary>
        private void LoadIconTextures()
        {
            // Load (or retrieve cached) icon assets from the asset manager
            loadedIconAssets = capi.Assets.GetMany("textures/icons/worldmap/", null, loadAsset: true);

            List<string> listIcons = new List<string>();
            List<string> listIconsVTML = new List<string>();
            foreach (var icon in loadedIconAssets)
            {
                // Replicate logic the game uses when loading map icons in WaypointMapLayer constructor
                int dotIndex = icon.Name.IndexOf(".");
                if (dotIndex <= 0) continue;
                string name = icon.Name.Substring(0, dotIndex);
                name = Regex.Replace(name, "\\d+\\-", "");
                if (string.IsNullOrEmpty(name)) continue;
                listIcons.Add(name);                                               //example:  bee
                listIconsVTML.Add($"<icon name=\"{GetWaypointIconName(name)}\">"); //example:  <icon name=\"wpBee\">
            }
            icons = listIcons.ToArray();
            iconsVTML = listIconsVTML.ToArray();
        }

        /// <summary>
        /// Release references to the icon assets so that the AssetManager can later dispose of them.
        /// </summary>
        private void UnloadIconTextures()
        {
            loadedIconAssets = null;
        }

        /// <summary>
        /// Composes or re-composes the GUI. Called each time the menu tab changes or an option is toggled off/on
        /// </summary>
        private void SetupDialog()
        {
            afterCompose.Clear();
            CairoFont disabledFont = CairoFont.WhiteSmallishText().Clone().WithSlant(FontSlant.Italic);
            disabledFont.Color[3] = 0.2;

            /*
             *  Dialog bounds nesting structure:
             *
             *  dialogBounds
             *    bgBounds
             *      toggleButtonBarBounds
             *        toggleButtonBounds [multiple]
             *      mainContentBounds
             *        clipBounds (visible viewport, marker tabs only)
             *          markerOptionAreaBounds (scrollable content)
             *            markerOptionRowBounds [multiple]
             *              markerOption* [multiple]
             *        scrollbarBounds (marker tabs only)
             */

            // Determine current tab and whether it needs to be paged
            if (string.IsNullOrEmpty(CurrentTab))
                CurrentTab = Layout.Tabs.Count > 0 ? Layout.Tabs[0].TabKey : "UI";
            bool isMarkerTab = CurrentTab != "UI";

            // Auto-sized dialog at the center of the screen
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            // Fixed position values
            double dialogPadding = 40;
            double xpos = 0;
            double ypos = 2.5;
            double yStart = 0;
            double opAreaHeight = ScrollAreaHeight + ScrollAreaPadding;
            double dgMinWidth = 920;
            double dgMaxWidth = 1080;
            double toggleBarHeight = 76;

            double rowIndent = 20;
            double rowHeight = 42;

            double opPromptWidth = 320;
            double toggleWidth = 64; // 100; //Toggle switch, plus empty space after
            double disabledMsgWidth = 200;
            double iconColorButtonWidth = 70;
            double iconColorButtonHeight = 32;
            double titleBarThickness = 31;
            double dgInnerWidth = opPromptWidth + toggleWidth + iconColorButtonWidth;
            if (dgInnerWidth < dgMinWidth)
                dgInnerWidth = dgMinWidth;
            else if (dgInnerWidth > dgMaxWidth)
                dgInnerWidth = dgMaxWidth;
            double dgWidth = dgInnerWidth + rowIndent * 2 + dialogPadding * 2;

            ElementBounds toggleButtonBarBounds = ElementBounds
                .Fixed(0, titleBarThickness, dgWidth, toggleBarHeight)
                .WithFixedPadding(GuiStyle.ElementToDialogPadding)
                .WithFixedWidth(dgWidth - 2.0 * GuiStyle.ElementToDialogPadding);
            ElementBounds toggleButtonBounds = ElementBounds.Fixed(0, 0, 160, 40).WithFixedPadding(0, 3);


            ElementBounds markerOptionAreaBounds = ElementBounds.Fixed(0, (int)(yStart))
                .WithFixedPadding(rowIndent, 0)
                .WithSizing(ElementSizing.FitToChildren);

            ElementBounds markerOptionRowBounds = ElementBounds.Fixed(0, 0)
                .WithSizing(horizontalSizing: ElementSizing.FitToChildren, verticalSizing: ElementSizing.Fixed)
                .WithFixedHeight(rowHeight); // Single option row

            ElementBounds markerOptionPromptBounds = ElementBounds.Fixed(xpos, ypos, opPromptWidth, rowHeight);
            double toggleClickSize = 34; // Tightly wrap the 30x30 visual switch
            ElementBounds markerOptionToggleBounds = ElementBounds.Fixed(xpos += opPromptWidth, ypos - 2.5, toggleClickSize, toggleClickSize);
            ElementBounds markerOptionDisabledMessage = ElementBounds.Fixed(xpos += toggleWidth, ypos, disabledMsgWidth, rowHeight);
            ElementBounds markerOptionIconColorButtonBounds = ElementBounds.Fixed(xpos, ypos - 3.5, iconColorButtonWidth, iconColorButtonHeight);

            ElementBounds mainContentBounds = ElementBounds.Fixed(0, titleBarThickness + toggleBarHeight, dgWidth - dialogPadding * 2, opAreaHeight)
                .WithFixedPadding(dialogPadding);

            // Scrollable content area bounds
            double clipWidth = dgInnerWidth + rowIndent * 2;
            double clipHeight = ScrollAreaHeight;
            ElementBounds clipBounds = ElementBounds.Fixed(0, 0, clipWidth, clipHeight);
            ElementBounds scrollbarBounds = ElementBounds.Fixed(clipWidth + 5, 0, 20, clipHeight);

            ElementBounds bgBounds = ElementBounds.Fill
                .WithChildren(mainContentBounds, toggleButtonBarBounds)
                .WithSizing(ElementSizing.FitToChildren);

            SingleComposer = capi.Gui.CreateCompo(DialogID, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("egocarib-mapmarkers:config-menu-title"), OnTitleBarCloseClicked)
                .BeginChildElements(bgBounds)
                .AddStaticCustomDraw(toggleButtonBarBounds, delegate (Context ctx, ImageSurface surface, ElementBounds bounds)
                {
                    //This header design copied from Vintagestory.Client.NoObf.GuiCompositeSettings.ComposerHeader
                    ctx.SetSourceRGBA(1.0, 1.0, 1.0, 0.1);
                    GuiElement.RoundRectangle(ctx, GuiElement.scaled(5.0) + bounds.bgDrawX, GuiElement.scaled(5.0) + bounds.bgDrawY, bounds.OuterWidth - GuiElement.scaled(10.0), GuiElement.scaled(75.0), 1.0);
                    ctx.Fill();
                })
                .BeginChildElements(); // start header bar toggle buttons

            // Marker tabs from layout
            foreach (var tab in Layout.Tabs)
            {
                string tabDisplayName = Lang.Get(tab.LangKey);
                string tabKey = tab.TabKey;
                CairoFont buttonFont = CairoFont.ButtonText();
                SingleComposer.AddToggleButton(
                    text: tabDisplayName,
                    font: buttonFont,
                    onToggle: isSelected => OnTabToggle(tabKey),
                    bounds: toggleButtonBounds.WithFixedWidth(GetFontTextWidth(buttonFont, tabDisplayName)),
                    key: tabKey + "-toggle-tab");
                toggleButtonBounds = toggleButtonBounds.RightCopy(15, 0);
            }
            // Custom tab (conditional)
            if (ModSettings.EnableCustomHotkeys)
            {
                string customDisplayName = Lang.Get("egocarib-mapmarkers:custom");
                CairoFont buttonFont = CairoFont.ButtonText();
                SingleComposer.AddToggleButton(
                    text: customDisplayName,
                    font: buttonFont,
                    onToggle: isSelected => OnTabToggle("Custom"),
                    bounds: toggleButtonBounds.WithFixedWidth(GetFontTextWidth(buttonFont, customDisplayName)),
                    key: "Custom-toggle-tab");
                toggleButtonBounds = toggleButtonBounds.RightCopy(15, 0);
            }
            // UI tab (always)
            {
                CairoFont buttonFont = CairoFont.ButtonText();
                SingleComposer.AddToggleButton(
                    text: ExtraSettingsTabName,
                    font: buttonFont,
                    onToggle: isSelected => OnTabToggle("UI"),
                    bounds: toggleButtonBounds.WithFixedWidth(GetFontTextWidth(buttonFont, ExtraSettingsTabName)),
                    key: "UI-toggle-tab");
            }

            SingleComposer.GetToggleButton(CurrentTab + "-toggle-tab").SetValue(true);

            string resetLabel = Lang.Get("egocarib-mapmarkers:reset-defaults");
            SingleComposer
                .AddButton(text: resetLabel,
                    onClick: OnResetDefaultsClicked,
                    bounds: ElementBounds.Fixed(0.0, 0.0, GetFontTextWidth(CairoFont.ButtonText(), resetLabel), 40.0)
                        .WithFixedPadding(4.0, 3.0).WithAlignment(EnumDialogArea.RightTop).WithFixedAlignmentOffset(-90.0, 0.0))
                .AddButton(text: Lang.Get("general-close"),
                    onClick: OnSaveButton,
                    bounds: ElementBounds.Fixed(0.0, 0.0, 80.0, 40.0).WithFixedPadding(4.0, 3.0).WithAlignment(EnumDialogArea.RightTop))
                .EndChildElements(); // end header bar toggle buttons

            if (ModSettings.DisableAllModFeatures)
            {
                string enableLabel = Lang.Get("egocarib-mapmarkers:re-enable-mod");
                SingleComposer
                    .BeginChildElements(mainContentBounds)
                    .AddButton(
                        text: enableLabel,
                        onClick: () =>
                        {
                            ModSettings.DisableAllModFeatures = false;
                            SetupDialog();
                            return true;
                        },
                        bounds: ElementBounds
                            .Fixed(0, 0, GetFontTextWidth(CairoFont.ButtonText(), enableLabel), 40)
                            .WithFixedPadding(20, 5)
                            .WithAlignment(EnumDialogArea.CenterMiddle)
                            .WithFixedAlignmentOffset(0, -100))
                    .EndChildElements() // mainContentBounds
                    .EndChildElements() // bgBounds
                    .Compose();
                return;
            }

            SingleComposer
                .BeginChildElements(mainContentBounds);

            scrollClipBounds = isMarkerTab ? clipBounds : null;
            if (isMarkerTab)
            {
                scrollContentBounds = markerOptionAreaBounds;
                SingleComposer
                    .AddInset(clipBounds, 3)
                    .BeginClip(clipBounds);
            }
            SingleComposer.BeginChildElements(markerOptionAreaBounds);

            ComposeMarkerOptions(ref markerOptionRowBounds, rowHeight, dgInnerWidth, disabledFont,
                markerOptionPromptBounds, markerOptionToggleBounds, markerOptionDisabledMessage,
                markerOptionIconColorButtonBounds, markerOptionAreaBounds);

            if (CurrentTab == "UI")
            {
                ComposeExtraSettingsTab(ref markerOptionRowBounds, rowHeight);
            }

            SingleComposer
                .EndChildElements(); // markerOptionAreaBounds
            if (isMarkerTab)
            {
                SingleComposer
                    .EndClip()
                    .AddVerticalScrollbar(OnNewScrollbarValue, scrollbarBounds, "scrollbar");
            }
            SingleComposer
                .EndChildElements() // mainContentBounds
                .EndChildElements() // bgBounds
                .Compose();

            foreach (var action in afterCompose)
            {
                try { action(); }
                catch (Exception e) { MessageUtil.LogError($"Settings menu setup step failed: {e}"); }
            }
            afterCompose.Clear();

            if (isMarkerTab)
            {
                float visibleHeight = (float)clipHeight;
                float totalContentHeight = CalculateTabContentHeight(rowHeight);
                float savedScrollY = currentScrollY;
                var scrollbar = SingleComposer.GetScrollbar("scrollbar");
                scrollbar?.SetHeights(visibleHeight, totalContentHeight);
                if (savedScrollY > 0)
                {
                    scrollbar.CurrentYPosition = savedScrollY;
                    scrollbar.TriggerChanged();
                }
            }
        }

        /// <summary>
        /// Calculates total content height for the current marker tab.
        /// </summary>
        private float CalculateTabContentHeight(double rowHeight)
        {
            if (CurrentTab == "Custom")
            {
                return (float)((Layout.CustomEntries.Count * rowHeight) + (rowHeight * 0.5));
            }

            var tab = Layout.Tabs.FirstOrDefault(t => t.TabKey == CurrentTab);
            if (tab == null) return 0;

            int totalRows = 0;
            foreach (var sg in tab.Subgroups)
            {
                foreach (var entry in sg.Entries)
                {
                    totalRows++; // parent/header row
                    if (entry.IsExpandable && entry.IsExpanded)
                        totalRows += entry.SubEntries?.Count ?? 0;
                }
            }
            double extraHeight = 0;
            if (tab.HasSubheadings)
            {
                totalRows += tab.Subgroups.Count; // one row per subheading
                extraHeight = (tab.Subgroups.Count - 1) * rowHeight * 0.4; // spacing between subgroups
            }
            totalRows++; // "all markers in this tab" coverage radius row
            return (float)(totalRows * rowHeight + extraHeight + 9); // +9 for inset top padding
        }

        /// <summary>
        /// Composes the marker option rows for the currently selected marker tab.
        /// Handles: layout tabs with subheadings, custom tab, expandable entries.
        /// </summary>
        private void ComposeMarkerOptions(ref ElementBounds markerOptionRowBounds, double rowHeight, double dgInnerWidth,
            CairoFont disabledFont,
            ElementBounds markerOptionPromptBounds, ElementBounds markerOptionToggleBounds,
            ElementBounds markerOptionDisabledMessage, ElementBounds markerOptionIconColorButtonBounds,
            ElementBounds markerOptionAreaBounds)
        {
            // Custom tab
            if (CurrentTab == "Custom")
            {
                if (!ModSettings.EnableCustomHotkeys)
                    return;

                SingleComposer.BeginChildElements(markerOptionRowBounds = markerOptionRowBounds.FlatCopy().WithFixedHeight(rowHeight * 1.5))
                .AddDynamicText(
                    text: Lang.Get("egocarib-mapmarkers:custom-gui-description"),
                    font: CairoFont.WhiteSmallishText(),
                    bounds: ElementBounds.Fixed(0, 0, dgInnerWidth, rowHeight * 1.5),
                    key: "custom-tab-description")
                .EndChildElements();
                markerOptionRowBounds = markerOptionRowBounds.BelowCopy().WithFixedHeight(rowHeight);

                // Custom markers are always offered in the same fixed order (1, 2, 3).
                // Use a stable, index-based entryKey instead of customEntry.Key (which is a
                // localized string and may collide across translations).
                int customIndex = 1;
                foreach (var customEntry in Layout.CustomEntries)
                {
                    ComposeMarkerRow(customEntry.Key, customEntry.Value, ref markerOptionRowBounds, disabledFont,
                        markerOptionPromptBounds, markerOptionToggleBounds, markerOptionDisabledMessage,
                        markerOptionIconColorButtonBounds,
                        entryKey: "custom-marker-" + customIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    customIndex++;
                }
                return;
            }

            // Layout tabs
            var tab = Layout.Tabs.FirstOrDefault(t => t.TabKey == CurrentTab);
            if (tab == null)
                return;

            // Add top padding inside the inset before the first row
            markerOptionRowBounds = markerOptionRowBounds.FlatCopy().WithFixedHeight(9);
            markerOptionRowBounds = markerOptionRowBounds.BelowCopy().WithFixedHeight(rowHeight);

            radiusRowKeys.Clear();
            // Whole tab at once: set every marker's coverage radius, then tweak single rows below.
            SingleComposer.BeginChildElements(markerOptionRowBounds = markerOptionRowBounds.FlatCopy().WithFixedHeight(rowHeight))
            .AddDynamicText(
                text: Lang.Get("egocarib-mapmarkers:radius-all-in-tab"),
                font: CairoFont.WhiteSmallishText().Clone().WithWeight(FontWeight.Bold),
                bounds: markerOptionPromptBounds.FlatCopy().WithParent(markerOptionRowBounds),
                key: "group-radius-tab-label");
            ComposeGroupRadiusSlider("group-radius-tab", CollectSettings(tab.Subgroups), markerOptionRowBounds, markerOptionIconColorButtonBounds);
            SingleComposer.EndChildElements();
            markerOptionRowBounds = markerOptionRowBounds.BelowCopy().WithFixedHeight(rowHeight);

            bool isFirstSubgroup = true;
            foreach (var subgroup in tab.Subgroups)
            {
                // Render subheading if tab has multiple subgroups
                if (tab.HasSubheadings && subgroup.SubgroupName != null)
                {
                    // Add vertical spacing before non-first subheadings
                    if (!isFirstSubgroup)
                    {
                        markerOptionRowBounds = markerOptionRowBounds.FlatCopy().WithFixedHeight(rowHeight * 0.4);
                        markerOptionRowBounds = markerOptionRowBounds.BelowCopy().WithFixedHeight(rowHeight);
                    }

                    string subheadingText = Lang.Get("egocarib-mapmarkers:" + subgroup.SubgroupName.ToLowerInvariant().Replace(" ", "-"));
                    CairoFont subheadingFont = CairoFont.WhiteSmallishText().Clone().WithWeight(FontWeight.Bold);
                    SingleComposer.BeginChildElements(markerOptionRowBounds = markerOptionRowBounds.FlatCopy().WithFixedHeight(rowHeight))
                    .AddDynamicText(
                        text: subheadingText,
                        font: subheadingFont,
                        bounds: ElementBounds.Fixed(0, rowHeight * 0.25, dgInnerWidth, rowHeight).WithParent(markerOptionRowBounds),
                        key: "subheading-" + subgroup.SubgroupName);
                    // Whole subgroup at once, on the subheading row (same column as the row sliders).
                    ComposeGroupRadiusSlider("group-radius-" + subgroup.SubgroupName,
                        CollectSettings(new[] { subgroup }), markerOptionRowBounds,
                        markerOptionIconColorButtonBounds.FlatCopy().WithFixedOffset(15, 0));
                    SingleComposer.EndChildElements();
                    markerOptionRowBounds = markerOptionRowBounds.BelowCopy().WithFixedHeight(rowHeight);
                    isFirstSubgroup = false;
                }

                // Indent entries under subheadings
                double subIndent = tab.HasSubheadings ? 15 : 0;
                ElementBounds promptBounds = subIndent > 0 ? markerOptionPromptBounds.FlatCopy().WithFixedOffset(subIndent, 0) : markerOptionPromptBounds;
                ElementBounds toggleBounds = subIndent > 0 ? markerOptionToggleBounds.FlatCopy().WithFixedOffset(subIndent, 0) : markerOptionToggleBounds;
                ElementBounds disabledBounds = subIndent > 0 ? markerOptionDisabledMessage.FlatCopy().WithFixedOffset(subIndent, 0) : markerOptionDisabledMessage;
                ElementBounds iconBounds = subIndent > 0 ? markerOptionIconColorButtonBounds.FlatCopy().WithFixedOffset(subIndent, 0) : markerOptionIconColorButtonBounds;

                foreach (var entry in subgroup.Entries)
                {
                    if (entry.IsExpandable)
                    {
                        ComposeExpandableMarkerRow(entry, ref markerOptionRowBounds, rowHeight, disabledFont,
                            promptBounds, toggleBounds, disabledBounds, iconBounds);
                    }
                    else
                    {
                        ComposeMarkerRow(entry.DisplayName, entry.Setting, ref markerOptionRowBounds, disabledFont,
                            promptBounds, toggleBounds, disabledBounds, iconBounds,
                            entryKey: entry.EntryLabel);
                    }
                }
            }
        }

        /// <summary>
        /// Composes an expandable marker entry. When collapsed, renders as a normal entry row
        /// with an expand (+) button after the icon/color button. When expanded, shows a header
        /// row with a collapse (-) button near the label, then indented sub-entries below.
        /// </summary>
        private void ComposeExpandableMarkerRow(MarkerSettingEntry entry,
            ref ElementBounds markerOptionRowBounds, double rowHeight, CairoFont disabledFont,
            ElementBounds promptBounds, ElementBounds toggleBounds,
            ElementBounds disabledBounds, ElementBounds iconBounds)
        {
            var capturedEntry = entry;
            double expandBtnSize = 32; // Same height as icon/color button
            double expandBtnGap = 10;  // Gap between icon/color button and expand button

            if (entry.IsExpanded)
            {
                // Header row: label + collapse button at toggle position
                ElementBounds headerLabelBounds = promptBounds.FlatCopy().WithParent(markerOptionRowBounds);
                ElementBounds collapseButtonBounds = toggleBounds.FlatCopy().WithParent(markerOptionRowBounds)
                    .WithFixedSize(expandBtnSize, expandBtnSize);

                SingleComposer.BeginChildElements(markerOptionRowBounds)
                .AddDynamicText(
                    text: entry.DisplayName,
                    font: CairoFont.WhiteSmallishText(),
                    bounds: headerLabelBounds,
                    key: entry.EntryLabel + "-expand-header")
                .AddExpandButton(
                    onClick: () => { ToggleExpand(capturedEntry); },
                    expand: false,
                    bounds: collapseButtonBounds,
                    key: entry.EntryLabel + "-collapse-btn")
                .EndChildElements();
                markerOptionRowBounds = markerOptionRowBounds.BelowCopy();

                // Sub-entries indented
                double subIndent = 25;
                ElementBounds subPrompt = promptBounds.FlatCopy().WithFixedOffset(subIndent, 0);
                ElementBounds subToggle = toggleBounds.FlatCopy().WithFixedOffset(subIndent, 0);
                ElementBounds subDisabled = disabledBounds.FlatCopy().WithFixedOffset(subIndent, 0);
                ElementBounds subIcon = iconBounds.FlatCopy().WithFixedOffset(subIndent, 0);

                string subKeyPrefix = entry.EntryLabel + "-";
                foreach (var sub in entry.SubEntries)
                {
                    // Build the GUI key from parent.EntryLabel + sub.EntryLabel rather than
                    // sub.DisplayName. The localized DisplayName can collide between sibling
                    // sub-entries (e.g. two olivine ore variants both resolve to
                    // "олівінової руди" in Ukrainian), which triggers
                    // ArgumentException: "An item with the same key has already been added".
                    ComposeMarkerRow(sub.DisplayName, sub.Setting, ref markerOptionRowBounds, disabledFont,
                        subPrompt, subToggle, subDisabled, subIcon,
                        entryKey: subKeyPrefix + sub.EntryLabel);
                }
            }
            else
            {
                // Collapsed: normal entry row + expand button after icon/color button
                if (entry.Setting != null)
                {
                    // Position expand button after the disabled text area (wider than icon button)
                    // so it doesn't overlap when the entry is disabled
                    double expandButtonX = iconBounds.fixedX + iconBounds.fixedWidth + expandBtnGap + 15;
                    ElementBounds expandButtonBounds = ElementBounds.Fixed(expandButtonX, iconBounds.fixedY, expandBtnSize, expandBtnSize);

                    // Capture current row bounds before ref is modified by ComposeMarkerRow
                    var currentRowBounds = markerOptionRowBounds;
                    ComposeMarkerRow(entry.DisplayName, entry.Setting, ref markerOptionRowBounds, disabledFont,
                        promptBounds, toggleBounds, disabledBounds, iconBounds,
                        entryKey: entry.EntryLabel,
                        expandButton: () => {
                            SingleComposer.AddExpandButton(
                                onClick: () => { ToggleExpand(capturedEntry); },
                                expand: true,
                                bounds: expandButtonBounds.FlatCopy().WithParent(currentRowBounds),
                                key: entry.EntryLabel + "-expand-btn");
                        });
                }
            }
        }

        /// <summary>
        /// Toggles the expand/collapse state of an expandable entry.
        /// Invalidates layout cache and rebuilds the registry immediately.
        /// </summary>
        private void ToggleExpand(MarkerSettingEntry entry)
        {
            entry.IsExpanded = !entry.IsExpanded;
            ModSettings.ExpandStates[entry.EntryLabel] = entry.IsExpanded;
            ModSettings.InvalidateLayout();
            MapMarkerConfig.RebuildRegistry();
            Layout = ModSettings.GetMapMarkerSettingLayout();
            // Clamp scroll position if collapsing made it exceed the new content height
            if (!entry.IsExpanded)
            {
                float newContentHeight = CalculateTabContentHeight(42);
                float visibleHeight = (float)ScrollAreaHeight;
                float maxScroll = Math.Max(0, newContentHeight - visibleHeight);
                if (currentScrollY > maxScroll)
                    currentScrollY = maxScroll;
            }
            SetupDialog();
        }

        /// <summary>
        /// Composes a single marker option row (label + toggle + icon button or disabled text).
        /// </summary>
        /// <param name="markerSettingTitle">Localized display text shown to the user. NOT used as a GUI key.</param>
        /// <param name="entryKey">Stable, unique identifier (typically the marker definition's Label)
        /// used as the base for all GUI element keys registered for this row. Must be unique per row.
        /// Falls back to <paramref name="markerSettingTitle"/> only when null/empty, which preserves
        /// backward compatibility for any external caller that did not pass an explicit key.</param>
        private void ComposeMarkerRow(string markerSettingTitle, AutoMapMarkerSetting markerSetting,
            ref ElementBounds markerOptionRowBounds, CairoFont disabledFont,
            ElementBounds markerOptionPromptBounds, ElementBounds markerOptionToggleBounds,
            ElementBounds markerOptionDisabledMessage, ElementBounds markerOptionIconColorButtonBounds,
            string entryKey = null, Action expandButton = null)
        {
            // IMPORTANT: GUI element keys must be derived from a stable, unique identifier
            // (the marker definition's Label) — never from the localized DisplayName.
            // Different marker definitions can resolve to the same localized string in some
            // languages (e.g. surface olivine ore "game:block-looseores-olivine-peridotite-*"
            // and deep olivine ore "game:ore-olivine" both translate to "олівінової руди"
            // in Ukrainian). Building keys from the DisplayName caused
            // System.ArgumentException: An item with the same key has already been added.
            //   Key: олівінової руди-auto-markers-label
            // when the second row tried to register under the same key.
            string keyBase = !string.IsNullOrEmpty(entryKey) ? entryKey : markerSettingTitle;
            double[] markerColorRGBADoubles = markerSetting.MarkerColorInteger.HasValue
                ? ColorUtil.ToRGBADoubles(markerSetting.MarkerColorInteger.Value)
                : new double[] { 1.0, 1.0, 1.0, 1.0 };

            ElementBounds labelBounds = markerOptionPromptBounds.FlatCopy().WithParent(markerOptionRowBounds);

            SingleComposer.BeginChildElements(markerOptionRowBounds)

            .AddDynamicText(
                text: markerSettingTitle,
                font: CairoFont.WhiteSmallishText(),
                bounds: labelBounds,
                key: keyBase + "-auto-markers-label")

            .AddInteractiveSwitch(
                onToggle: OnMarkerToggleEnabled,
                bounds: markerOptionToggleBounds.FlatCopy().WithParent(markerOptionRowBounds),
                key: keyBase + "-auto-markers-enabled");

            // Add expand button first (before disabled text) to test click dispatch order
            expandButton?.Invoke();

            SingleComposer
            .AddIf(markerSetting.Enabled == true)

                .AddColorIconButton(
                    icon: GetWaypointIconName(markerSetting.MarkerIcon),
                    iconColor: markerColorRGBADoubles,
                    onClick: () => { return OnIconColorButtonClick(CurrentTab, markerSettingTitle, markerSetting); },
                    bounds: markerOptionIconColorButtonBounds.FlatCopy().WithParent(markerOptionRowBounds),
                    style: EnumButtonStyle.Normal,
                    key: keyBase + "-auto-markers-icon")

                // Coverage radius: no new marker of this type within this many blocks of an existing one.
                // Sits right of the icon/color button, past where an expand (+) button can appear.
                .AddScrollSafeSlider(
                    onNewSliderValue: index =>
                    {
                        int[] steps = GetRadiusSteps(markerSetting);
                        int radius = steps[GameMath.Clamp(index, 0, steps.Length - 1)];
                        markerSetting.MarkerCoverageRadius = radius;
                        SetRadiusInputText(keyBase + "-auto-markers-radius-input", radius.ToString());
                        return true;
                    },
                    bounds: ElementBounds.Fixed(
                            markerOptionIconColorButtonBounds.fixedX + RadiusSliderOffsetX,
                            markerOptionIconColorButtonBounds.fixedY + 6,
                            RadiusSliderWidth, 20)
                        .WithParent(markerOptionRowBounds),
                    key: keyBase + "-auto-markers-radius")

                // Exact value: type any radius instead of using the power-of-two slider.
                .AddScrollSafeNumberInput(
                    bounds: RadiusInputBounds(markerOptionIconColorButtonBounds).WithParent(markerOptionRowBounds),
                    onTextChanged: text =>
                    {
                        if (radiusInputUpdating || !TryParseRadius(text, out int typed)) return;
                        markerSetting.MarkerCoverageRadius = ClampRadius(typed, markerSetting);
                        ConfigureStepSlider(SingleComposer.GetSlider(keyBase + "-auto-markers-radius"),
                            GetRadiusSteps(markerSetting), markerSetting.MarkerCoverageRadius);
                    },
                    clipBounds: scrollClipBounds,
                    key: keyBase + "-auto-markers-radius-input")

                .AddDynamicText(
                    text: Lang.Get("egocarib-mapmarkers:radius-unit"),
                    font: CairoFont.WhiteSmallText(),
                    bounds: RadiusUnitBounds(markerOptionIconColorButtonBounds).WithParent(markerOptionRowBounds),
                    key: keyBase + "-auto-markers-radius-unit")

            .EndIf()
            .AddIf(markerSetting.Enabled == false)

                .AddDynamicText(
                    text: Lang.Get("egocarib-mapmarkers:disabled"),
                    font: disabledFont,
                    bounds: markerOptionDisabledMessage.FlatCopy().WithParent(markerOptionRowBounds),
                    key: keyBase + "-auto-markers-disabled")

            .EndIf();

            SingleComposer.EndChildElements(); // markerOptionRowBounds

            SingleComposer.GetInteractiveSwitch(keyBase + "-auto-markers-enabled").SetValue(markerSetting.Enabled);

            if (markerSetting.Enabled)
            {
                radiusRowKeys[markerSetting] = keyBase;
                ConfigureStepSlider(SingleComposer.GetSlider(keyBase + "-auto-markers-radius"),
                    GetRadiusSteps(markerSetting), markerSetting.MarkerCoverageRadius);
                string inputKey = keyBase + "-auto-markers-radius-input";
                afterCompose.Add(() =>
                {
                    SetRadiusInputText(inputKey, markerSetting.MarkerCoverageRadius.ToString());
                    if (SingleComposer.GetTextInput(inputKey) is GuiElementScrollSafeNumberInput input)
                    {
                        // Done typing: show the value actually stored (clamped, or restored if left empty).
                        input.OnCommit = () => SetRadiusInputText(inputKey, markerSetting.MarkerCoverageRadius.ToString());
                    }
                });
            }

            markerOptionRowBounds = markerOptionRowBounds.BelowCopy();
        }

        private const double RadiusSliderOffsetX = 145; // icon/color button (70) + gap + expand button (32) + gap
        private const double RadiusSliderWidth = 230;
        private const double RadiusInputWidth = 52;

        private static ElementBounds RadiusInputBounds(ElementBounds iconColumnBounds)
        {
            return ElementBounds.Fixed(
                iconColumnBounds.fixedX + RadiusSliderOffsetX + RadiusSliderWidth + 12,
                iconColumnBounds.fixedY + 3,
                RadiusInputWidth, 26);
        }

        private static ElementBounds RadiusUnitBounds(ElementBounds iconColumnBounds)
        {
            return ElementBounds.Fixed(
                iconColumnBounds.fixedX + RadiusSliderOffsetX + RadiusSliderWidth + 12 + RadiusInputWidth + 6,
                iconColumnBounds.fixedY + 6,
                60, 24);
        }

        /// <summary>True while the code (not the player) is changing a radius box, so its change handler stays quiet.</summary>
        private bool radiusInputUpdating;

        /// <summary>Setup that needs composed elements (text boxes), run right after Compose().</summary>
        private readonly List<Action> afterCompose = new List<Action>();

        private void SetRadiusInputText(string key, string text)
        {
            var input = SingleComposer?.GetTextInput(key);
            if (input == null || input.HasFocus) return; // never overwrite what the player is typing
            radiusInputUpdating = true;
            try { input.SetValue(text); }
            finally { radiusInputUpdating = false; }
        }

        private static bool TryParseRadius(string text, out int radius)
        {
            return int.TryParse(text?.Trim(), out radius) && radius >= 1;
        }

        private static int ClampRadius(int radius, AutoMapMarkerSetting setting)
        {
            return GameMath.Clamp(radius, 1, Math.Max(1, setting.MaxCoverageRadius));
        }

        private static string FormatRadius(int radius)
        {
            return radius + " blocks";
        }

        /// <summary>
        /// Coverage radius choices: powers of two (2..512), easier to hit than a 1-block slider.
        /// Values saved before this change (e.g. 40, 500) are kept as they are until the slider moves.
        /// </summary>
        private static readonly int[] RadiusPowers = { 2, 4, 8, 16, 32, 64, 128, 256, 512 };

        /// <summary>Enabled marker setting -> its row's key base, so group sliders can update the rows.</summary>
        private readonly Dictionary<AutoMapMarkerSetting, string> radiusRowKeys = new Dictionary<AutoMapMarkerSetting, string>();

        private static int[] GetRadiusSteps(int maxRadius)
        {
            int[] steps = RadiusPowers.Where(p => p <= maxRadius).ToArray();
            return steps.Length > 0 ? steps : new[] { Math.Max(1, maxRadius) };
        }

        // Never cap below the current value, so a radius saved by a hand-edited settings file
        // (or an older definition) isn't lost when the menu opens.
        private static int[] GetRadiusSteps(AutoMapMarkerSetting setting)
        {
            return GetRadiusSteps(Math.Max(setting.MaxCoverageRadius, setting.MarkerCoverageRadius));
        }

        /// <summary>Index of the step closest to value on a log scale (40 -> 32, 48 -> 64).</summary>
        private static int NearestStepIndex(int[] steps, int value)
        {
            double target = Math.Log(Math.Max(1, value));
            int best = 0;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < steps.Length; i++)
            {
                double distance = Math.Abs(Math.Log(steps[i]) - target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        private static void ConfigureStepSlider(GuiElementSlider slider, int[] steps, int currentValue)
        {
            if (slider == null) return;
            slider.OnSliderTooltip = index => FormatRadius(steps[GameMath.Clamp(index, 0, steps.Length - 1)]);
            slider.SetValues(NearestStepIndex(steps, currentValue), 0, Math.Max(1, steps.Length - 1), 1);
        }

        private void SetRowRadiusSlider(string keyBase, AutoMapMarkerSetting setting)
        {
            ConfigureStepSlider(SingleComposer.GetSlider(keyBase + "-auto-markers-radius"),
                GetRadiusSteps(setting), setting.MarkerCoverageRadius);
            SetRadiusInputText(keyBase + "-auto-markers-radius-input", setting.MarkerCoverageRadius.ToString());
        }

        /// <summary>Every marker setting in these subgroups: collapsed parents and their sub-entries.</summary>
        private static List<AutoMapMarkerSetting> CollectSettings(IEnumerable<MarkerSettingSubgroup> subgroups)
        {
            var result = new List<AutoMapMarkerSetting>();
            foreach (var subgroup in subgroups)
            {
                foreach (var entry in subgroup.Entries)
                {
                    if (entry.Setting != null && !result.Contains(entry.Setting))
                        result.Add(entry.Setting);
                    if (entry.SubEntries == null) continue;
                    foreach (var sub in entry.SubEntries)
                    {
                        if (sub.Setting != null && !result.Contains(sub.Setting))
                            result.Add(sub.Setting);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Adds a slider + value label that sets the coverage radius of every marker in the group at once
        /// (each clamped to that marker's own maximum). Must be called inside BeginChildElements(rowBounds).
        /// </summary>
        private void ComposeGroupRadiusSlider(string key, List<AutoMapMarkerSetting> members,
            ElementBounds rowBounds, ElementBounds iconColumnBounds)
        {
            if (members.Count == 0) return;

            int groupMax = members.Max(m => Math.Max(m.MaxCoverageRadius, m.MarkerCoverageRadius));
            int[] steps = GetRadiusSteps(groupMax);
            bool allSame = members.All(m => m.MarkerCoverageRadius == members[0].MarkerCoverageRadius);

            string inputKey = key + "-input";
            void ApplyToGroup(int radius)
            {
                foreach (var member in members)
                {
                    member.MarkerCoverageRadius = ClampRadius(radius, member);
                    if (radiusRowKeys.TryGetValue(member, out string rowKey))
                        SetRowRadiusSlider(rowKey, member);
                }
            }
            string GroupText() => members.All(m => m.MarkerCoverageRadius == members[0].MarkerCoverageRadius)
                ? members[0].MarkerCoverageRadius.ToString()
                : "";

            SingleComposer
            .AddScrollSafeSlider(
                onNewSliderValue: index =>
                {
                    int radius = steps[GameMath.Clamp(index, 0, steps.Length - 1)];
                    ApplyToGroup(radius);
                    SetRadiusInputText(inputKey, GroupText());
                    return true;
                },
                bounds: ElementBounds.Fixed(
                        iconColumnBounds.fixedX + RadiusSliderOffsetX,
                        iconColumnBounds.fixedY + 6,
                        RadiusSliderWidth, 20)
                    .WithParent(rowBounds),
                key: key)
            .AddScrollSafeNumberInput(
                bounds: RadiusInputBounds(iconColumnBounds).WithParent(rowBounds),
                onTextChanged: text =>
                {
                    if (radiusInputUpdating || !TryParseRadius(text, out int typed)) return;
                    ApplyToGroup(typed);
                    ConfigureStepSlider(SingleComposer.GetSlider(key), steps, typed);
                },
                clipBounds: scrollClipBounds,
                key: inputKey)
            .AddDynamicText(
                text: Lang.Get("egocarib-mapmarkers:radius-unit"),
                font: CairoFont.WhiteSmallText(),
                bounds: RadiusUnitBounds(iconColumnBounds).WithParent(rowBounds),
                key: key + "-unit");

            afterCompose.Add(() =>
            {
                if (SingleComposer.GetTextInput(inputKey) is GuiElementScrollSafeNumberInput input)
                {
                    // Empty box + "mixed" placeholder when the markers in the group differ.
                    input.SetPlaceHolderText(Lang.Get("egocarib-mapmarkers:radius-mixed"));
                    input.OnCommit = () => SetRadiusInputText(inputKey, GroupText());
                }
                SetRadiusInputText(inputKey, GroupText());
            });

            int shown = allSame ? members[0].MarkerCoverageRadius
                : members.Select(m => m.MarkerCoverageRadius).OrderBy(r => r).ElementAt(members.Count / 2); // median
            ConfigureStepSlider(SingleComposer.GetSlider(key), steps, shown);
        }

        /// <summary>
        /// The marker rows now carry radius sliders, which would otherwise grab the mouse wheel
        /// whenever the cursor passes over one while scrolling the list. Over the list area the
        /// wheel always scrolls the list.
        /// </summary>
        public override void OnMouseWheel(MouseWheelEventArgs args)
        {
            var scrollbar = SingleComposer?.GetScrollbar("scrollbar");
            if (scrollbar != null && scrollClipBounds != null
                && scrollClipBounds.PointInside(capi.Input.MouseX, capi.Input.MouseY))
            {
                float visibleHeight = (float)ScrollAreaHeight;
                float maxScroll = Math.Max(0, CalculateTabContentHeight(42) - visibleHeight);
                float step = 42 * args.deltaPrecise; // one row per wheel notch, in the same unscaled units as currentScrollY
                float target = Math.Clamp(scrollbar.CurrentYPosition - step, 0, maxScroll);
                scrollbar.CurrentYPosition = target;
                scrollbar.TriggerChanged();
                args.SetHandled(true);
                return;
            }
            base.OnMouseWheel(args);
        }

        /// <summary>
        /// Composes the "UI" extra settings tab with toggle switches for mod options.
        /// </summary>
        private void ComposeExtraSettingsTab(ref ElementBounds markerOptionRowBounds, double rowHeight)
        {
            ElementBounds uiToggleBounds = ElementBounds.Fixed(0, 0, 60, rowHeight);
            ElementBounds uiToggleLabelBounds = ElementBounds.Fixed(60, 2.5, 800, rowHeight);
            int hotkeyTooltipWidth = (int)(GetFontTextWidth(CairoFont.WhiteSmallText(), Lang.Get("egocarib-mapmarkers:show-chat-message-hotkey-tooltip")) / 2 + 25);
            string hotkeyTooltipText = Lang.Get("egocarib-mapmarkers:show-chat-message-hotkey-tooltip").Replace(">", "&gt;");

            SingleComposer.BeginChildElements(markerOptionRowBounds)

                // Interact with object to trigger creation of a map marker
                .AddSwitch(
                    onToggle: isSelected => {
                        ModSettings.EnableMarkOnInteract = isSelected;
                        if (!ModSettings.EnableMarkOnInteract && !ModSettings.EnableMarkOnSneak && !ModSettings.EnableDetectHotkey)
                        {
                            ModSettings.EnableMarkOnInteract = true; // Force at least one option to be chosen
                            SetupDialog();
                        }
                    },
                    bounds: uiToggleBounds.FlatCopy().WithParent(markerOptionRowBounds),
                    key: "toggle-mark-style-interact")
                .AddStaticText(
                    text: Lang.Get("egocarib-mapmarkers:mark-style-interact"),
                    font: CairoFont.WhiteSmallishText(),
                    bounds: uiToggleLabelBounds.FlatCopy().WithParent(markerOptionRowBounds))

            .EndChildElements()
            .BeginChildElements(markerOptionRowBounds = markerOptionRowBounds.BelowCopy())

                // Sneak while looking at object to trigger creation of a map marker
                .AddSwitch(
                    onToggle: isSelected => {
                        ModSettings.EnableMarkOnSneak = isSelected;
                        if (!ModSettings.EnableMarkOnSneak && !ModSettings.EnableMarkOnInteract && !ModSettings.EnableDetectHotkey)
                        {
                            ModSettings.EnableMarkOnSneak = true; // Force at least one option to be chosen
                            SetupDialog();
                        }
                    },
                    bounds: uiToggleBounds.FlatCopy().WithParent(markerOptionRowBounds),
                    key: "toggle-mark-style-sneak")
                .AddStaticText(
                    text: Lang.Get("egocarib-mapmarkers:mark-style-sneak"),
                    font: CairoFont.WhiteSmallishText(),
                    bounds: uiToggleLabelBounds.FlatCopy().WithParent(markerOptionRowBounds))

            .EndChildElements()
            .BeginChildElements(markerOptionRowBounds = markerOptionRowBounds.BelowCopy())

                // Mark map by pressing a hotkey while looking at object
                .AddSwitch(
                    onToggle: isSelected =>
                    {
                        ModSettings.EnableDetectHotkey = isSelected;
                        if (!ModSettings.EnableDetectHotkey && !ModSettings.EnableMarkOnInteract && !ModSettings.EnableMarkOnSneak)
                        {
                            ModSettings.EnableDetectHotkey = true; // Force at least one option to be chosen
                        }
                        RegisterDetectHotkey();
                        SetupDialog();
                    },
                    bounds: uiToggleBounds = uiToggleBounds.FlatCopy().WithParent(markerOptionRowBounds),
                    key: "toggle-enable-detect-hotkey")
                .AddStaticText(
                    text: Lang.Get("egocarib-mapmarkers:enable-detect-hotkey"),
                    font: CairoFont.WhiteSmallishText(),
                    bounds: uiToggleLabelBounds = uiToggleLabelBounds.FlatCopy().WithParent(markerOptionRowBounds))
                .AddHoverText(
                    text: hotkeyTooltipText,
                    font: CairoFont.WhiteSmallText(),
                    width: hotkeyTooltipWidth,
                    bounds: uiToggleLabelBounds.FlatCopy())

            .EndChildElements()
            .BeginChildElements(markerOptionRowBounds = markerOptionRowBounds.BelowCopy())

                // Chat messages for waypoint creation enabled/disabled
                .AddSwitch(
                    onToggle: isSelected => { ModSettings.ChatNotifyOnWaypointCreation = isSelected; },
                    bounds: uiToggleBounds.FlatCopy().WithParent(markerOptionRowBounds),
                    key: "toggle-show-create-chat-message")
                .AddStaticText(
                    text: Lang.Get("egocarib-mapmarkers:show-chat-message"),
                    font: CairoFont.WhiteSmallishText(),
                    bounds: uiToggleLabelBounds.FlatCopy().WithParent(markerOptionRowBounds))

            .EndChildElements()
            .BeginChildElements(markerOptionRowBounds = markerOptionRowBounds.BelowCopy())

                // Include coordinates in marker labels enabled/disabled
                .AddSwitch(
                    onToggle: isSelected => { ModSettings.LabelCoordinates = isSelected; },
                    bounds: uiToggleBounds.FlatCopy().WithParent(markerOptionRowBounds),
                    key: "toggle-label-coordinates")
                .AddStaticText(
                    text: Lang.Get("egocarib-mapmarkers:include-coordinates"),
                    font: CairoFont.WhiteSmallishText(),
                    bounds: uiToggleLabelBounds.FlatCopy().WithParent(markerOptionRowBounds))

            .EndChildElements()
            .BeginChildElements(markerOptionRowBounds = markerOptionRowBounds.BelowCopy())

                // Suppress markers on farmland enabled/disabled
                .AddSwitch(
                    onToggle: isSelected => { ModSettings.SuppressMarkerOnFarmland = isSelected; },
                    bounds: uiToggleBounds.FlatCopy().WithParent(markerOptionRowBounds),
                    key: "toggle-suppress-farmland")
                .AddStaticText(
                    text: Lang.Get("egocarib-mapmarkers:suppress-farmland"),
                    font: CairoFont.WhiteSmallishText(),
                    bounds: uiToggleLabelBounds.FlatCopy().WithParent(markerOptionRowBounds))

            .EndChildElements()
            .BeginChildElements(markerOptionRowBounds = markerOptionRowBounds.BelowCopy())

                // Chat messages for boat marker creation/deletion enabled/disabled
                .AddSwitch(
                    onToggle: isSelected => { ModSettings.ChatNotifyOnBoatMarker = isSelected; },
                    bounds: uiToggleBounds.FlatCopy().WithParent(markerOptionRowBounds),
                    key: "toggle-show-boat-chat-message")
                .AddStaticText(
                    text: Lang.Get("egocarib-mapmarkers:boat-chat-message"),
                    font: CairoFont.WhiteSmallishText(),
                    bounds: uiToggleLabelBounds.FlatCopy().WithParent(markerOptionRowBounds))

            .EndChildElements()
            .BeginChildElements(markerOptionRowBounds = markerOptionRowBounds.BelowCopy())

                // Custom waypoint hotkeys enabled/disabled
                .AddSwitch(
                    onToggle: isSelected =>
                    {
                        ModSettings.EnableCustomHotkeys = isSelected;
                        if (!isSelected && CurrentTab == "Custom")
                            CurrentTab = null; // will re-default on next SetupDialog
                        RegisterCustomHotkeys();
                        SetupDialog();
                    },
                    bounds: uiToggleBounds = uiToggleBounds.FlatCopy().WithParent(markerOptionRowBounds),
                    key: "toggle-enable-custom-hotkeys")
                .AddStaticText(
                    text: Lang.Get("egocarib-mapmarkers:enable-custom-hotkeys"),
                    font: CairoFont.WhiteSmallishText(),
                    bounds: uiToggleLabelBounds = uiToggleLabelBounds.FlatCopy().WithParent(markerOptionRowBounds))
                .AddHoverText(
                    text: hotkeyTooltipText,
                    font: CairoFont.WhiteSmallText(),
                    width: hotkeyTooltipWidth,
                    bounds: uiToggleLabelBounds.FlatCopy())

            .EndChildElements()
            .BeginChildElements(markerOptionRowBounds = markerOptionRowBounds.BelowCopy())

                // Waypoint deletion enabled/disabled
                .AddSwitch(
                    onToggle: isSelected =>
                    {
                        ModSettings.EnableWaypointDeletionHotkey = isSelected;
                        RegisterDeleteHotkey();
                        SetupDialog();
                    },
                    bounds: uiToggleBounds = uiToggleBounds.FlatCopy().WithParent(markerOptionRowBounds),
                    key: "toggle-enable-delete-hotkey")
                .AddStaticText(
                    text: Lang.Get("egocarib-mapmarkers:enable-delete-hotkey"),
                    font: CairoFont.WhiteSmallishText(),
                    bounds: uiToggleLabelBounds = uiToggleLabelBounds.FlatCopy().WithParent(markerOptionRowBounds))
                .AddHoverText(
                    text: hotkeyTooltipText,
                    font: CairoFont.WhiteSmallText(),
                    width: hotkeyTooltipWidth,
                    bounds: uiToggleLabelBounds.FlatCopy())

            .EndChildElements();

            if (ModSettings.EnableWaypointDeletionHotkey)
            {
                SingleComposer
                .BeginChildElements(markerOptionRowBounds = markerOptionRowBounds.BelowCopy().WithFixedOffset(60, 0))

                    // Chat messages for waypoint deletion enabled/disabled
                    .AddSwitch(
                        onToggle: isSelected => { ModSettings.ChatNotifyOnWaypointDeletion = isSelected; },
                        bounds: uiToggleBounds = uiToggleBounds.FlatCopy().WithParent(markerOptionRowBounds),
                        key: "toggle-show-delete-chat-message")
                    .AddStaticText(
                        text: Lang.Get("egocarib-mapmarkers:show-chat-message-on-delete"),
                        font: CairoFont.WhiteSmallishText(),
                        bounds: uiToggleLabelBounds = uiToggleLabelBounds.FlatCopy().WithParent(markerOptionRowBounds))

                .EndChildElements();
                markerOptionRowBounds = markerOptionRowBounds.FlatCopy().WithFixedOffset(-60, 0);
            }

            SingleComposer
            .BeginChildElements(markerOptionRowBounds = markerOptionRowBounds.BelowCopy())

                // Master toggle to disable the mod
                .AddSwitch(
                    onToggle: isSelected =>
                    {
                        ModSettings.DisableAllModFeatures = isSelected;
                        SetupDialog();
                    },
                    bounds: uiToggleBounds = uiToggleBounds.FlatCopy().WithParent(markerOptionRowBounds),
                    key: "toggle-disable-mod")
                .AddStaticText(
                    text: Lang.Get("egocarib-mapmarkers:disable-mod"),
                    font: CairoFont.WhiteSmallishText(),
                    bounds: uiToggleLabelBounds = uiToggleLabelBounds.FlatCopy().WithParent(markerOptionRowBounds))

            .EndChildElements();

            SingleComposer.GetSwitch("toggle-mark-style-interact").SetValue(ModSettings.EnableMarkOnInteract);
            SingleComposer.GetSwitch("toggle-mark-style-sneak").SetValue(ModSettings.EnableMarkOnSneak);
            SingleComposer.GetSwitch("toggle-show-create-chat-message").SetValue(ModSettings.ChatNotifyOnWaypointCreation);
            SingleComposer.GetSwitch("toggle-label-coordinates").SetValue(ModSettings.LabelCoordinates);
            SingleComposer.GetSwitch("toggle-suppress-farmland").SetValue(ModSettings.SuppressMarkerOnFarmland);
            SingleComposer.GetSwitch("toggle-show-boat-chat-message").SetValue(ModSettings.ChatNotifyOnBoatMarker);
            SingleComposer.GetSwitch("toggle-enable-detect-hotkey").SetValue(ModSettings.EnableDetectHotkey);
            SingleComposer.GetSwitch("toggle-enable-custom-hotkeys").SetValue(ModSettings.EnableCustomHotkeys);
            SingleComposer.GetSwitch("toggle-enable-delete-hotkey").SetValue(ModSettings.EnableWaypointDeletionHotkey);
            SingleComposer.GetSwitch("toggle-show-delete-chat-message")?.SetValue(ModSettings.ChatNotifyOnWaypointDeletion);
            SingleComposer.GetSwitch("toggle-disable-mod").SetValue(ModSettings.DisableAllModFeatures);
        }

        /// <summary>
        /// Called when the selected menu tab changes. Recomposes the GUI to draw the new menu screen.
        /// </summary>
        private void OnTabToggle(string tabName)
        {
            CurrentTab = tabName;
            currentScrollY = 0;
            SetupDialog();
        }

        /// <summary>
        /// Called when the scrollbar value changes. Scrolls the marker options content.
        /// </summary>
        private void OnNewScrollbarValue(float value)
        {
            currentScrollY = value;
            if (scrollContentBounds != null)
            {
                scrollContentBounds.fixedY = 0 - value;
                scrollContentBounds.CalcWorldBounds();
            }
        }

        /// <summary>
        /// Called when a map marker option is toggled off/on. Enables or disables that option and recomposes the GUI.
        /// </summary>
        private void OnMarkerToggleEnabled(bool isSelected)
        {
            // Update all layout tab entries (including sub-entries).
            // Look up each switch by its stable EntryLabel-derived key, mirroring the keys
            // produced by ComposeMarkerRow. The localized DisplayName is NOT a safe lookup key
            // because two distinct definitions may share the same translation (which is what
            // originally crashed the GUI when the second element was added).
            foreach (var tab in Layout.Tabs)
                foreach (var subgroup in tab.Subgroups)
                    foreach (var entry in subgroup.Entries)
                    {
                        if (entry.IsExpandable)
                        {
                            if (entry.IsExpanded && entry.SubEntries != null)
                            {
                                string subKeyPrefix = entry.EntryLabel + "-";
                                foreach (var sub in entry.SubEntries)
                                {
                                    var sw = SingleComposer.GetInteractiveSwitch(subKeyPrefix + sub.EntryLabel + "-auto-markers-enabled");
                                    if (sw != null) sub.Setting.Enabled = sw.On;
                                }
                            }
                            else if (entry.Setting != null)
                            {
                                var sw = SingleComposer.GetInteractiveSwitch(entry.EntryLabel + "-auto-markers-enabled");
                                if (sw != null) entry.Setting.Enabled = sw.On;
                            }
                        }
                        else if (entry.Setting != null)
                        {
                            var sw = SingleComposer.GetInteractiveSwitch(entry.EntryLabel + "-auto-markers-enabled");
                            if (sw != null) entry.Setting.Enabled = sw.On;
                        }
                    }
            // Update custom entries — index-based key matches what ComposeMarkerOptions registers.
            int customIndex = 1;
            foreach (var customEntry in Layout.CustomEntries)
            {
                var sw = SingleComposer.GetInteractiveSwitch("custom-marker-" + customIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-auto-markers-enabled");
                if (sw != null) customEntry.Value.Enabled = sw.On;
                customIndex++;
            }
            SetupDialog(); // Redraw GUI
        }

        private bool OnIconColorButtonClick(string settingGroup, string settingName, AutoMapMarkerSetting settings)
        {
            try
            {
                if (icons == null || icons.Length == 0) LoadIconTextures();
                if (colors == null || colors.Length == 0) LoadColorOptions();
                iconConfigPopup = new MapMarkerIconSettingsGUI(capi, settingName, settings, icons, colors);
                iconConfigPopup.OnClosed += delegate
                {
                    try
                    {
                        capi.Gui.RequestFocus(this);
                        SetupDialog();  // Redraw GUI
                    }
                    catch (Exception e)
                    {
                        MessageUtil.LogError($"Could not redraw the settings menu after editing a marker: {e}");
                    }
                };
                iconConfigPopup.TryOpen();
            }
            catch (Exception e)
            {
                MessageUtil.LogError($"Could not open the marker icon/color editor: {e}");
                capi.ShowChatMessage("[Auto Map Markers] Could not open the icon/color editor, see client-main.log.");
            }
            return true;
        }

        /// <summary>
        /// Closes the GUI, which will also trigger an attempt to save the settings.
        /// </summary>
        private bool OnSaveButton()
        {
            TryClose();
            return true;
        }

        /// <summary>
        /// Deletes the saved settings and rebuilds the dialog from the mod's shipped defaults.
        /// </summary>
        private bool OnResetDefaultsClicked()
        {
            try
            {
                ModSettings = MapMarkerConfig.ResetSettings(capi);
                Layout = ModSettings.GetMapMarkerSettingLayout();
                currentScrollY = 0;
                SetupDialog();
                MessageUtil.Chat(Lang.Get("egocarib-mapmarkers:reset-defaults-done"));
            }
            catch (Exception exception)
            {
                MapMarkerMod.CoreAPI?.Logger.Error(
                    $"Map Marker Mod: Failed to reset settings. ({exception})");
            }
            return true;
        }

        /// <summary>
        /// Called when the title bar "X" button is clicked. Closes the GUI and attempts to save the settings.
        /// </summary>
        private void OnTitleBarCloseClicked()
        {
            TryClose();
        }

        /// <summary>
        /// Called when the GUI is closed for any reason. Attempts to save the user's settings if they are valid.
        /// </summary>
        public override void OnGuiClosed()
        {
            MapMarkerConfig.SaveSettings(capi, ModSettings);
            UnloadIconTextures();
            base.OnGuiClosed();
        }

        /// <summary>
        /// Measures the GUI width that will be consumed by a string of text in the specified font.
        /// Can be used to help pre-determine the width needed for the text's enclosing GUI element.
        /// </summary>
        private double GetFontTextWidth(CairoFont font, string text, double padding = 15.0)
        {
            TextExtents textExtents = font.GetTextExtents(text);
            return textExtents.Width / (double)RuntimeEnv.GUIScale + padding;
        }
    }
}
