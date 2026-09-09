using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace WLESS_CHRG_CTRL
{
    /// <summary>
    /// Rappresenta un comando con il suo delay opzionale (in ms).
    /// </summary>
    public class CommandWithDelay
    {
        public string Command { get; set; } = string.Empty;
        public int Delay { get; set; } = 0;
    }

    public partial class CmdLinesDialog : Window
    {
        /// <summary>
        /// Lista dei comandi elaborati (una per elemento), popolata alla chiusura con OK.
        /// </summary>
        public List<CommandWithDelay> ParsedCommands { get; private set; } = [];

        /// <summary>
        /// Ritardo tra comandi in millisecondi, popolato alla chiusura con OK.
        /// </summary>
        public int CmdDelay { get; private set; } = 500;

        /// <summary>
        /// Costruttore che pre-popola le righe di comando a partire da un file
        /// script (.wcx). Usato sia dal drag&drop sia dal caricamento da menu File.
        /// </summary>
        public CmdLinesDialog(string filePath) : this()
        {
            LoadFromFile(filePath);
        }

        public CmdLinesDialog()
        {
            InitializeComponent();
        }

        private void LoadFromReader(StreamReader reader)
        {
            try
            {
                var lines = new List<string>();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        lines.Add(trimmed);
                }

                txtCmdLines.Text = string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading script content:\n{ex.Message}",
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadFromFile(string filePath)
        {
            try
            {
                using var reader = new StreamReader(filePath);
                LoadFromReader(reader);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading script file:\n{ex.Message}",
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Estrae il comando effettivo da una riga, rimuovendo:
        /// - Commenti preceduti da '#'
        /// - Indicatore di delay '@numero'
        /// Ritorna il comando pulito (senza caratteri speciali).
        /// </summary>
        private static string ExtractCommand(string line)
        {
            // Rimuovi commenti (tutto da # in poi)
            int commentIndex = line.IndexOf('#');
            if (commentIndex >= 0)
                line = line[..commentIndex];

            // Rimuovi il delay marker (@numero)
            line = Regex.Replace(line, @"@\d+", "");

            return line.Trim();
        }

        /// <summary>
        /// Estrae il delay (in ms) da una riga, cercando @numero.
        /// Se presente, ritorna il valore numerico; altrimenti ritorna 0.
        /// </summary>
        private static int ExtractDelay(string line)
        {
            var match = Regex.Match(line, @"@(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int delay))
                return delay;
            return 0;
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            // Parsa il delay globale
            if (nudDelay.Value is null || nudDelay.Value < 0)
            {
                MessageBox.Show("Insert a valid delay value (positive integer).",
                    "Invalid Delay", MessageBoxButton.OK, MessageBoxImage.Warning);
                nudDelay.Focus();
                return;
            }

            // Parsa le righe di comando
            var lines = txtCmdLines.Text
                .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line))
                .ToList();

            if (lines.Count == 0)
            {
                MessageBox.Show("Insert at least one command.",
                    "No Commands", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtCmdLines.Focus();
                return;
            }

            // Elabora ogni riga: estrai comando e delay individuale
            ParsedCommands = [];
            foreach (var line in lines)
            {
                string command = ExtractCommand(line);
                if (string.IsNullOrEmpty(command))
                    continue; // Salta righe che diventano vuote dopo rimozione commenti

                int lineDelay = ExtractDelay(line);
                ParsedCommands.Add(new CommandWithDelay 
                { 
                    Command = command, 
                    Delay = lineDelay 
                });
            }

            if (ParsedCommands.Count == 0)
            {
                MessageBox.Show("No valid commands after parsing.",
                    "No Commands", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtCmdLines.Focus();
                return;
            }

            CmdDelay = (int)nudDelay.Value;

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
