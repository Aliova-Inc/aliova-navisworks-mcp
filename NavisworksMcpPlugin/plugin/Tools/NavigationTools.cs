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
    public static class NavigationTools
    {
        [McpServerTool, Description(
            "Zoom the camera to focus on specific items by their GUIDs, without changing visibility. " +
            "All items remain visible — only the camera moves to frame the specified items. " +
            "Useful to navigate to a clash or element without isolating.")]
        public static Task<ZoomResult> zoom_to_items(
            [Description("Comma-separated instance GUIDs of items to zoom to")] string guids) =>
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

                // Compute combined bounding box
                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                bool hasBB = false;

                foreach (var item in matches)
                {
                    var bb = item.BoundingBox();
                    if (bb != null)
                    {
                        hasBB = true;
                        if (bb.Min.X < minX) minX = bb.Min.X;
                        if (bb.Min.Y < minY) minY = bb.Min.Y;
                        if (bb.Min.Z < minZ) minZ = bb.Min.Z;
                        if (bb.Max.X > maxX) maxX = bb.Max.X;
                        if (bb.Max.Y > maxY) maxY = bb.Max.Y;
                        if (bb.Max.Z > maxZ) maxZ = bb.Max.Z;
                    }
                }

                if (!hasBB)
                    throw new ArgumentException("Cannot compute bounding box for the specified items.");

                var combinedBB = new BoundingBox3D(
                    new Point3D(minX, minY, minZ),
                    new Point3D(maxX, maxY, maxZ));

                var vp = doc.CurrentViewpoint.CreateCopy();
                vp.ZoomBox(combinedBB);
                doc.CurrentViewpoint.CopyFrom(vp);

                return new ZoomResult
                {
                    Zoomed = matches.Count,
                    BoundingBox = $"({minX:F2},{minY:F2},{minZ:F2})-({maxX:F2},{maxY:F2},{maxZ:F2})"
                };
            });

        [McpServerTool, Description(
            "Enable a section box (clipping planes) around specific items by their GUIDs. " +
            "Clips the view to show only the region around those items with an optional margin. " +
            "Useful to focus on a clash zone or a specific area. Also zooms to the region.")]
        public static Task<SectionBoxResult> set_section_box_from_items(
            [Description("Comma-separated instance GUIDs")] string guids,
            [Description("Margin to add around the items in model units (default 2.0 meters)")] double margin = 2.0) =>
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

                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                bool hasBB = false;

                foreach (var item in matches)
                {
                    var bb = item.BoundingBox();
                    if (bb != null)
                    {
                        hasBB = true;
                        if (bb.Min.X < minX) minX = bb.Min.X;
                        if (bb.Min.Y < minY) minY = bb.Min.Y;
                        if (bb.Min.Z < minZ) minZ = bb.Min.Z;
                        if (bb.Max.X > maxX) maxX = bb.Max.X;
                        if (bb.Max.Y > maxY) maxY = bb.Max.Y;
                        if (bb.Max.Z > maxZ) maxZ = bb.Max.Z;
                    }
                }

                if (!hasBB)
                    throw new ArgumentException("Cannot compute bounding box for the specified items.");

                // Add margin
                minX -= margin; minY -= margin; minZ -= margin;
                maxX += margin; maxY += margin; maxZ += margin;

                // Use View.SetClippingPlanes(string json) API
                // JSON format: ClipPlaneSet with OrientedBox3D
                var json = BuildClipBoxJson(minX, minY, minZ, maxX, maxY, maxZ);
                doc.ActiveView.SetClippingPlanes(json);

                // Also zoom to the region
                var box = new BoundingBox3D(
                    new Point3D(minX, minY, minZ),
                    new Point3D(maxX, maxY, maxZ));
                var vp = doc.CurrentViewpoint.CreateCopy();
                vp.ZoomBox(box);
                doc.CurrentViewpoint.CopyFrom(vp);

                return new SectionBoxResult
                {
                    Enabled = true,
                    Box = $"({minX:F2},{minY:F2},{minZ:F2})-({maxX:F2},{maxY:F2},{maxZ:F2})"
                };
            });

        [McpServerTool, Description(
            "Remove all section/clipping planes and restore the full unclipped view.")]
        public static Task<SectionBoxResult> clear_section_box() =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                // Disabled ClipPlaneSet removes all clipping
                var json = "{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"Enabled\":false}";
                doc.ActiveView.SetClippingPlanes(json);
                return new SectionBoxResult { Enabled = false, Box = "" };
            });

        [McpServerTool, Description(
            "DEBUG: Get the current clipping planes JSON from the active view.")]
        public static Task<DebugClipResult> debug_get_clip_planes() =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var json = doc.ActiveView.GetClippingPlanes();
                return new DebugClipResult { Json = json ?? "(null)" };
            });

        /// <summary>
        /// Builds a JSON string for a ClipPlaneSet with an OrientedBox3D.
        /// This is the format expected by View.SetClippingPlanes().
        /// </summary>
        private static string BuildClipBoxJson(
            double minX, double minY, double minZ,
            double maxX, double maxY, double maxZ)
        {
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(ic,
                "{{\"Type\":\"ClipPlaneSet\",\"Version\":1," +
                "\"OrientedBox\":{{\"Type\":\"OrientedBox3D\",\"Version\":1," +
                "\"Box\":[[{0},{1},{2}],[{3},{4},{5}]]," +
                "\"Rotation\":[0,0,0]}}," +
                "\"Enabled\":true}}",
                minX, minY, minZ, maxX, maxY, maxZ);
        }
    }

    public class ZoomResult
    {
        public int Zoomed { get; set; }
        public string BoundingBox { get; set; }
    }

    public class SectionBoxResult
    {
        public bool Enabled { get; set; }
        public string Box { get; set; }
    }

    public class DebugClipResult
    {
        public string Json { get; set; }
    }
}
