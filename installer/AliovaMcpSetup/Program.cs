using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Aliova.McpSetup
{
    internal static class Program
    {
        /// <summary>
        /// Argument du mode "nettoyage seul", utilise par l'instance relancee en
        /// administrateur. Ce mode ne pose rien : il retire l'ancienne installation dans
        /// Program Files, puis rend la main. Le paquet reste pose par l'utilisateur courant.
        /// </summary>
        public const string CleanupArgument = "--cleanup-legacy";

        /// <summary>
        /// Force la langue de l'interface : --lang=fr ou --lang=en. Sans cet argument, la
        /// langue suit celle de Windows et reste changeable par la bascule de l'en-tete.
        /// Utile pour un deploiement d'entreprise qui veut une langue imposee, et pour
        /// verifier la mise en page dans les deux langues sans piloter la souris.
        /// </summary>
        public const string LanguageArgument = "--lang=";

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args != null && args.Any(a => string.Equals(a, CleanupArgument, StringComparison.OrdinalIgnoreCase)))
            {
                return RunCleanupOnly();
            }

            ApplyLanguageArgument(args);

            Application.Run(new SetupForm());
            return 0;
        }

        private static void ApplyLanguageArgument(string[] args)
        {
            if (args == null) return;

            foreach (var arg in args)
            {
                if (arg == null) continue;
                if (!arg.StartsWith(LanguageArgument, StringComparison.OrdinalIgnoreCase)) continue;

                var value = arg.Substring(LanguageArgument.Length).Trim();
                if (value.StartsWith("fr", StringComparison.OrdinalIgnoreCase)) Strings.Current = Strings.Lang.Fr;
                else if (value.StartsWith("en", StringComparison.OrdinalIgnoreCase)) Strings.Current = Strings.Lang.En;
                return;
            }
        }

        /// <summary>
        /// Mode administrateur, sans interface : le code de sortie est le seul canal, lu par
        /// l'instance appelante. Aucune fenetre ici, sinon deux interfaces se superposeraient.
        /// </summary>
        private static int RunCleanupOnly()
        {
            try
            {
                var targets = NavisworksDetector.Detect();
                var leftovers = LegacyInstall.Find(targets);
                if (leftovers.Count == 0) return 0;

                var failed = LegacyInstall.Remove(leftovers);
                return failed.Count == 0 ? 0 : 1;
            }
            catch (Exception)
            {
                return 1;
            }
        }
    }
}
