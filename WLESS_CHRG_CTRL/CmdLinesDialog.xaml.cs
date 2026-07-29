using System;
using System.Collections.Generic;
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

        public CmdLinesDialog()
        {
            InitializeComponent();
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            // Parsa il delay
            if (nudDelay.Value is null || nudDelay.Value < 0)
            {
                MessageBox.Show("Inserisci un valore di delay valido (numero intero >= 0).",
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
                MessageBox.Show("Inserisci almeno un comando.",
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