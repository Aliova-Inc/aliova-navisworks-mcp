using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Aliova.McpSetup
{
    /// <summary>
    /// Une version de Navisworks presente sur le poste et prise en charge par le paquet.
    /// </summary>
    internal sealed class NavisworksTarget
    {
        public string Release { get; set; }        // "2026"
        public string SeriesCode { get; set; }     // "Nw23"
        public string InstallPath { get; set; }
        public string Edition { get; set; }        // "Manage", "Simulate"

        public override string ToString()
        {
            return "Navisworks " + Edition + " " + Release;
        }
    }

    /// <summary>
    /// Detection des versions de Navisworks installees.
    ///
    /// On lit le registre puis on retombe sur le disque, parce que les deux peuvent mentir
    /// separement : une entree de registre survit a une desinstallation bacle, et un dossier
    /// peut exister sans que le produit soit enregistre. On ne retient une cible que si
    /// Autodesk.Navisworks.Api.dll est reellement la : c'est la seule preuve qu'un plugin
    /// gere pourra se charger.
    /// </summary>
    internal static class NavisworksDetector
    {
        // Millesime -> code de serie du manifeste, releve sur les paquets tiers du poste.
        private static readonly Dictionary<string, string> SeriesByRelease = new Dictionary<string, string>
        {
            { "2021", "Nw18" },
            { "2022", "Nw19" },
            { "2023", "Nw20" },
            { "2024", "Nw21" },
            { "2025", "Nw22" },
            { "2026", "Nw23" },
        };

        // Le paquet ne declare que 2026 : la DLL est compilee contre cette API.
        private static readonly string[] SupportedReleases = { "2026" };

        private static readonly string[] Editions = { "Manage", "Simulate" };

        public static List<NavisworksTarget> Detect()
        {
            var found = new List<NavisworksTarget>();

            foreach (var release in SupportedReleases)
            {
                foreach (var edition in Editions)
                {
                    var path = LocateFromRegistry(release, edition) ?? LocateOnDisk(release, edition);
                    if (path == null) continue;
                    if (!File.Exists(Path.Combine(path, "Autodesk.Navisworks.Api.dll"))) continue;

                    found.Add(new NavisworksTarget
                    {
                        Release = release,
                        SeriesCode = SeriesByRelease[release],
                        InstallPath = path,
                        Edition = edition,
                    });
                }
            }

            return found;
        }

        private static string LocateFromRegistry(string release, string edition)
        {
            // Autodesk enregistre les chemins d'installation sous plusieurs formes selon les
            // millesimes ; on essaie les variantes connues plutot que d'en supposer une.
            var keys = new[]
            {
                @"SOFTWARE\Autodesk\Navisworks " + edition + " x64\\" + release,
                @"SOFTWARE\Autodesk\Navisworks\" + edition + "\\" + release,
            };

            foreach (var key in keys)
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                        using (var sub = baseKey.OpenSubKey(key))
                        {
                            if (sub == null) continue;
                            foreach (var name in new[] { "InstallLocation", "Location", "InstallDir" })
                            {
                                var value = sub.GetValue(name) as string;
                                if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                                    return value.TrimEnd('\\');
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning("Lecture du registre echouee (" + key + ") : " + ex.Message);
                    }
                }
            }

            return null;
        }

        private static string LocateOnDisk(string release, string edition)
        {
            foreach (var root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            })
            {
                if (string.IsNullOrEmpty(root)) continue;
                var candidate = Path.Combine(root, "Autodesk", "Navisworks " + edition + " " + release);
                if (Directory.Exists(candidate)) return candidate;
            }

            return null;
        }
    }
}
