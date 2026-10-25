using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    [McpServerToolType]
    public static class ExportTools
    {
        [McpServerTool, Description(
            "Export the current 3D view as a PNG image file. The image is saved to the Desktop " +
            "in a 'Navisworks_Exports' folder. Useful for generating report screenshots, " +
            "documenting clashes, or capturing coordination views.")]
        public static Task<ExportImageResult> export_image(
            [Description("Output file name without extension (e.g. 'CLASH_ARC_MEP_N2')")] string fileName,
            [Description("Optional: full output directory path. Defaults to Desktop/Navisworks_Exports.")] string outputDir = "",
            [Description("Image width in pixels (default 1920)")] int width = 1920,
            [Description("Image height in pixels (default 1080)")] int height = 1080) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;

                if (string.IsNullOrWhiteSpace(outputDir))
                    outputDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "Navisworks_Exports");

                Directory.CreateDirectory(outputDir);

                // Sanitize filename
                var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
                var fullPath = Path.Combine(outputDir, safeName + ".png");

                // Document.GenerateImage(ImageGenerationStyle, width, height, enableSectioning) -> Bitmap
                using (var bitmap = doc.GenerateImage(ImageGenerationStyle.ScenePlusOverlay, width, height, true))
                {
                    bitmap.Save(fullPath, ImageFormat.Png);
                }

                long sizeKb = 0;
                if (File.Exists(fullPath))
                    sizeKb = new FileInfo(fullPath).Length / 1024;

                return new ExportImageResult
                {
                    FilePath = fullPath,
                    Width = width,
                    Height = height,
                    FileSizeKB = sizeKb
                };
            });
    }

    public class ExportImageResult
    {
        public string FilePath { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long FileSizeKB { get; set; }
    }
}
