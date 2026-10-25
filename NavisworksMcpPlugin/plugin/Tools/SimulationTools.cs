using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    /// <summary>
    /// Pilotage du CURSEUR de simulation TimeLiner et export d'images par palier.
    ///
    /// `LcTlSimulationHelper` (assembly Autodesk.Navisworks.Timeliner.dll, couche interop) est un
    /// singleton exposant `SetSimulationTime(DateTime)`, `GetStartTime/GetEndTime`, `Play/Pause/Stop/Step`.
    /// C'est la seule voie trouvée pour déplacer le temps de simulation par code : il n'y a rien pour ça
    /// dans l'API managée.
    ///
    /// Sert à VOIR la séquence sans la jouer à la main : on place le curseur à N instants et on rend une
    /// image à chaque fois. Le rendu passe par `Document.GenerateImage`, managé, comme export_image.
    /// </summary>
    [McpServerToolType]
    public static class SimulationTools
    {
        /// <summary>
        /// PIÈGE CORRIGÉ (2026-08-03) : appeler `Initialise(state)` à chaque fois RÉ-INITIALISE le
        /// helper, qui renvoie alors des dates à l'époque Unix (1970) et `TimeControlsValid = false`
        /// alors que l'interface fonctionne. Ne l'initialiser QUE si l'instance est absente.
        /// </summary>
        private static LcTlSimulationHelper Helper()
        {
            var h = LcTlSimulationHelper.Instance;
            if (h != null) return h;
            try { LcTlSimulationHelper.Initialise(LcOpState.GetActiveInstance()); } catch { }
            return LcTlSimulationHelper.Instance;
        }

        private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        [McpServerTool(ReadOnly = true), Description(
            "État du curseur de simulation TimeLiner : plage de dates, instant courant, état de lecture, " +
            "type de vue (Planifié / Réel), et validité des contrôles de temps. " +
            "À appeler avant timeliner_simulate_export pour connaître la plage réelle.")]
        public static Task<SimulationStateResult> timeliner_simulation_state() =>
            UiThread.Run(() =>
            {
                var h = Helper();
                if (h == null) return new SimulationStateResult { Error = "LcTlSimulationHelper.Instance est null." };
                return new SimulationStateResult
                {
                    StartTime = Iso(h.GetStartTime()),
                    EndTime = Iso(h.GetEndTime()),
                    CurrentTime = Iso(h.GetSimulationTime()),
                    State = h.GetSimulationState().ToString(),
                    ViewType = h.GetViewType().ToString(),
                    TimeControlsValid = h.GetTimeControlsValid(),
                    GuiActive = h.IsGUIActive(),
                    StepSizeSeconds = h.GetSingleStepSizeInSeconds()
                };
            });

        [McpServerTool, Description(
            "Parcourt la simulation TimeLiner et exporte UNE IMAGE PAR PALIER : place le curseur de temps à " +
            "N instants régulièrement répartis, et rend la vue courante à chaque fois. " +
            "Permet de vérifier une séquence 4D (démolition, descente d'un équipement) sans la jouer à la main, " +
            "et de produire une planche pour le client. " +
            "Cadrer la vue AVANT (goto_viewpoint), l'export utilise la caméra courante. " +
            "Les images vont sur le Bureau dans 'Navisworks_Exports' sauf outputDir. " +
            "Vérifier dans le retour que 'timeSet' varie ET que la taille des PNG varie : deux images " +
            "identiques signalent un curseur qui n'a pas pris effet.")]
        public static Task<SimulateExportResult> timeliner_simulate_export(
            [Description("Nombre d'images à produire (def 8)")] int steps = 8,
            [Description("Préfixe des fichiers (def 'SIM')")] string fileNamePrefix = "SIM",
            [Description("Début, ISO (vide = début de la simulation)")] string startDate = "",
            [Description("Fin, ISO (vide = fin de la simulation)")] string endDate = "",
            [Description("Dossier de sortie (vide = Bureau/Navisworks_Exports)")] string outputDir = "",
            [Description("Largeur en pixels (def 1600)")] int width = 1600,
            [Description("Hauteur en pixels (def 900)")] int height = 900) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var h = Helper();
                if (h == null) return new SimulateExportResult { Error = "LcTlSimulationHelper.Instance est null." };
                if (steps < 2) steps = 2;

                DateTime from, to;
                if (!DateTime.TryParse(startDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out from))
                    from = h.GetStartTime();
                if (!DateTime.TryParse(endDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out to))
                    to = h.GetEndTime();
                if (to <= from) return new SimulateExportResult { Error = "Plage de dates invalide: " + Iso(from) + " -> " + Iso(to) };

                if (string.IsNullOrWhiteSpace(outputDir))
                    outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Navisworks_Exports");
                Directory.CreateDirectory(outputDir);

                var safePrefix = string.Join("_", fileNamePrefix.Split(Path.GetInvalidFileNameChars()));
                var shots = new List<SimulationShot>();
                var totalTicks = (to - from).Ticks;

                for (int i = 0; i < steps; i++)
                {
                    var t = from.AddTicks(totalTicks * i / (steps - 1));
                    string readBack = null, error = null;
                    long sizeKb = 0;
                    var file = Path.Combine(outputDir,
                        string.Format(CultureInfo.InvariantCulture, "{0}_{1:00}_{2:yyyyMMdd}.png", safePrefix, i + 1, t));

                    try
                    {
                        h.SetSimulationTime(t);
                        readBack = Iso(h.GetSimulationTime());
                        using (var bmp = doc.GenerateImage(ImageGenerationStyle.ScenePlusOverlay, width, height, true))
                            bmp.Save(file, ImageFormat.Png);
                        if (File.Exists(file)) sizeKb = new FileInfo(file).Length / 1024;
                    }
                    catch (Exception ex) { error = ex.Message; }

                    shots.Add(new SimulationShot
                    {
                        Index = i + 1,
                        TimeRequested = Iso(t),
                        TimeSet = readBack,
                        FilePath = error == null ? file : null,
                        FileSizeKB = sizeKb,
                        Error = error
                    });
                }

                // Deux tailles identiques d'affilée = curseur probablement inopérant.
                var sizes = shots.Where(s => s.Error == null).Select(s => s.FileSizeKB).ToList();
                bool allSame = sizes.Count > 1 && sizes.Distinct().Count() == 1;

                return new SimulateExportResult
                {
                    OutputDir = outputDir,
                    Steps = shots.Count,
                    From = Iso(from),
                    To = Iso(to),
                    Shots = shots.ToArray(),
                    AllImagesIdenticalSize = allSame,
                    Warning = allSame ? "Toutes les images ont la même taille : le curseur de simulation n'a peut-être pas pris effet." : null
                };
            });
    }

    public class SimulationStateResult
    {
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public string CurrentTime { get; set; }
        public string State { get; set; }
        public string ViewType { get; set; }
        public bool TimeControlsValid { get; set; }
        public bool GuiActive { get; set; }
        public long StepSizeSeconds { get; set; }
        public string Error { get; set; }
    }

    public class SimulateExportResult
    {
        public string OutputDir { get; set; }
        public int Steps { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public SimulationShot[] Shots { get; set; }
        public bool AllImagesIdenticalSize { get; set; }
        public string Warning { get; set; }
        public string Error { get; set; }
    }

    public class SimulationShot
    {
        public int Index { get; set; }
        public string TimeRequested { get; set; }
        public string TimeSet { get; set; }
        public string FilePath { get; set; }
        public long FileSizeKB { get; set; }
        public string Error { get; set; }
    }
}
