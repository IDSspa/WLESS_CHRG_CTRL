using System;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;

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

        // Valori disponibili
        private static readonly int[] BaudRates = [9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600];
        private static readonly int[] DataBitsValues = [5, 6, 7, 8];
        private static readonly string[] ParityNames = ["None", "Odd", "Even", "Mark", "Space"];
        private static readonly string[] StopBitsNames = ["None", "One", "Two", "OnePointFive"];

        public SettingsDialog()
        {
            InitializeComponent();
            InitializeComboBoxes();
            LoadCurrentSettings();
        }

        private void InitializeComboBoxes()
        {
            // Baud Rate
            foreach (var rate in BaudRates)
                cbBaudRate.Items.Add(rate.ToString());
            cbBaudRate.Items.Add("Custom...");

            // Data Bits
            foreach (var bits in DataBitsValues)
                cbDataBits.Items.Add(bits.ToString());

            // Parity
            foreach (var p in ParityNames)
                cbParity.Items.Add(p);

            // Stop Bits
            foreach (var s in StopBitsNames)
                cbStopBits.Items.Add(s);
        }

        private void LoadCurrentSettings()
        {
            var settings = WLESS_CHRG_CTRL.Properties.Settings.Default;

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

            // Parity
            string parityStr = settings.SerialParity ?? "None";
            cbParity.SelectedItem = parityStr;

            // Stop Bits
            string stopBitsStr = settings.SerialStopBits ?? "One";
            cbStopBits.SelectedItem = stopBitsStr;

            UpdateConfigDisplay();
        }

        private void UpdateConfigDisplay()
        {
            string baud = cbBaudRate.Text;
            string data = cbDataBits.SelectedItem?.ToString() ?? "8";
            string parity = cbParity.SelectedItem?.ToString() ?? "N";
            string stop = cbStopBits.SelectedItem?.ToString() ?? "1";

            // Abbreviazione parity per display
            string parityAbbr = parity switch
            {
                "None" => "N",
                "Odd" => "O",
                "Even" => "E",
                "Mark" => "M",
                "Space" => "S",
                _ => "N"
            };

            // Abbreviazione stop bits
            string stopAbbr = stop switch
            {
                "None" => "0",
                "One" => "1",
                "Two" => "2",
                "OnePointFive" => "1.5",
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
            txtStatus.Text = "Settings applied (not saved yet)";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateAndApply())
                return;

            WLESS_CHRG_CTRL.Properties.Settings.Default.Save();
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

            // Parse Parity
            if (cbParity.SelectedItem == null || !Enum.TryParse<Parity>(cbParity.SelectedItem.ToString(), out Parity parity))
            {
                MessageBox.Show("Select valid parity.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                cbParity.Focus();
                return false;
            }

            // Parse Stop Bits
            if (cbStopBits.SelectedItem == null || !Enum.TryParse<StopBits>(cbStopBits.SelectedItem.ToString(), out StopBits stopBits))
            {
                MessageBox.Show("Select valid stop bits.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                cbStopBits.Focus();
                return false;
            }

            // Apply to properties
            BannedPortsValue = txtBannedPorts.Text ?? string.Empty;
            BaudRate = baudRate;
            DataBits = dataBits;
            ParityValue = parity;
            StopBitsValue = stopBits;

            // Apply to Settings
            var settings = WLESS_CHRG_CTRL.Properties.Settings.Default;
            settings.BannedPorts = BannedPortsValue;
            settings.SerialBaudRate = baudRate;
            settings.SerialDataBits = dataBits;
            settings.SerialParity = parity.ToString();
            settings.SerialStopBits = stopBits.ToString();

            UpdateConfigDisplay();
            return true;
        }

        // Riferimento status bar
        private TextBlock txtStatus;

        public void SetStatusReference(TextBlock statusTextBlock)
        {
            txtStatus = statusTextBlock;
        }
    }
}