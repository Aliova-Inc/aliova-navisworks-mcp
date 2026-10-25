using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    [McpServerToolType]
    public static class AppearanceTools
    {
        [McpServerTool, Description(
            "Override the color of specific items by their GUIDs. Useful for visual markup: " +
            "red for clashes, green for resolved, blue for a discipline, etc. " +
            "Color is specified as a hex string (e.g. 'FF0000' for red, '00FF00' for green). " +
            "Pass an optional transparency value 0-1 (0=opaque, 1=fully transparent).")]
        public static Task<ColorResult> color_override(
            [Description("Comma-separated instance GUIDs of items to color")] string guids,
            [Description("Hex color string, e.g. 'FF0000' for red")] string color,
            [Description("Transparency 0.0 (opaque) to 1.0 (fully transparent), default 0.0")] double transparency = 0.0) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var guidSet = new HashSet<string>(
                    guids.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(g => g.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                // Parse hex color
                var hex = color.Trim().TrimStart('#');
                if (hex.Length != 6)
                    throw new ArgumentException("Color must be a 6-character hex string (e.g. 'FF0000')");

                byte rb = Convert.ToByte(hex.Substring(0, 2), 16);
                byte gb = Convert.ToByte(hex.Substring(2, 2), 16);
                byte bb = Convert.ToByte(hex.Substring(4, 2), 16);

                // Navisworks Color uses doubles 0.0-1.0
                var navisColor = new Color(rb / 255.0, gb / 255.0, bb / 255.0);
                if (transparency < 0) transparency = 0;
                if (transparency > 1) transparency = 1;

                var matches = doc.Models.RootItemDescendantsAndSelf
                    .Where(item => guidSet.Contains(item.InstanceGuid.ToString()))
                    .ToList();

                if (matches.Count == 0)
                    throw new ArgumentException("No items found for the provided GUIDs.");

                // Include descendants
                var toColor = new HashSet<ModelItem>();
                foreach (var m in matches)
                    foreach (var d in m.DescendantsAndSelf)
                        toColor.Add(d);

                var itemList = toColor.ToList();
                doc.Models.OverridePermanentColor(itemList, navisColor);
                if (transparency > 0)
                    doc.Models.OverridePermanentTransparency(itemList, transparency);

                return new ColorResult { Colored = itemList.Count, Color = "#" + hex };
            });

        [McpServerTool, Description(
            "Reset ALL color and transparency overrides to original appearance. " +
            "Removes all permanent material overrides from the entire model.")]
        public static Task<ResetColorResult> reset_colors() =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                doc.Models.ResetAllPermanentMaterials();
                return new ResetColorResult { Reset = true };
            });

        [McpServerTool, Description(
            "Hide specific items by their GUIDs without affecting other items visibility. " +
            "Unlike isolate_items which shows ONLY the specified items, hide_items keeps everything visible " +
            "except the specified items. Useful to remove obstructing elements from view.")]
        public static Task<HideResult> hide_items(
            [Description("Comma-separated instance GUIDs of items to hide")] string guids) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var guidSet = new HashSet<string>(
                    guids.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(g => g.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                var matches = doc.Models.RootItemDescendantsAndSelf
                    .Where(item => guidSet.Contains(item.InstanceGuid.ToString()))
                    .ToList();

                if (matches.Count == 0)
                    throw new ArgumentException("No items found for the provided GUIDs.");

                // Include descendants
                var toHide = new List<ModelItem>();
                foreach (var m in matches)
                    foreach (var d in m.DescendantsAndSelf)
                        toHide.Add(d);

                doc.Models.SetHidden(toHide, true);
                return new HideResult { Hidden = toHide.Count };
            });

        [McpServerTool, Description(
            "Hide entire models/partitions by keyword matching on their display name. " +
            "Opposite of isolating by name (isolate_by_multi_search with branches): hides matched models instead. " +
            "Example: hide architecture to see only MEP with keywords 'PRJ_A'.")]
        public static Task<HideResult> hide_by_model(
            [Description("Comma-separated keywords to match model/partition names to hide (case-insensitive)")] string keywords) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var kws = keywords.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(k => k.Trim().ToLowerInvariant())
                                  .Where(k => k.Length > 0)
                                  .ToList();

                if (kws.Count == 0)
                    throw new ArgumentException("At least one keyword is required.");

                var toHide = new List<ModelItem>();
                foreach (var model in doc.Models)
                {
                    foreach (var item in model.RootItem.DescendantsAndSelf)
                    {
                        bool isPartition = item.ClassDisplayName == "LcOaPartition"
                                        || item.IsLayer
                                        || (item.Parent != null && item.Parent == model.RootItem);

                        if (isPartition)
                        {
                            var dn = item.DisplayName?.ToLowerInvariant() ?? "";
                            if (kws.Any(k => dn.Contains(k)))
                            {
                                foreach (var d in item.DescendantsAndSelf)
                                    toHide.Add(d);
                            }
                        }
                    }
                }

                if (toHide.Count == 0)
                    throw new ArgumentException("No models found matching the provided keywords.");

                doc.Models.SetHidden(toHide, true);
                return new HideResult { Hidden = toHide.Count };
            });
    }

    public class ColorResult
    {
        public int Colored { get; set; }
        public string Color { get; set; }
    }

    public class ResetColorResult
    {
        public bool Reset { get; set; }
    }

    public class HideResult
    {
        public int Hidden { get; set; }
    }
}
