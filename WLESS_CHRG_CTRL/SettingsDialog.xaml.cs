using System;
using System.IO.Ports;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WLESS_CHRG_CTRL
{
    public partial class SettingsDialog : Window
    {
        public string BannedPortsValue { get; private set; } = string.Empty;
        public int BaudRate { get; private set; } = 115200;
        public int DataBits { get; private set; } = 8;
        public Parity ParityValue { get; private set; } = Parity.None;
        public StopBits StopBitsValue { get; private set; } = StopBits.One;
        public bool IsApplied { get; private set; } = false;

        // Evita che gli SelectionChanged sparino durante LoadCurrentSettings()
        private readonly bool isInitializing = true;

        public SettingsDialog()
        {
            InitializeComponent();
            LoadCurrentSettings();
            isInitializing = false;
        }

        private void LoadCurrentSettings()
        {
            var settings = Properties.Settings.Default;

            // Banned Ports
            txtBannedPorts.Text = settings.BannedPorts ?? string.Empty;

            // Baud Rate
            string baudStr = settings.SerialBaudRate.ToString();
            if (cbBaudRate.Items.Contains(baudStr))
                cbBaudRate.SelectedItem = baudStr;
            else
                cbBaudRate.Text = baudStr;

            // Data Bits
            string dataBitsStr = settings.SerialDataBits.ToString();
            cbDataBits.SelectedItem = dataBitsStr;

            // Parity / Stop Bits: selezioniamo direttamente il valore enum
            cbParity.SelectedItem = Enum.TryParse<Parity>(settings.SerialParity, out var parity)
                ? parity : Parity.None;
            cbStopBits.SelectedItem = Enum.TryParse<StopBits>(settings.SerialStopBits, out var stopBits)
                ? stopBits : StopBits.One;

            UpdateConfigDisplay();
        }

        // Aggiorna live la scritta "Current: ..." ogni volta che l'utente cambia un ComboBox
        private void Cb_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isInitializing) return;
            UpdateConfigDisplay();
        }

        // Aggiorna live anche mentre l'utente digita un baud rate custom
        private void CbBaudRate_KeyUp(object sender, KeyEventArgs e)
        {
            if (isInitializing) return;
            UpdateConfigDisplay();
        }

        private void UpdateConfigDisplay()
        {
            string baud = cbBaudRate.Text;
            string data = cbDataBits.SelectedItem?.ToString() ?? "8";
            Parity parity = cbParity.SelectedItem is Parity p ? p : Parity.None;
            StopBits stop = cbStopBits.SelectedItem is StopBits s ? s : StopBits.One;

            string parityAbbr = parity switch
            {
                Parity.None => "N",
                Parity.Odd => "O",
                Parity.Even => "E",
                Parity.Mark => "M",
                Parity.Space => "S",
                _ => "N"
            };

            string stopAbbr = stop switch
            {
                StopBits.None => "0",
                StopBits.One => "1",
                StopBits.Two => "2",
                StopBits.OnePointFive => "1.5",
                _ => "1"
            };

            txtCurrentConfig.Text = $"Current: {baud}, {data}, {parityAbbr}, {stopAbbr}";
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsApplied = false;
            DialogResult = false;
            Close();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateAndApply())
                return;

            IsApplied = true;
            DialogResult = true;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateAndApply())
                return;

            Properties.Settings.Default.Save();
            IsApplied = true;
            DialogResult = true;
            Close();
        }

        private bool ValidateAndApply()
        {
            // Validate Baud Rate
            if (!int.TryParse(cbBaudRate.Text.Trim(), out int baudRate) || baudRate <= 0)
            {
                MessageBox.Show("Invalid baud rate. Enter a positive integer.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                cbBaudRate.Focus();
                return false;
            }

            // Validate Data Bits
            if (cbDataBits.SelectedItem == null || !int.TryParse(cbDataBits.SelectedItem.ToString(), out int dataBits))
            {
                MessageBox.Show("Select valid data bits.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                cbDataBits.Focus();
                return false;
            }

            // Parity / StopBits: il ComboBox garantisce già un valore enum valido
            if (cbParity.SelectedItem is not Parity parity)
            {
                MessageBox.Show("Select valid parity.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                cbParity.Focus();
                return false;
            }

            if (cbStopBits.SelectedItem is not StopBits stopBits)
            {
                MessageBox.Show("Select valid stop bits.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                cbStopBits.Focus();
                return false;
            }

            // Validate DataBits/StopBits combination (regole imposte da System.IO.Ports.SerialPort)
            if (dataBits == 5 && stopBits == StopBits.Two)
            {
                MessageBox.Show("5 data bits is not compatible with 2 stop bits.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                cbStopBits.Focus();
                return false;
            }
            if (dataBits != 5 && stopBits == StopBits.OnePointFive)
            {
                MessageBox.Show("1.5 stop bits requires 5 data bits.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                cbStopBits.Focus();
                return false;
            }

            // Normalizza Banned Ports (trim + rimuovi duplicati/vuoti)
            var normalizedPorts = txtBannedPorts.Text
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            // Apply to properties
            BannedPortsValue = string.Join(";", normalizedPorts);
            BaudRate = baudRate;
            DataBits = dataBits;
            ParityValue = parity;
            StopBitsValue = stopBits;

            // Apply to Settings
            Properties.Settings.Default.BannedPorts = BannedPortsValue;
            Properties.Settings.Default.SerialBaudRate = baudRate;
            Properties.Settings.Default.SerialDataBits = dataBits;
            Properties.Settings.Default.SerialParity = parity.ToString();
            Properties.Settings.Default.SerialStopBits = stopBits.ToString();

            UpdateConfigDisplay();
            return true;
        }
    }
}