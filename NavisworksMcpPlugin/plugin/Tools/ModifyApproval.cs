using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NavisworksMcpPlugin.Mcp;
using NavisworksMcpPlugin.Plugin;

namespace NavisworksMcpPlugin.Tools
{
    /// <summary>
    /// Approbation des ecritures, sur le modele de la politique "ask" de pyRevit.
    ///
    /// L'agent ne decide pas seul de modifier la maquette : avant que navis_modify execute quoi
    /// que ce soit, Navisworks montre a l'utilisateur le titre donne par l'agent, la liste
    /// exacte des appels qui ecrivent dans le document (lue dans l'IL, pas declaree par
    /// l'agent) et le code. Rien ne s'execute sans "Appliquer".
    ///
    /// Difference avec pyRevit, assumee : pyRevit execute puis propose Keep / Discard, parce
    /// que Revit sait annuler une transaction. Navisworks n'offre pas d'annulation fiable pour
    /// tout (visibilite, selection, camera ne passent pas par l'historique). On demande donc
    /// AVANT d'executer, ce qui ne suppose aucune capacite d'annulation.
    ///
    /// Politique reglee par la variable d'environnement ALIOVA_NAVISWORKS_MCP_MODIFY :
    ///   ask   (defaut) demander a chaque appel ;
    ///   allow executer sans demander (la verification de l'IL et les blocages restent) ;
    ///   deny  refuser toute ecriture.
    /// </summary>
    public static class ModifyApproval
    {
        public const string PolicyEnvironmentVariable = "ALIOVA_NAVISWORKS_MCP_MODIFY";

        public enum Policy { Ask, Allow, Deny }

        public static Policy Current
        {
            get
            {
                var raw = (Environment.GetEnvironmentVariable(PolicyEnvironmentVariable) ?? "").Trim().ToLowerInvariant();
                if (raw == "allow") return Policy.Allow;
                if (raw == "deny") return Policy.Deny;
                return Policy.Ask;
            }
        }

        /// <summary>A appeler sur le fil d'interface. true si l'utilisateur approuve.</summary>
        public static bool Ask(string title, IList<string> mutations, string code)
        {
            using (var form = BuildForm(title, mutations, code))
            {
                return form.ShowDialog() == DialogResult.OK;
            }
        }

        private static Form BuildForm(string title, IList<string> mutations, string code)
        {
            var fr = Texts.Fr;
            var form = new Form
            {
                Text = Texts.DialogTitle + (fr ? " : modification demandee par l'agent" : " : change requested by the agent"),
                StartPosition = FormStartPosition.CenterScreen,
                Size = new Size(820, 640),
                MinimumSize = new Size(560, 420),
                TopMost = true,
                ShowInTaskbar = false,
                MinimizeBox = false,
                MaximizeBox = true,
                Font = new Font("Segoe UI", 9f)
            };

            var header = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 56,
                Padding = new Padding(10, 8, 10, 0),
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Text = (string.IsNullOrWhiteSpace(title) ? (fr ? "(sans titre)" : "(untitled)") : title.Trim()) +
                       Environment.NewLine +
                       (fr ? "Rien n'a encore ete execute. Enregistrez le .nwf avant d'appliquer si besoin."
                           : "Nothing has run yet. Save the .nwf before applying if needed.")
            };

            var callsLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Padding = new Padding(10, 4, 10, 0),
                Text = mutations.Count == 0
                    ? (fr ? "Aucun appel d'ecriture detecte dans le code." : "No write call detected in the code.")
                    : (fr ? "Appels qui modifient le document (" + mutations.Count + ") :"
                          : "Calls that change the document (" + mutations.Count + "):")
            };

            var calls = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 110,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f),
                Text = string.Join(Environment.NewLine, mutations.Select(m => "  " + m))
            };

            var codeLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Padding = new Padding(10, 4, 10, 0),
                Text = fr ? "Code a executer :" : "Code to run:"
            };

            var codeBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 9f),
                Text = (code ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine)
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };
            var reject = new Button { Text = fr ? "Refuser" : "Reject", DialogResult = DialogResult.Cancel, Width = 110 };
            var apply = new Button { Text = fr ? "Appliquer" : "Apply", DialogResult = DialogResult.OK, Width = 110 };
            buttons.Controls.Add(reject);
            buttons.Controls.Add(apply);

            // Refuser est le choix par defaut : Entree ou Echap par reflexe ne modifie rien.
            form.AcceptButton = reject;
            form.CancelButton = reject;

            // L'ordre d'ajout compte avec Dock : Fill d'abord, puis les Top du bas vers le haut.
            form.Controls.Add(codeBox);
            form.Controls.Add(codeLabel);
            form.Controls.Add(calls);
            form.Controls.Add(callsLabel);
            form.Controls.Add(header);
            form.Controls.Add(buttons);

            form.Shown += (s, e) => reject.Focus();
            Diagnostics.Info("Approbation demandee : " + title);
            return form;
        }
    }
}
