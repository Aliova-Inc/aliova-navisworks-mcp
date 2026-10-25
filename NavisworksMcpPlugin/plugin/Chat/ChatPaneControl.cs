using System;
using System.Drawing;
using System.Windows.Forms;

namespace NavisworksMcpPlugin.Chat
{
    /// <summary>
    /// Chatbox WinForms dockable dans Navisworks. A l'envoi, lance `claude -p`
    /// (ClaudeRunner) en sous-processus avec le MCP Navis branche, et affiche la reponse.
    /// </summary>
    public class ChatPaneControl : UserControl
    {
        private readonly RichTextBox _history;
        private readonly TextBox _input;
        private readonly Button _send;
        private readonly Label _status;

        private static readonly Color UserColor = Color.FromArgb(86, 156, 214);   // bleu
        private static readonly Color AiColor   = Color.FromArgb(217, 119, 87);   // corail Claude
        private static readonly Color SysColor  = Color.Gray;
        private static readonly Color ErrColor  = Color.FromArgb(224, 108, 117);  // rouge doux

        // Preambule injecte devant la question pour cadrer le claude headless.
        private const string Preamble =
            "Tu es un assistant BIM integre a Navisworks. Tu disposes d'outils MCP 'navis' "
            + "(get_document_info, list_models, isolate_by_multi_search, search_by_name, search_by_property, "
            + "list_clash_tests, get_clash_results, color_override, etc.) pour inspecter et piloter "
            + "la maquette federee actuellement ouverte. Utilise ces outils si necessaire pour repondre, "
            + "puis donne une reponse CONCISE en francais (pas de bloc de code inutile). "
            + "Question de l'utilisateur :\n\n";

        public ChatPaneControl()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(40, 40, 40);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = Color.FromArgb(40, 40, 40),
                Padding = new Padding(4)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // historique
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));   // statut
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));   // saisie
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));

            _history = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.WhiteSmoke,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9.5f)
            };
            layout.Controls.Add(_history, 0, 0);
            layout.SetColumnSpan(_history, 2);

            _status = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = SysColor,
                Text = "Pret. Posez votre question.",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.25f)
            };
            layout.Controls.Add(_status, 0, 1);
            layout.SetColumnSpan(_status, 2);

            _input = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(55, 55, 55),
                ForeColor = Color.WhiteSmoke,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f)
            };
            _input.KeyDown += OnInputKeyDown;
            layout.Controls.Add(_input, 0, 2);

            _send = new Button
            {
                Dock = DockStyle.Fill,
                Text = "Envoyer",
                FlatStyle = FlatStyle.Flat,
                BackColor = AiColor,
                ForeColor = Color.White
            };
            _send.FlatAppearance.BorderSize = 0;
            _send.Click += (s, e) => Send();
            layout.Controls.Add(_send, 1, 2);

            Controls.Add(layout);

            AppendMessage("Info",
                "Bonjour. Posez une question sur la maquette Navisworks "
                + "(ex : \"combien de modeles federes ?\", \"isole la plomberie\", "
                + "\"combien de clashs actifs ?\"). Claude pilotera Navisworks et repondra ici.",
                SysColor);
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                Send();
            }
        }

        // async void : gestionnaire d'evenement UI. L'await reprend sur le thread UI
        // (WindowsFormsSynchronizationContext), donc l'affichage se fait sans Invoke manuel.
        private async void Send()
        {
            var text = _input.Text.Trim();
            if (text.Length == 0) return;

            AppendMessage("Vous", text, UserColor);
            _input.Clear();
            SetBusy(true);
            SetStatus("Analyse en cours (claude -p)...");

            ClaudeRunner.Result result;
            try
            {
                result = await ClaudeRunner.AskAsync(Preamble + text);
            }
            catch (Exception ex)
            {
                result = new ClaudeRunner.Result { Ok = false, Error = ex.Message };
            }

            if (result.Ok)
            {
                AppendMessage("Claude", result.Text, AiColor);
                SetStatus("Reponse recue.");
            }
            else
            {
                AppendMessage("Erreur", result.Error, ErrColor);
                SetStatus("Echec.");
            }
            SetBusy(false);
        }

        private void SetBusy(bool busy)
        {
            _send.Enabled = !busy;
            _input.Enabled = !busy;
            if (!busy) _input.Focus();
        }

        private void SetStatus(string text)
        {
            _status.Text = text;
        }

        private void AppendMessage(string who, string body, Color color)
        {
            _history.SelectionStart = _history.TextLength;
            _history.SelectionColor = color;
            _history.SelectionFont = new Font(_history.Font, FontStyle.Bold);
            _history.AppendText(who + Environment.NewLine);

            _history.SelectionColor = Color.WhiteSmoke;
            _history.SelectionFont = new Font(_history.Font, FontStyle.Regular);
            _history.AppendText((body ?? "") + Environment.NewLine + Environment.NewLine);

            _history.SelectionStart = _history.TextLength;
            _history.ScrollToCaret();
        }
    }
}
