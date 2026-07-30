using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WLESS_CHRG_CTRL
{
    public class SerialMessage
    {
        public string Time { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Brush TextColor { get; set; } = Brushes.Black;
    }

    public partial class MainWindow : Window
    {
        private bool isStationConnected = false;
        private bool isVehicleConnected = false;

        private readonly SerialPort spStation = new();
        private readonly SerialPort spVehicle = new();

        private readonly ObservableCollection<SerialMessage> stationMessages = [];
        private readonly ObservableCollection<SerialMessage> vehicleMessages = [];

        private static readonly Brush DefaultTextColor = Brushes.Black;
        private static readonly Brush TxTextColor = Brushes.Green;

        private readonly Dispatcher uiDispatcher;

        /// <summary>
        /// Nomi dei campi IN ORDINE, come definiti dal protocollo.
        /// Questi sono SOLO etichette UI — NON vengono trasmessi dal dispositivo.
        /// </summary>
        private static readonly List<string> UQFieldNames =
        [
            "synthetic_test_enable",
            "signal_valid_mask",
            "signal_missing_mask",
            "v_dc_mV",
            "v_bat_mV",
            "v_bat_adc_raw",
            "v_bat_sensed_mV",
            "p_bat_ref_mW",
            "i_l_ref_a_mA",
            "i_l_a_mA",
            "i_l_b_mA",
            "i_coil_local_mA",
            "duty_a_raw_ppm",
            "duty_a_mapped_ppm",
            "duty_a_applied_ppm",
            "pwm_enable_cmd",
            "power_output_enable",
            "bbc_enabled",
            "bbc_dock_test_enable",
            "duty_mapping_mode",
            "duty_max_milli_pu",
            "v_dc_ref_mV",
            "v_dc_trip_latched",
            "v_dc_trip_threshold_mV",
            "i_l_trip_threshold_mA",
            "i_l_trip_confirm_cycles",
            "v_dc_trip_capture_mV",
            "i_l_trip_latched",
            "i_l_trip_threshold_mA",
            "i_l_trip_capture_a_mA",
            "i_l_trip_capture_b_mA",
            "vdc_controller_gain_milli",
            "duty_a_ramped_ppm",
            "duty_ramp_step_ppm",
            "v_dc_ref_config_V",
            "i_bat_max_config_mA",
            "v_dc_ref_config_mV",
            "v_bat_config_mV",
            "v_dc_trip_config_mV",
            "hybrid_active",
            "i_bat_ref_min_mA",
            "i_bat_ref_max_mA",
            "duty_b_raw_ppm",
            "duty_b_mapped_ppm",
            "duty_b_applied_ppm",
            "duty_b_ramped_ppm",
            "i_l_error_a_mA",
            "i_l_error_b_mA",
            "current_polarity_mask",
            "i_l_raw_a_mA",
            "i_l_raw_b_mA"
        ];

        public MainWindow()
        {
            InitializeComponent();
            uiDispatcher = Dispatcher.CurrentDispatcher;

            lvStationMessages.ItemsSource = stationMessages;
            lvVehicleMessages.ItemsSource = vehicleMessages;

            PopulateSerialPorts();
            ConfigureSerialPort(spStation);
            ConfigureSerialPort(spVehicle);

            spStation.DataReceived += SpStation_DataReceived;
            spVehicle.DataReceived += SpVehicle_DataReceived;
        }

        /// <summary>
        /// Popola le ComboBox con l'elenco delle porte seriali disponibili,
        /// escludendo quelle presenti in BannedPorts (separate da ';').
        /// </summary>
        private void PopulateSerialPorts()
        {
            string[] allPorts = SerialPort.GetPortNames();

            // Carica e parsa le porte bannate
            var bannedPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string bannedSetting = Properties.Settings.Default.BannedPorts;

            if (!string.IsNullOrWhiteSpace(bannedSetting))
            {
                var split = bannedSetting.Split([';'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in split)
                {
                    bannedPorts.Add(p.Trim());
                }
            }

            // Filtra le porte disponibili
            var availablePorts = allPorts
                .Where(p => !bannedPorts.Contains(p.Trim()))
                .ToList();

            cbStationPort.Items.Clear();
            cbVehiclePort.Items.Clear();

            foreach (string port in availablePorts)
            {
                cbStationPort.Items.Add(port);
                cbVehiclePort.Items.Add(port);
            }

            if (cbStationPort.Items.Count > 0)
                cbStationPort.SelectedIndex = 0;
            if (cbVehiclePort.Items.Count > 0)
                cbVehiclePort.SelectedIndex = Math.Min(1, cbVehiclePort.Items.Count - 1);

            // Aggiorna status se alcune porte sono state filtrate
            int filteredCount = allPorts.Length - availablePorts.Count;
            if (filteredCount > 0)
            {
                txtStatus.Text = $"Found {availablePorts.Count} ports ({filteredCount} filtered by BannedPorts)";
            }
            else
            {
                txtStatus.Text = $"Found {availablePorts.Count} serial ports";
            }
        }

        /// <summary>
        /// Configura un SerialPort con i parametri dalle impostazioni utente
        /// </summary>
        private static void ConfigureSerialPort(SerialPort sp)
        {
            var settings = WLESS_CHRG_CTRL.Properties.Settings.Default;

            sp.BaudRate = settings.SerialBaudRate;
            sp.DataBits = settings.SerialDataBits;

            if (Enum.TryParse<Parity>(settings.SerialParity, out Parity parity))
                sp.Parity = parity;
            else
                sp.Parity = Parity.None;

            if (Enum.TryParse<StopBits>(settings.SerialStopBits, out StopBits stopBits))
                sp.StopBits = stopBits;
            else
                sp.StopBits = StopBits.One;

            sp.Handshake = Handshake.None;
            sp.ReadTimeout = 500;
            sp.WriteTimeout = 500;
            sp.Encoding = Encoding.ASCII;
            sp.NewLine = "\r\n";
        }

        private void SpStation_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = spStation.ReadExisting();
                AppendMessage(stationMessages, data, false);
            }
            catch (Exception ex)
            {
                AppendMessage(stationMessages, $"[ERROR] {ex.Message}", false);
            }
        }

        private void SpVehicle_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = spVehicle.ReadExisting();
                AppendMessage(vehicleMessages, data, false);
            }
            catch (Exception ex)
            {
                AppendMessage(vehicleMessages, $"[ERROR] {ex.Message}", false);
            }
        }

        private void AppendMessage(ObservableCollection<SerialMessage> collection, string message, bool isTx)
        {
            if (string.IsNullOrEmpty(message))
                return;

            var lines = message.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                string trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;

                uiDispatcher.Invoke(() =>
                {
                    collection.Add(new SerialMessage
                    {
                        Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                        Message = trimmedLine,
                        TextColor = isTx ? TxTextColor : DefaultTextColor
                    });

                    while (collection.Count > 1000)
                        collection.RemoveAt(0);

                    AutoResizeMessageColumn(collection);
                    ScrollToLastItem(collection == stationMessages ? lvStationMessages : lvVehicleMessages);
                });
            }
        }

        private void AutoResizeMessageColumn(ObservableCollection<SerialMessage> collection)
        {
            if (collection.Count == 0)
                return;

            GridViewColumn messageColumn;
            if (collection == stationMessages)
                messageColumn = gvcStationMessage;
            else
                messageColumn = gvcVehicleMessage;

            double maxWidth = 0;
            var typeface = new Typeface(this.FontFamily, this.FontStyle, this.FontWeight, this.FontStretch);

            foreach (var msg in collection)
            {
                var formattedText = new FormattedText(
                    msg.Message,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    this.FontSize,
                    Brushes.Black,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                if (formattedText.Width > maxWidth)
                    maxWidth = formattedText.Width;
            }

            maxWidth += 20;
            double minWidth = 200;
            double newWidth = Math.Max(maxWidth, minWidth);
            double maxAllowedWidth = 800;

            messageColumn.Width = Math.Min(newWidth, maxAllowedWidth);
        }

        private static void ScrollToLastItem(ListView listView)
        {
            if (listView.Items.Count > 0)
                listView.ScrollIntoView(listView.Items[^1]);
        }

        private void MenuClearStation_Click(object sender, RoutedEventArgs e)
        {
            stationMessages.Clear();
            gvcStationMessage.Width = 380;
            txtStatus.Text = "Station messages cleared";
        }

        private void MenuClearVehicle_Click(object sender, RoutedEventArgs e)
        {
            vehicleMessages.Clear();
            gvcVehicleMessage.Width = 380;
            txtStatus.Text = "Vehicle messages cleared";
        }

        private void MenuSaveStation_Click(object sender, RoutedEventArgs e)
        {
            SaveMessagesToFile(stationMessages, "Station");
        }

        private void MenuSaveVehicle_Click(object sender, RoutedEventArgs e)
        {
            SaveMessagesToFile(vehicleMessages, "Vehicle");
        }

        private void SaveMessagesToFile(ObservableCollection<SerialMessage> collection, string portName)
        {
            if (collection.Count == 0)
            {
                MessageBox.Show($"Nessun messaggio da salvare per {portName}.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = $"WLESS_{portName}_Messages_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Title = $"Salva messaggi {portName}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"=== WLESS CHRG CTRL - {portName} Messages ===");
                    sb.AppendLine($"Saved: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine($"Total messages: {collection.Count}");
                    sb.AppendLine(new string('=', 60));
                    sb.AppendLine();

                    foreach (var msg in collection)
                    {
                        sb.AppendLine($"[{msg.Time}] {msg.Message}");
                    }

                    File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                    txtStatus.Text = $"Salvati {collection.Count} messaggi in {System.IO.Path.GetFileName(dialog.FileName)}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Errore durante il salvataggio:\n{ex.Message}", "Errore",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnStationConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!isStationConnected)
            {
                if (cbStationPort.SelectedItem == null)
                {
                    MessageBox.Show("Select a serial port for Station!", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string stationPort = cbStationPort.SelectedItem.ToString()!;

                try
                {
                    spStation.PortName = stationPort;
                    spStation.Open();

                    ledStation.Fill = Brushes.LimeGreen;
                    cbStationPort.IsEnabled = false;
                    AppendMessage(stationMessages, "[SYSTEM] Connected to " + stationPort, false);

                    isStationConnected = true;
                    btnStationConnect.Content = "DISCONNECT";

                    txtStatus.Text = $"Station connected - {stationPort}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error connecting Station: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);

                    if (spStation.IsOpen) spStation.Close();
                }
            }
            else
            {
                try
                {
                    if (spStation.IsOpen) spStation.Close();
                }
                catch (Exception ex)
                {
                    AppendMessage(stationMessages, $"[ERROR] Disconnect: {ex.Message}", false);
                }

                isStationConnected = false;
                btnStationConnect.Content = "CONNECT";
                ledStation.Fill = Brushes.Gray;
                cbStationPort.IsEnabled = true;

                txtStatus.Text = "Station disconnected";
                AppendMessage(stationMessages, "[SYSTEM] Disconnected", false);
            }
        }

        private void BtnVehicleConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!isVehicleConnected)
            {
                if (cbVehiclePort.SelectedItem == null)
                {
                    MessageBox.Show("Select a serial port for Vehicle!", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string vehiclePort = cbVehiclePort.SelectedItem.ToString()!;

                try
                {
                    spVehicle.PortName = vehiclePort;
                    spVehicle.Open();

                    ledVehicle.Fill = Brushes.LimeGreen;
                    cbVehiclePort.IsEnabled = false;
                    AppendMessage(vehicleMessages, "[SYSTEM] Connected to " + vehiclePort, false);

                    isVehicleConnected = true;
                    btnVehicleConnect.Content = "DISCONNECT";

                    txtStatus.Text = $"Vehicle connected - {vehiclePort}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error connecting Vehicle: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);

                    if (spVehicle.IsOpen) spVehicle.Close();
                }
            }
            else
            {
                try
                {
                    if (spVehicle.IsOpen) spVehicle.Close();
                }
                catch (Exception ex)
                {
                    AppendMessage(vehicleMessages, $"[ERROR] Disconnect: {ex.Message}", false);
                }

                isVehicleConnected = false;
                btnVehicleConnect.Content = "CONNECT";
                ledVehicle.Fill = Brushes.Gray;
                cbVehiclePort.IsEnabled = true;

                txtStatus.Text = "Vehicle disconnected";
                AppendMessage(vehicleMessages, "[SYSTEM] Disconnected", false);
            }
        }
        private void TxtStationCommand_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                SendCommand(spStation, txtStationCommand, stationMessages);
        }

        private void TxtVehicleCommand_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                SendCommand(spVehicle, txtVehicleCommand, vehicleMessages);
        }

        private void SendCommand(SerialPort port, TextBox textBox,
            ObservableCollection<SerialMessage> collection)
        {
            string command = textBox.Text.Trim();

            if (string.IsNullOrEmpty(command))
                return;

            if (!port.IsOpen)
            {
                AppendMessage(collection, "[ERROR] Porta non connessa!", false);
                return;
            }

            try
            {
                if (!command.EndsWith("\r\n"))
                    command += "\r\n";

                port.Write(command);
                AppendMessage(collection, $"[TX] {textBox.Text.Trim()}", true);
                textBox.Clear();
            }
            catch (Exception ex)
            {
                AppendMessage(collection, $"[ERROR] Send failed: {ex.Message}", false);
            }
        }

        private void LvMessages_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (sender is not ListView listView || listView.SelectedItems.Count == 0)
                    return;

                var sb = new StringBuilder();

                foreach (SerialMessage msg in listView.SelectedItems.Cast<SerialMessage>())
                {
                    sb.AppendLine($"{msg.Time}\t{msg.Message}");
                }

                Clipboard.SetText(sb.ToString());
                txtStatus.Text = $"Copiati {listView.SelectedItems.Count} messaggi negli appunti";
                e.Handled = true;
            }
        }

        private void LvStationMessages_Loaded(object sender, RoutedEventArgs e)
        {
            gvcStationMessage.Width = 380;
        }

        private void LvVehicleMessages_Loaded(object sender, RoutedEventArgs e)
        {
            gvcVehicleMessage.Width = 380;
        }

        // ==================== CONTEXT MENU: PARSE UQ? RESPONSE ====================

        private void MenuParseStation_Click(object sender, RoutedEventArgs e)
        {
            ParseResponse(stationMessages, lvStationMessages, "Station");
        }

        private void MenuParseVehicle_Click(object sender, RoutedEventArgs e)
        {
            ParseResponse(vehicleMessages, lvVehicleMessages, "Vehicle");
        }

        private void ParseResponse(ObservableCollection<SerialMessage> collection,
            ListView listView, string sourceName)
        {
            if (listView.SelectedItem == null)
            {
                MessageBox.Show("Select the first row of the message to decode.",
                    "Parse Message", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Trova l'indice dell'elemento selezionato nella collection
            int startIndex = listView.SelectedIndex;
            if (startIndex < 0 || startIndex >= collection.Count)
            {
                MessageBox.Show("Invalid selection.", "Parse Message",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedMessage = collection[startIndex];

            // Verifica che la riga selezionata inizi con uno dei messaggi risposta validi (UQ?, U, ecc.)
            if (selectedMessage.Message.Trim().StartsWith("U,"))
                ParseUqResponse(collection, sourceName, startIndex);
            else
            {
                MessageBox.Show("The selected row does not start with any of the expected headers.\n" +
                    "Please select the first row of the message.",
                    "Parse Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

        }

        /// <summary>
        /// Parsa la risposta UQ? a partire dalla riga selezionata dall'utente.
        /// Raccoglie la riga selezionata e tutte le successive contigue fino
        /// a un messaggio di sistema o un altro comando.
        /// </summary>
        private void ParseUqResponse(ObservableCollection<SerialMessage> collection,
            string sourceName, int startIndex)
        {
            var selectedMessage = collection[startIndex];

            // Ricostruisce la risposta dalla riga selezionata in poi
            var uqFragments = new List<string>();

            // Primo frammento: rimuovi "U,"
            string firstFragment = selectedMessage.Message.Trim()[2..];
            uqFragments.Add(firstFragment);

            // Frammenti successivi: raccogli finché non trovi un messaggio di sistema o altro comando
            for (int i = startIndex + 1; i < collection.Count; i++)
            {
                string msg = collection[i].Message.Trim();

                // Interrompi se trovi un comando inviato, un errore di sistema, o un'altra risposta U,
                if (msg.StartsWith("[TX]") ||
                    msg.StartsWith("[SYSTEM]") ||
                    msg.StartsWith("[ERROR]") ||
                    msg.StartsWith("U,"))
                {
                    break;
                }

                uqFragments.Add(msg);
            }

            try
            {
                // Concatena tutti i frammenti
                var sb = new StringBuilder();
                for (int i = 0; i < uqFragments.Count; i++)
                {
                    string fragment = uqFragments[i];

                    // Aggiungi virgola di giunzione se necessario
                    if (i > 0 && sb.Length > 0 && sb[^1] != ',' && !fragment.StartsWith(','))
                    {
                        sb.Append(',');
                    }

                    sb.Append(fragment);
                }

                string fullPayload = sb.ToString();

                // Splitta per virgole
                var rawValues = fullPayload.Split([','], StringSplitOptions.RemoveEmptyEntries);

                // Trim e filtra
                var values = new List<string>();
                foreach (var v in rawValues)
                {
                    string trimmed = v.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        values.Add(trimmed);
                }

                // Apri dialog
                var dialog = new StatusDialog(sourceName, UQFieldNames, values)
                {
                    Owner = this
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante il parsing:\n{ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== DOUBLE CLICK: APERTURA DIALOG MULTILINEA ====================

        private void TxtStationCommand_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenCmdLinesDialog(spStation, txtStationCommand, stationMessages);
        }

        private void TxtVehicleCommand_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenCmdLinesDialog(spVehicle, txtVehicleCommand, vehicleMessages);
        }

        /// <summary>
        /// Apre il dialog multi-linea e, se confermato, invia i comandi in sequenza con ritardo.
        /// </summary>
        private void OpenCmdLinesDialog(SerialPort port, TextBox associatedTextBox,
            ObservableCollection<SerialMessage> messageCollection)
        {
            var dialog = new CmdLinesDialog
            {
                Owner = this
            };

            // Pre-popola con il contenuto attuale della TextBox (se presente)
            if (!string.IsNullOrWhiteSpace(associatedTextBox.Text))
            {
                dialog.txtCmdLines.Text = associatedTextBox.Text;
            }

            if (dialog.ShowDialog() == true)
            {
                // Avvia l'invio sequenziale in background per non bloccare la UI
                _ = SendCommandsSequenceAsync(port, dialog.CmdLines, dialog.CmdDelay,
                    messageCollection);
            }
        }

        /// <summary>
        /// Invia una sequenza di comandi con ritardo specificato tra uno e l'altro.
        /// Eseguito in background per non bloccare l'interfaccia utente.
        /// </summary>
        private async System.Threading.Tasks.Task SendCommandsSequenceAsync(
            SerialPort port,
            List<string> commands,
            int delayMs,
            ObservableCollection<SerialMessage> messageCollection)
        {
            if (!port.IsOpen)
            {
                uiDispatcher.Invoke(() =>
                {
                    AppendMessage(messageCollection, "[ERROR] Porta non connessa — sequenza annullata.", false);
                });
                return;
            }

            uiDispatcher.Invoke(() =>
            {
                AppendMessage(messageCollection, $"[SYSTEM] Starting sequence of {commands.Count} commands (delay: {delayMs}ms)", false);
            });

            for (int i = 0; i < commands.Count; i++)
            {
                string cmd = commands[i].Trim();

                if (string.IsNullOrEmpty(cmd))
                    continue;

                // Verifica connessione ancora attiva
                if (!port.IsOpen)
                {
                    uiDispatcher.Invoke(() =>
                    {
                        AppendMessage(messageCollection, "[ERROR] Connessione persa durante la sequenza.", false);
                    });
                    return;
                }

                try
                {
                    string cmdWithNewline = cmd.EndsWith("\r\n") ? cmd : cmd + "\r\n";
                    port.Write(cmdWithNewline);

                    uiDispatcher.Invoke(() =>
                    {
                        AppendMessage(messageCollection, $"[TX] [{i + 1}/{commands.Count}] {cmd}", true);
                    });
                }
                catch (Exception ex)
                {
                    uiDispatcher.Invoke(() =>
                    {
                        AppendMessage(messageCollection, $"[ERROR] Command {i + 1} failed: {ex.Message}", false);
                    });
                    return;
                }

                // Ritardo prima del prossimo comando (non dopo l'ultimo)
                if (i < commands.Count - 1 && delayMs > 0)
                {
                    await System.Threading.Tasks.Task.Delay(delayMs);
                }
            }

            uiDispatcher.Invoke(() =>
            {
                AppendMessage(messageCollection, $"[SYSTEM] Sequence completed ({commands.Count} commands sent)", false);
            });
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (spStation.IsOpen) spStation.Close();
                if (spVehicle.IsOpen) spVehicle.Close();
                spStation.Dispose();
                spVehicle.Dispose();
            }
            catch { }

            base.OnClosing(e);
        }

        private void Vehicle_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            MenuParseVehicle.IsEnabled = lvVehicleMessages.SelectedItem != null;
            MenuSaveVehicle.IsEnabled = lvVehicleMessages.Items.Count > 0;
            MenuClearVehicle.IsEnabled = lvVehicleMessages.Items.Count > 0;
        }

        private void Station_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            MenuParseStation.IsEnabled = lvStationMessages.SelectedItem != null;
            MenuSaveStation.IsEnabled = lvStationMessages.Items.Count > 0;
            MenuClearStation.IsEnabled = lvStationMessages.Items.Count > 0;
        }

        // ==================== MENU: SETTINGS ====================

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SettingsDialog
            {
                Owner = this
            };
            _ = dialog.ShowDialog();

            if (dialog.IsApplied)
            {
                // Riconfigura le porte seriali con i nuovi parametri
                ConfigureSerialPort(spStation);
                ConfigureSerialPort(spVehicle);

                // Se connessi, mostra avviso che serve riconnessione
                if (isStationConnected || isVehicleConnected)
                {
                    txtStatus.Text = "Settings applied. Disconnect and reconnect to apply new serial parameters.";
                    MessageBox.Show("Serial settings changed. Please disconnect and reconnect to apply the new parameters.",
                        "Settings Applied", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    PopulateSerialPorts();
                    txtStatus.Text = $"Settings applied - {dialog.BaudRate}, {dialog.DataBits}, {dialog.ParityValue.ToString()[0]}, {dialog.StopBitsValue}";
                }
            }
        }
        // ==================== MENU: EXIT ====================

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}