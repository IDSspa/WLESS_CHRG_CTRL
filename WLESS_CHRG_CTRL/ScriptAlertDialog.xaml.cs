using System.ComponentModel;
using System.Windows;

namespace WLESS_CHRG_CTRL
{
    public enum AlertDialogAction
    {
        Continue,
        Pause,
        Stop
    }

    public partial class ScriptAlertDialog : Window
    {
        private bool selectionMade;

        public AlertDialogAction SelectedAction { get; private set; } = AlertDialogAction.Pause;

        public ScriptAlertDialog(string role, string message)
        {
            InitializeComponent();
            txtContext.Text = $"{role.ToUpperInvariant()} SCRIPT — Operator confirmation";
            txtMessage.Text = message;
        }

        public AlertDialogAction ShowAlert()
        {
            ShowDialog();
            return SelectedAction;
        }

        private void Continue_Click(object sender, RoutedEventArgs e) => CloseWith(AlertDialogAction.Continue);

        private void Pause_Click(object sender, RoutedEventArgs e) => CloseWith(AlertDialogAction.Pause);

        private void Stop_Click(object sender, RoutedEventArgs e) => CloseWith(AlertDialogAction.Stop);

        private void CloseWith(AlertDialogAction action)
        {
            SelectedAction = action;
            selectionMade = true;
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!selectionMade)
                SelectedAction = AlertDialogAction.Pause;
        }
    }
}
