using System.Windows;

namespace WLESS_CHRG_CTRL
{
    /// <summary>
    /// Finestra di dettaglio per un messaggio seriale: mostra sorgente,
    /// orario e il testo completo del messaggio con possibilita' di copia.
    /// </summary>
    public partial class MessageDetailsDialog : Window
    {
        public MessageDetailsDialog(string sourceName, SerialMessage message)
        {
            InitializeComponent();
            Title = $"Message Details - {sourceName}";
            txtHeaderTitle.Text = $"{sourceName} Message";
            txtSource.Text = $"SOURCE: {sourceName.ToUpperInvariant()}";
            txtTime.Text = message.Time;
            txtMessage.Text = message.Message;
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(txtMessage.Text))
                Clipboard.SetText(txtMessage.Text);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
