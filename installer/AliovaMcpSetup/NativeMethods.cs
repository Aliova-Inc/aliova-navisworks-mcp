using System.Runtime.InteropServices;

namespace Aliova.McpSetup
{
    internal static class NativeMethods
    {
        /// <summary>
        /// Supprime un fichier ou un flux alternatif NTFS. File.Delete refuse la syntaxe
        /// "fichier:flux", il faut donc passer par l'API Win32 pour retirer le
        /// Zone.Identifier qui bloque le chargement des assemblies telechargees.
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteFile(string name);
    }
}
