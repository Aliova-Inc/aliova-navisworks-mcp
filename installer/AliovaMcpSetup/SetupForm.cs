using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Aliova.McpSetup
{
    /// <summary>
    /// Fenetre unique de l'installateur. Pas d'assistant en plusieurs pages : il n'y a qu'une
    /// decision a prendre, installer ou retirer. L'etat du poste est affiche d'emblee pour que
    /// l'utilisateur sache ce qui va etre touche avant de cliquer.
    ///
    /// Toute la mise en page passe par des conteneurs, sans une seule coordonnee absolue. La
    /// premiere version en positions fixes se decalait : le lien recouvrait le bouton
    /// principal, qui n'affichait plus que la fin de son libelle. Un texte plus long, une
    /// autre langue ou une autre densite d'ecran suffisent a casser une mise en page absolue.
    /// </summary>
    internal sealed class SetupForm : Form
    {
        private static readonly Color Ink = Color.FromArgb(24, 24, 27);
        private static readonly Color Muted = Color.FromArgb(102, 102, 112);
        private static readonly Color Line = Color.FromArgb(226, 226, 231);
        private static readonly Color Blue = Color.FromArgb(4, 118, 232);      // bleu de marque
        private static readonly Color Good = Color.FromArgb(20, 130, 90);
        private static readonly Color Warn = Color.FromArgb(198, 40, 40);      // rouge assombri, lisible

        private readonly List<NavisworksTarget> _targets;
        private readonly List<string> _legacy;

        private Label _title;
        private Label _subtitle;
        private Label _stateLabel;
        private LinkLabel _openFolder;
        private FlowLayoutPanel _stepsPanel;
        private Button _primary;
        private Button _remove;
        private Button _close;
        private LinkLabel _langFr;
        private LinkLabel _langEn;

        public SetupForm()
        {
            _targets = NavisworksDetector.Detect();
            _legacy = LegacyInstall.Find(_targets);

            BuildLayout();
            ApplyTexts();
        }

        private void BuildLayout()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9F);
            Icon = LoadIcon();

            // AutoScale en Dpi : la fenetre doit tenir aussi bien sur un ecran a 100 % que sur
            // un portable a 150 %.
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(660, 500);

            // --- en-tete : logo, nom du produit, bascule de langue --------------------------
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(20, 16, 20, 14),
                BackColor = Color.White,
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            header.Controls.Add(new PictureBox
            {
                Image = LoadLogo(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(52, 52),
                Margin = new Padding(0, 0, 14, 0),
                BackColor = Color.Transparent,
            }, 0, 0);

            _title = new Label
            {
                Font = new Font("Segoe UI", 14F, FontStyle.Regular),
                ForeColor = Ink,
                AutoSize = true,
                Margin = new Padding(0),
                UseMnemonic = false,
            };
            _subtitle = new Label
            {
                ForeColor = Muted,
                AutoSize = true,
                Margin = new Padding(1, 4, 0, 0),
                UseMnemonic = false,
            };

            var titles = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0, 2, 0, 0),
            };
            titles.Controls.Add(_title);
            titles.Controls.Add(_subtitle);
            header.Controls.Add(titles, 1, 0);

            header.Controls.Add(BuildLanguageSwitch(), 2, 0);

            var rule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Line };

            // --- etat du poste, puis le lien vers le dossier installe ----------------------
            // Le lien etait dans la barre d'actions, ou il empietait sur le bouton principal.
            // Sa place est ici : c'est une information sur l'etat, pas une action d'installation.
            _stateLabel = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(21, 14, 21, 4),
                ForeColor = Ink,
                UseMnemonic = false,
            };

            _openFolder = new LinkLabel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(21, 0, 21, 10),
                LinkColor = Blue,
                ActiveLinkColor = Blue,
                Visible = false,
            };
            _openFolder.Click += (s, e) => OpenBundleFolder();

            // --- deroule des etapes ---------------------------------------------------------
            _stepsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(21, 2, 21, 8),
                AutoScroll = true,
                BackColor = Color.White,
            };

            // --- barre d'actions ------------------------------------------------------------
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(20, 12, 20, 14),
                BackColor = Color.White,
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _remove = MakeButton(false);
            _remove.Click += OnUninstall;

            _primary = MakeButton(true);
            _primary.Click += OnInstall;

            _close = MakeButton(false);
            _close.Click += (s, e) => Close();

            var right = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0),
            };
            right.Controls.Add(_primary);
            right.Controls.Add(_close);

            footer.Controls.Add(_remove, 0, 0);
            footer.Controls.Add(new Panel { Height = 1, BackColor = Color.White, Dock = DockStyle.Fill }, 1, 0);
            footer.Controls.Add(right, 2, 0);

            var footerRule = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Line };

            // L'ordre d'ajout compte pour les docks : le dernier ajoute occupe la position la
            // plus exterieure. On pose donc le remplissage d'abord, les bandeaux ensuite.
            Controls.Add(_stepsPanel);
            Controls.Add(footer);
            Controls.Add(footerRule);
            Controls.Add(_openFolder);
            Controls.Add(_stateLabel);
            Controls.Add(rule);
            Controls.Add(header);

            AcceptButton = _primary;
            CancelButton = _close;
        }

        /// <summary>
        /// Bascule FR / EN. Le choix reste offert meme si la langue de Windows est detectee :
        /// dans un bureau bilingue, le poste et l'utilisateur ne parlent pas toujours la meme.
        /// </summary>
        private Control BuildLanguageSwitch()
        {
            _langFr = MakeLangLink("FR", Strings.Lang.Fr);
            _langEn = MakeLangLink("EN", Strings.Lang.En);

            var separator = new Label
            {
                Text = "|",
                ForeColor = Line,
                AutoSize = true,
                Margin = new Padding(4, 3, 4, 0),
            };

            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(12, 6, 0, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            panel.Controls.Add(_langFr);
            panel.Controls.Add(separator);
            panel.Controls.Add(_langEn);
            return panel;
        }

        private LinkLabel MakeLangLink(string text, Strings.Lang lang)
        {
            var link = new LinkLabel
            {
                Text = text,
                AutoSize = true,
                LinkColor = Muted,
                ActiveLinkColor = Blue,
                Margin = new Padding(0, 3, 0, 0),
            };
            link.Click += (s, e) =>
            {
                if (Strings.Current == lang) return;
                Strings.Current = lang;
                ApplyTexts();
                ClearSteps();   // les etapes deja affichees sont dans l'ancienne langue
            };
            return link;
        }

        private static Button MakeButton(bool primary)
        {
            var button = new Button
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14, 6, 14, 6),
                MinimumSize = new Size(104, 32),
                Margin = new Padding(0, 0, 8, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Blue : Color.White,
                ForeColor = primary ? Color.White : Ink,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };

            if (primary) button.FlatAppearance.BorderSize = 0;
            else button.FlatAppearance.BorderColor = Line;

            return button;
        }

        // --- textes et etat ---------------------------------------------------------------

        /// <summary>
        /// Pose tous les textes visibles. Appelee a l'ouverture et a chaque changement de
        /// langue : c'est le seul endroit ou une chaine d'interface est ecrite.
        /// </summary>
        private void ApplyTexts()
        {
            Text = Strings.WindowTitle;
            _title.Text = Strings.ProductName;
            _subtitle.Text = Strings.Subtitle(PluginInstaller.PayloadVersion);
            _openFolder.Text = Strings.OpenFolder;
            _remove.Text = Strings.Uninstall;
            _close.Text = Strings.Close;

            var active = new Font(Font, FontStyle.Bold);
            _langFr.Font = Strings.IsFr ? active : Font;
            _langEn.Font = Strings.IsFr ? Font : active;
            _langFr.LinkColor = Strings.IsFr ? Ink : Muted;
            _langEn.LinkColor = Strings.IsFr ? Muted : Ink;

            RefreshState();
        }

        private void RefreshState()
        {
            var lines = new List<string>();

            if (_targets.Count == 0)
            {
                lines.Add(Strings.NavisworksNotFound);
                lines.Add(Strings.NavisworksNotFoundHint);
            }
            else
            {
                lines.Add(Strings.Detected(string.Join("   ", _targets.Select(t => t.ToString()))));
            }

            var installed = PluginInstaller.InstalledVersion;
            if (installed == null) lines.Add(Strings.NotInstalled);
            else if (installed == PluginInstaller.PayloadVersion) lines.Add(Strings.AlreadyInstalled(installed));
            else lines.Add(Strings.UpdateAvailable(installed, PluginInstaller.PayloadVersion));

            if (_legacy.Count > 0) lines.Add(Strings.LegacyFound(_legacy.Count));

            // MaximumSize impose le retour a la ligne. Sans elle, un Label AutoSize ancre en
            // haut s'etend au-dela de la fenetre et le texte est coupe a droite.
            _stateLabel.MaximumSize = new Size(ClientSize.Width - 44, 0);
            _stateLabel.Text = string.Join(Environment.NewLine, lines);

            if (installed == null) _primary.Text = Strings.Install;
            else if (installed == PluginInstaller.PayloadVersion) _primary.Text = Strings.Reinstall;
            else _primary.Text = Strings.Update;

            _remove.Enabled = PluginInstaller.IsInstalled;
            _openFolder.Visible = PluginInstaller.IsInstalled;
        }

        // --- actions ---------------------------------------------------------------------

        private void OnInstall(object sender, EventArgs e)
        {
            ClearSteps();

            // Navisworks ouvert verrouille la DLL du plugin : on refuse avant de toucher au
            // disque, plutot que d'echouer a mi-parcours et laisser une installation partielle.
            if (PluginInstaller.RunningNavisworks().Count > 0)
            {
                AddStep(Strings.NavisworksOpen, StepKind.Warning);
                MessageBox.Show(this, Strings.NavisworksOpenDetail(_primary.Text),
                    Strings.WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true);
            try
            {
                if (_legacy.Count > 0) RemoveLegacy();

                foreach (var step in PluginInstaller.Install())
                    AddStep(step.Text, step.Warning ? StepKind.Warning : StepKind.Ok);

                AddStep("", StepKind.Plain);
                AddStep(Strings.ServerWillListen(McpUrl), StepKind.Plain);
                AddStep(Strings.LaunchHint, StepKind.Plain);

                _legacy.Clear();
                _legacy.AddRange(LegacyInstall.Find(_targets));
                RefreshState();
            }
            catch (Exception ex)
            {
                AddStep(Strings.InstallInterrupted(ex.Message), StepKind.Warning);
                MessageBox.Show(this, ex.Message, Strings.WindowTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void RemoveLegacy()
        {
            if (LegacyInstall.NeedsElevation(_legacy) && !LegacyInstall.IsElevated)
            {
                AddStep(Strings.LegacyElevationAsked, StepKind.Plain);
                if (LegacyInstall.RunElevatedCleanup())
                {
                    AddStep(Strings.LegacyRemoved, StepKind.Ok);
                    return;
                }

                AddStep(Strings.LegacyKept, StepKind.Warning);
                AddStep(Strings.LegacyKeptConsequence, StepKind.Warning);
                return;
            }

            var failed = LegacyInstall.Remove(_legacy);
            AddStep(failed.Count == 0 ? Strings.LegacyRemoved : Strings.LegacyPartial(failed.Count),
                failed.Count == 0 ? StepKind.Ok : StepKind.Warning);
        }

        private void OnUninstall(object sender, EventArgs e)
        {
            var answer = MessageBox.Show(this, Strings.ConfirmUninstall, Strings.WindowTitle,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes) return;

            ClearSteps();

            if (PluginInstaller.RunningNavisworks().Count > 0)
            {
                AddStep(Strings.NavisworksOpenUninstall, StepKind.Warning);
                return;
            }

            SetBusy(true);
            try
            {
                foreach (var step in PluginInstaller.Uninstall())
                    AddStep(step.Text, step.Warning ? StepKind.Warning : StepKind.Ok);

                AddStep("", StepKind.Plain);
                AddStep(Strings.McpDeclarationRemains, StepKind.Plain);
                AddStep(Strings.McpDeclarationHarmless, StepKind.Plain);

                RefreshState();
            }
            catch (Exception ex)
            {
                AddStep(Strings.UninstallInterrupted(ex.Message), StepKind.Warning);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private static string McpUrl
        {
            // Doit rester aligne sur McpEndpoint.DefaultPort dans le plugin.
            get { return "http://127.0.0.1:8766/mcp"; }
        }

        private void OpenBundleFolder()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = PluginInstaller.BundleDir,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Strings.WindowTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // --- deroule ---------------------------------------------------------------------

        private enum StepKind { Ok, Warning, Plain }

        private void ClearSteps()
        {
            // Les controles retires doivent etre liberes : sur plusieurs installations de
            // suite, les laisser au ramasse-miettes garde des handles de fenetre ouverts.
            var old = _stepsPanel.Controls.Cast<Control>().ToList();
            _stepsPanel.Controls.Clear();
            foreach (var control in old) control.Dispose();
        }

        private void AddStep(string text, StepKind kind)
        {
            if (string.IsNullOrEmpty(text))
            {
                _stepsPanel.Controls.Add(new Panel { Height = 8, Width = 1, Margin = new Padding(0) });
                return;
            }

            var mark = kind == StepKind.Ok ? "✓" : kind == StepKind.Warning ? "!" : "·";
            var color = kind == StepKind.Ok ? Good : kind == StepKind.Warning ? Warn : Muted;

            var row = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 3),
                // Largeur imposee pour que le texte long passe a la ligne au lieu de
                // declencher une barre de defilement horizontale.
                Width = _stepsPanel.ClientSize.Width - 46,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            row.Controls.Add(new Label
            {
                Text = mark,
                ForeColor = color,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0),
            }, 0, 0);

            row.Controls.Add(new Label
            {
                Text = text,
                ForeColor = kind == StepKind.Plain ? Muted : Ink,
                AutoSize = true,
                MaximumSize = new Size(_stepsPanel.ClientSize.Width - 70, 0),
                Margin = new Padding(0),
                UseMnemonic = false,
            }, 1, 0);

            _stepsPanel.Controls.Add(row);
        }

        private void SetBusy(bool busy)
        {
            _primary.Enabled = !busy;
            _remove.Enabled = !busy && PluginInstaller.IsInstalled;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            Application.DoEvents();
        }

        // --- ressources ------------------------------------------------------------------

        private static Image LoadLogo()
        {
            try
            {
                using (var stream = typeof(SetupForm).Assembly.GetManifestResourceStream("Aliova.McpSetup.logo.png"))
                {
                    if (stream == null) return null;
                    // On recopie en memoire : Image garde le flux ouvert, et un flux de
                    // ressource dispose ferait echouer le premier rendu.
                    var buffer = new System.IO.MemoryStream();
                    stream.CopyTo(buffer);
                    buffer.Position = 0;
                    return Image.FromStream(buffer);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Logo non charge : " + ex.Message);
                return null;
            }
        }

        private static Icon LoadIcon()
        {
            try
            {
                return Icon.ExtractAssociatedIcon(typeof(SetupForm).Assembly.Location);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Icone non chargee : " + ex.Message);
                return null;
            }
        }
    }
}
