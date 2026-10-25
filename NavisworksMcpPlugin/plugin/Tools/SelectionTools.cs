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
    public static class SelectionTools
    {
        [McpServerTool, Description(
            "Get all currently selected items in Navisworks. Returns each item's display name, " +
            "class name, instance GUID, and bounding box. Use this to inspect what the user has selected.")]
        public static Task<SelectionResult> get_selection() =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var items = doc.CurrentSelection.SelectedItems.ToList();

                return new SelectionResult
                {
                    Count = items.Count,
                    Items = items.Select(ItemSummary.From).ToArray()
                };
            });

        [McpServerTool, Description(
            "Clear the current selection in Navisworks. Deselects all items.")]
        public static Task<OkResult> clear_selection() =>
            UiThread.Run(() =>
            {
                Application.ActiveDocument.CurrentSelection.Clear();
                return new OkResult { Ok = true };
            });

        [McpServerTool, Description(
            "Select items by their instance GUIDs. Replaces the current selection. " +
            "Pass a comma-separated list of GUIDs.")]
        public static Task<SelectCountResult> select_by_guids(
            [Description("Comma-separated instance GUIDs to select")] string guids) =>
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

                doc.CurrentSelection.Clear();
                if (matches.Count > 0)
                    doc.CurrentSelection.AddRange(matches);

                return new SelectCountResult { Selected = matches.Count };
            });
    }

    public class SelectionResult
    {
        public int Count { get; set; }
        public ItemSummary[] Items { get; set; }
    }

    public class SelectCountResult
    {
        public int Selected { get; set; }
    }

    public class OkResult
    {
        public bool Ok { get; set; }
    }

    public class ItemSummary
    {
        public string DisplayName { get; set; }
        public string ClassName { get; set; }
        public string InstanceGuid { get; set; }
        public string BoundingBox { get; set; }

        public static ItemSummary From(ModelItem item)
        {
            var bb = item.BoundingBox();
            string bbStr = bb != null
                ? $"({bb.Min.X:F2},{bb.Min.Y:F2},{bb.Min.Z:F2})-({bb.Max.X:F2},{bb.Max.Y:F2},{bb.Max.Z:F2})"
                : "";

            return new ItemSummary
            {
                DisplayName = item.DisplayName ?? "",
                ClassName = item.ClassName ?? "",
                InstanceGuid = item.InstanceGuid.ToString(),
                BoundingBox = bbStr
            };
        }
    }
}
