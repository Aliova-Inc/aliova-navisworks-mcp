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
    public static class MeasureTools
    {
        [McpServerTool, Description(
            "Measure the shortest distance between two items identified by their GUIDs. " +
            "Returns the distance between bounding boxes (center-to-center and edge-to-edge). " +
            "Also returns each item's bounding box dimensions. Useful to verify clearances, " +
            "free heights, distances between elements.")]
        public static Task<MeasureResult> measure_between(
            [Description("Instance GUID of first item")] string guidA,
            [Description("Instance GUID of second item")] string guidB) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;

                var itemA = doc.Models.RootItemDescendantsAndSelf
                    .FirstOrDefault(i => string.Equals(i.InstanceGuid.ToString(), guidA.Trim(), StringComparison.OrdinalIgnoreCase));
                var itemB = doc.Models.RootItemDescendantsAndSelf
                    .FirstOrDefault(i => string.Equals(i.InstanceGuid.ToString(), guidB.Trim(), StringComparison.OrdinalIgnoreCase));

                if (itemA == null)
                    throw new ArgumentException($"Item A not found: {guidA}");
                if (itemB == null)
                    throw new ArgumentException($"Item B not found: {guidB}");

                var bbA = itemA.BoundingBox();
                var bbB = itemB.BoundingBox();

                if (bbA == null || bbB == null)
                    throw new ArgumentException("Cannot compute bounding box for one or both items.");

                // Center-to-center distance
                var centerA = new Point3D(
                    (bbA.Min.X + bbA.Max.X) / 2,
                    (bbA.Min.Y + bbA.Max.Y) / 2,
                    (bbA.Min.Z + bbA.Max.Z) / 2);
                var centerB = new Point3D(
                    (bbB.Min.X + bbB.Max.X) / 2,
                    (bbB.Min.Y + bbB.Max.Y) / 2,
                    (bbB.Min.Z + bbB.Max.Z) / 2);

                double centerDist = Math.Sqrt(
                    Math.Pow(centerA.X - centerB.X, 2) +
                    Math.Pow(centerA.Y - centerB.Y, 2) +
                    Math.Pow(centerA.Z - centerB.Z, 2));

                // Edge-to-edge (closest point between two AABBs)
                double dx = Math.Max(0, Math.Max(bbA.Min.X - bbB.Max.X, bbB.Min.X - bbA.Max.X));
                double dy = Math.Max(0, Math.Max(bbA.Min.Y - bbB.Max.Y, bbB.Min.Y - bbA.Max.Y));
                double dz = Math.Max(0, Math.Max(bbA.Min.Z - bbB.Max.Z, bbB.Min.Z - bbA.Max.Z));
                double edgeDist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                // Check overlap
                bool overlaps = dx == 0 && dy == 0 && dz == 0;

                // Individual dimensions
                var dimA = new DimensionDto
                {
                    Width = bbA.Max.X - bbA.Min.X,
                    Depth = bbA.Max.Y - bbA.Min.Y,
                    Height = bbA.Max.Z - bbA.Min.Z
                };
                var dimB = new DimensionDto
                {
                    Width = bbB.Max.X - bbB.Min.X,
                    Depth = bbB.Max.Y - bbB.Min.Y,
                    Height = bbB.Max.Z - bbB.Min.Z
                };

                // Vertical distance (Z-axis)
                double verticalGap = 0;
                if (bbA.Min.Z > bbB.Max.Z)
                    verticalGap = bbA.Min.Z - bbB.Max.Z;
                else if (bbB.Min.Z > bbA.Max.Z)
                    verticalGap = bbB.Min.Z - bbA.Max.Z;

                return new MeasureResult
                {
                    ItemAName = itemA.DisplayName ?? "",
                    ItemBName = itemB.DisplayName ?? "",
                    CenterToCenter = Math.Round(centerDist, 4),
                    EdgeToEdge = Math.Round(edgeDist, 4),
                    VerticalGap = Math.Round(verticalGap, 4),
                    Overlaps = overlaps,
                    DimensionsA = dimA,
                    DimensionsB = dimB
                };
            });

        [McpServerTool, Description(
            "Get the bounding box dimensions and position of an item by its GUID. " +
            "Returns width (X), depth (Y), height (Z), center point, and min/max coordinates.")]
        public static Task<ItemDimensionResult> get_dimensions(
            [Description("Instance GUID of the item")] string guid) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var item = doc.Models.RootItemDescendantsAndSelf
                    .FirstOrDefault(i => string.Equals(i.InstanceGuid.ToString(), guid.Trim(), StringComparison.OrdinalIgnoreCase));

                if (item == null)
                    throw new ArgumentException($"Item not found: {guid}");

                var bb = item.BoundingBox();
                if (bb == null)
                    throw new ArgumentException("Cannot compute bounding box for this item.");

                return new ItemDimensionResult
                {
                    Name = item.DisplayName ?? "",
                    Width = Math.Round(bb.Max.X - bb.Min.X, 4),
                    Depth = Math.Round(bb.Max.Y - bb.Min.Y, 4),
                    Height = Math.Round(bb.Max.Z - bb.Min.Z, 4),
                    CenterX = Math.Round((bb.Min.X + bb.Max.X) / 2, 4),
                    CenterY = Math.Round((bb.Min.Y + bb.Max.Y) / 2, 4),
                    CenterZ = Math.Round((bb.Min.Z + bb.Max.Z) / 2, 4),
                    MinPoint = $"({bb.Min.X:F4},{bb.Min.Y:F4},{bb.Min.Z:F4})",
                    MaxPoint = $"({bb.Max.X:F4},{bb.Max.Y:F4},{bb.Max.Z:F4})"
                };
            });
    }

    public class MeasureResult
    {
        public string ItemAName { get; set; }
        public string ItemBName { get; set; }
        public double CenterToCenter { get; set; }
        public double EdgeToEdge { get; set; }
        public double VerticalGap { get; set; }
        public bool Overlaps { get; set; }
        public DimensionDto DimensionsA { get; set; }
        public DimensionDto DimensionsB { get; set; }
    }

    public class DimensionDto
    {
        public double Width { get; set; }
        public double Depth { get; set; }
        public double Height { get; set; }
    }

    public class ItemDimensionResult
    {
        public string Name { get; set; }
        public double Width { get; set; }
        public double Depth { get; set; }
        public double Height { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double CenterZ { get; set; }
        public string MinPoint { get; set; }
        public string MaxPoint { get; set; }
    }
}
