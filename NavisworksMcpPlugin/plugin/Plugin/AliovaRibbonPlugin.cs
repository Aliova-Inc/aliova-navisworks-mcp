using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using NavisworksMcpPlugin.Mcp;
using NavisworksMcpPlugin.Tools;
using NavisApp = Autodesk.Navisworks.Api.Application;

namespace NavisworksMcpPlugin.Plugin
{
    /// <summary>
    /// Onglet de ruban Aliova. C'est la seule facon d'afficher une icone de marque dans
    /// Navisworks : un AddInPlugin n'apparait que comme une entree texte sous Add-Ins, et
    /// ni PluginAttribute ni AddInPluginAttribute n'exposent de propriete d'icone.
    ///
    /// Le mecanisme complet, verifie sur les rubans tiers installes sur le poste :
    ///   1. heriter de CommandHandlerPlugin (pas de AddInPlugin) ;
    ///   2. [RibbonLayout("AliovaMcpRibbon.xaml")] nomme le XAML de mise en page, resolu
    ///      dans le sous-dossier de langue a cote de la DLL (fr-FR, en-US en repli) ;
    ///   3. [RibbonTab] declare l'onglet, son Id doit egaler celui du RibbonTab du XAML ;
    ///   4. [Command] declare chaque bouton, son nom doit egaler l'Id du bouton dans le XAML,
    ///      et c'est la seule place ou l'on peut mettre Icon / LargeIcon (noms de fichiers
    ///      relatifs au dossier de la DLL).
    /// Si un Id ne correspond pas, le bouton s'affiche mais reste inerte : c'est le
    /// symptome a chercher en premier.
    ///
    /// Cette classe porte aussi le demarrage du serveur, et ce n'est pas un hasard. Le
    /// demarrage reposait sur un AddInPlugin declare en AddInLocation.None avec
    /// LoadForCanExecute : sans aucune presence dans l'interface, Navisworks ne lui demandait
    /// jamais son CanExecute, donc le serveur ne demarrait pas. Le plugin de ruban, lui, est
    /// forcement charge des que l'onglet se dessine. C'est le seul point d'accroche dont on
    /// puisse prouver qu'il est atteint.
    /// </summary>
    [Plugin("NavisworksMcpPlugin.Plugin.AliovaRibbonPlugin", "NMCP",
        DisplayName = "Aliova MCP",
        ToolTip = "Aliova tab: MCP server, Claude chat, about")]
    [RibbonLayout("AliovaMcpRibbon.xaml")]
    [RibbonTab(TabId,
        DisplayName = "Aliova",
        LoadForCanExecute = true)]
    [Command(CommandServer,
        Icon = "mcp_16.ico",
        LargeIcon = "mcp_32.ico",
        CanToggle = true,
        LoadForCanExecute = true,
        DisplayName = "MCP server",
        ToolTip = "MCP channel state. Click to open or close.")]
    [Command(CommandChat,
        Icon = "chat_16.ico",
        LargeIcon = "chat_32.ico",
        CanToggle = true,
        LoadForCanExecute = true,
        DisplayName = "Claude chat",
        ToolTip = "Show or hide the Claude chat pane")]
    [Command(CommandAbout,
        Icon = "about_16.ico",
        LargeIcon = "about_32.ico",
        DisplayName = "About",
        ToolTip = "Version, channel address, available tools and log")]
    public class AliovaRibbonPlugin : CommandHandlerPlugin
    {
        // Ces constantes sont la source unique de verite : elles alimentent les attributs
        // ci-dessus ET l'aiguillage de ExecuteCommand. Le XAML doit reprendre les memes
        // chaines a la lettre.
        internal const string TabId = "ID_ALIOVA_MCP_TAB";
        internal const string CommandServer = "ID_ALIOVA_MCP_SERVER";
        internal const string CommandChat = "ID_ALIOVA_MCP_CHAT";
        internal const string CommandAbout = "ID_ALIOVA_MCP_ABOUT";

        private const string ChatPaneId = "NavisworksMcpPlugin.Chat.ChatDockPane.NMCP";

        private static readonly object StartGate = new object();
        private static bool _autoStartDone;
        private static Exception _startFailure;

        public override int ExecuteCommand(string name, params string[] parameters)
        {
            try
            {
                switch (name)
                {
                    case CommandServer:
                        ToggleServer();
                        break;
                    case CommandChat:
                        ToggleChatPane();
                        break;
                    case CommandAbout:
                        ShowAbout();
                        break;
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Error("Commande de ruban " + name + " en echec.", ex);
                MessageBox.Show(Texts.CommandError(ex.Message), Texts.DialogTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return 0;
        }

        /// <summary>
        /// Appele par Navisworks pour peindre l'etat des boutons. C'est ici que le bouton du
        /// serveur ecrit lui-meme son etat et son port : l'information doit etre lisible sur
        /// le ruban, pas cachee derriere une boite de dialogue.
        /// Doit rester rapide et ne jamais lever : il tourne a chaque rafraichissement.
        /// </summary>
        public override CommandState CanExecuteCommand(string name)
        {
            EnsureServerStarted();

            var state = new CommandState(true);

            try
            {
                switch (name)
                {
                    case CommandServer:
                        var running = McpHost.IsRunning;
                        state.IsChecked = running;

                        // Deux voies posees en meme temps : OverrideDisplayName, documentee
                        // mais non garantie sur un bouton de ruban, et l'ecriture directe par
                        // l'API AdWindows. Le journal dira laquelle a pris.
                        var label = ServerButtonLabel(running);
                        state.OverrideDisplayName = label;
                        RibbonStatus.Refresh(label, ServerButtonTooltip(running));
                        break;
                    case CommandChat:
                        state.IsChecked = IsChatPaneVisible();
                        break;
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Warn("CanExecuteCommand(" + name + ") : " + ex.Message);
            }

            return state;
        }

        public override bool CanExecuteRibbonTab(string name)
        {
            // Premier appel garanti du cycle de vie : l'onglet est evalue avant d'etre
            // dessine. On s'en sert comme point de demarrage du serveur.
            EnsureServerStarted();
            return name == TabId;
        }

        /// <summary>
        /// Libelle du bouton, ecrit en deux lignes comme le reste du ruban Navisworks.
        /// Il repond aux deux seules questions qui comptent : est-ce ouvert, et sur quel port.
        /// </summary>
        private static string ServerButtonLabel(bool running)
        {
            if (running) return Texts.ServerOpen(McpEndpoint.ActivePort);
            if (_startFailure != null) return Texts.ServerFailed();
            return Texts.ServerClosed();
        }

        /// <summary>
        /// Infobulle du bouton : elle porte l'adresse complete, celle a coller dans la
        /// configuration du client MCP, sans avoir a ouvrir quoi que ce soit.
        /// </summary>
        private static string ServerButtonTooltip(bool running)
        {
            if (running)
            {
                return Texts.TooltipOpen(
                    McpEndpoint.CurrentMcpUrl, McpEndpoint.IsOnFallbackPort, McpEndpoint.PreferredPort);
            }

            if (_startFailure != null) return Texts.TooltipFailed(DeepestMessage(_startFailure));

            return Texts.TooltipClosed(McpEndpoint.PreferredPort);
        }

        // --- demarrage -------------------------------------------------------------------

        /// <summary>
        /// Demarre le serveur une seule fois, quel que soit le nombre d'appels du ruban.
        ///
        /// Aucune boite de dialogue ici, sous aucun pretexte. Cette methode est appelee depuis
        /// CanExecuteCommand, c'est-a-dire pendant que Navisworks DESSINE son ruban : y ouvrir
        /// une fenetre modale fait pomper la boucle de messages et reentrer Navisworks dans
        /// lui-meme. C'est ce qui l'a fait planter lors d'une session precedente. Un echec est
        /// donc journalise et affiche sur le bouton, et la fenetre d'explication n'apparait
        /// que si l'utilisateur clique.
        /// </summary>
        private static void EnsureServerStarted()
        {
            lock (StartGate)
            {
                if (_autoStartDone) return;
                _autoStartDone = true;
            }

            try
            {
                Diagnostics.Info("Demarrage automatique demande depuis le ruban Aliova.");
                ServerLifecycle.Start();
                Diagnostics.Info("Canal MCP ouvert sur " + McpEndpoint.CurrentMcpUrl);

                if (McpEndpoint.IsOnFallbackPort)
                {
                    Diagnostics.Warn(
                        "Port " + McpEndpoint.PreferredPort + " occupe, repli sur " + McpEndpoint.ActivePort +
                        ". La configuration du client MCP doit pointer sur " + McpEndpoint.CurrentMcpUrl);
                }
            }
            catch (Exception ex)
            {
                _startFailure = ex;
                Diagnostics.Error("Le canal MCP n'a pas pu s'ouvrir au demarrage.", ex);
            }
        }

        /// <summary>
        /// Explique l'echec. A n'appeler que depuis ExecuteCommand, sur clic de l'utilisateur :
        /// une fenetre modale y est sans danger, contrairement au passage de peinture du ruban.
        /// </summary>
        private static void ShowStartFailure()
        {
            MessageBox.Show(
                Texts.StartFailure(DeepestMessage(_startFailure), McpEndpoint.PreferredPort, Diagnostics.LogPath),
                Texts.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string DeepestMessage(Exception ex)
        {
            if (ex == null) return Texts.Fr ? "inconnue" : "unknown";
            var current = ex;
            while (current.InnerException != null) current = current.InnerException;
            return current.Message;
        }

        private static void ToggleServer()
        {
            if (McpHost.IsRunning)
            {
                McpHost.StopAsync().GetAwaiter().GetResult();
                Diagnostics.Info("Canal MCP ferme depuis le ruban.");
                return;
            }

            // Nouvelle tentative explicite : on efface l'echec precedent pour que le bouton
            // reflete le resultat de CETTE tentative et pas de l'ancienne.
            _startFailure = null;
            try
            {
                ServerLifecycle.Start();
                Diagnostics.Info("Canal MCP ouvert depuis le ruban sur " + McpEndpoint.CurrentMcpUrl);

                if (McpEndpoint.IsOnFallbackPort)
                {
                    // Le client MCP porte l'adresse dans sa configuration : un port different
                    // le laisserait sans connexion. C'est le seul cas de succes qui merite
                    // d'interrompre, et on est ici sur un clic, pas sur une peinture.
                    MessageBox.Show(
                        Texts.FallbackPortWarning(McpEndpoint.PreferredPort, McpEndpoint.CurrentMcpUrl),
                        Texts.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                _startFailure = ex;
                Diagnostics.Error("Ouverture du canal MCP refusee.", ex);
                ShowStartFailure();
            }
        }

        // --- panneau de discussion --------------------------------------------------------

        private static void ToggleChatPane()
        {
            var pane = LoadChatPane();
            if (pane == null)
            {
                MessageBox.Show(
                    Texts.ChatPaneMissing(PluginFolder()),
                    Texts.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            pane.Visible = !pane.Visible;
        }

        private static bool IsChatPaneVisible()
        {
            // On ne charge PAS le panneau ici : peindre le ruban ne doit pas avoir d'effet
            // de bord. Un panneau pas encore charge est simplement declare non visible.
            var record = NavisApp.Plugins.FindPlugin(ChatPaneId);
            if (record == null || !record.IsLoaded) return false;

            var pane = record.LoadedPlugin as DockPanePlugin;
            return pane != null && pane.Visible;
        }

        private static DockPanePlugin LoadChatPane()
        {
            var record = NavisApp.Plugins.FindPlugin(ChatPaneId);
            if (record == null) return null;
            if (!record.IsLoaded) record.LoadPlugin();
            return record.LoadedPlugin as DockPanePlugin;
        }

        // --- a propos ---------------------------------------------------------------------

        private static void ShowAbout()
        {
            var asm = Assembly.GetExecutingAssembly();
            var running = McpHost.IsRunning;

            var message =
                Texts.AboutProduct() + Environment.NewLine +
                Texts.AboutVersion() + asm.GetName().Version + Environment.NewLine +
                Environment.NewLine +
                Texts.AboutChannel() + (running ? Texts.AboutOpen() : Texts.AboutClosed()) + Environment.NewLine +
                Texts.AboutAddress() + McpEndpoint.CurrentMcpUrl + Environment.NewLine +
                Texts.AboutPort() + (running
                    ? McpEndpoint.ActivePort.ToString()
                    : McpEndpoint.PreferredPort + Texts.AboutPreferred()) + Environment.NewLine +
                Texts.AboutTools() + CountTools(asm) + Environment.NewLine +
                Texts.AboutToken() + AccessGuard.TokenFilePath + Environment.NewLine +
                Texts.AboutModifyPolicy() + ModifyApproval.Current.ToString().ToLowerInvariant() + Environment.NewLine +
                Texts.AboutRuns() + RunJournal.RunsDirectory + Environment.NewLine +
                Environment.NewLine +
                Texts.AboutPackage() + PluginFolder() + Environment.NewLine +
                Texts.AboutLog() + Diagnostics.LogPath;

            if (_startFailure != null)
            {
                message += Environment.NewLine + Environment.NewLine +
                    Texts.AboutLastError() + Environment.NewLine +
                    DeepestMessage(_startFailure);
            }

            // Le repli de port merite d'etre dit : le client MCP porte l'adresse dans sa
            // configuration, un port different le laisserait sans connexion et sans
            // explication.
            if (McpEndpoint.IsOnFallbackPort)
            {
                message += Environment.NewLine + Environment.NewLine +
                    Texts.AboutFallback(McpEndpoint.PreferredPort);
            }

            message += Environment.NewLine + Environment.NewLine + Texts.AboutChoices();

            var answer = MessageBox.Show(message, Texts.DialogTitle,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button3);

            if (answer == DialogResult.Yes) CopyClientCommand();
            else if (answer == DialogResult.No) OpenLogFolder();
        }

        /// <summary>
        /// Copie la commande `claude mcp add` complete, jeton compris : c'est la seule chose
        /// que l'utilisateur ait a faire cote client, et recopier un jeton de 64 caracteres a
        /// la main est une source d'erreur garantie.
        /// </summary>
        private static void CopyClientCommand()
        {
            try
            {
                Clipboard.SetText(AccessGuard.ClaudeCodeAddCommand(McpEndpoint.CurrentMcpUrl));
                MessageBox.Show(Texts.ClientCommandCopied(), Texts.DialogTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Diagnostics.Warn("Copie de la commande client refusee : " + ex.Message);
            }
        }

        private static void OpenLogFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(Diagnostics.LogDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = Diagnostics.LogDirectory,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Diagnostics.Warn("Ouverture du dossier de journal refusee : " + ex.Message);
            }
        }

        private static string PluginFolder()
        {
            try
            {
                var location = typeof(AliovaRibbonPlugin).Assembly.Location;
                return string.IsNullOrEmpty(location) ? "inconnu" : System.IO.Path.GetDirectoryName(location);
            }
            catch (Exception)
            {
                return "inconnu";
            }
        }

        private static int CountTools(Assembly asm)
        {
            try
            {
                return asm.GetTypes()
                    .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    .Count(m => m.GetCustomAttribute<McpServerToolAttribute>() != null);
            }
            catch (Exception ex)
            {
                Diagnostics.Warn("Comptage des outils impossible : " + ex.Message);
                return 0;
            }
        }
    }
}
