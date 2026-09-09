using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace WLESS_CHRG_CTRL
{
    public partial class CmdLinesDialog : Window
    {
        /// <summary>
        /// Lista dei comandi (una riga per elemento), popolata alla chiusura con OK.
        /// </summary>
        public List<string> CmdLines { get; private set; } = [];

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

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            // Parsa il delay
            if (nudDelay.Value is null || nudDelay.Value < 0)
            {
                MessageBox.Show("Insert a valid delay value (positive integer).",
                    "Invalid Delay", MessageBoxButton.OK, MessageBoxImage.Warning);
                nudDelay.Focus();
                return;
            }

            // Parsa le righe di comando (esclude righe vuote)
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

            CmdDelay = (int)nudDelay.Value;
            CmdLines = lines;

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