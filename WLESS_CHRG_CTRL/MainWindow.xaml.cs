using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

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

        private const int ReceiveMessageTimeoutMilliseconds = 150;
        private readonly StringBuilder stationReceiveBuffer = new();
        private readonly StringBuilder vehicleReceiveBuffer = new();
        private readonly DispatcherTimer stationReceiveTimeoutTimer;
        private readonly DispatcherTimer vehicleReceiveTimeoutTimer;

        private static readonly Brush DefaultTextColor = Brushes.Black;
        private static readonly Brush TxTextColor = Brushes.Green;
        private IntPtr deviceNotificationHandle = IntPtr.Zero;
        private HwndSource hwndSource;
        private readonly DispatcherTimer deviceChangeDebounceTimer;
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

        private static readonly List<string> HFCFieldNames =
        [
            "CLLLC_tripFlag.CLLLC_TripFlag_Enum",
            "CLLLC_hfcGanFaultActiveLow",
            "CLLLC_pwmFrequency_Hz",
            "CLLLC_pwmDutyPrimRef_pu",
            "CLLLC_pwmPhaseShiftPrimLegsRef_pu",
            "CLLLC_iTankModSensedRaw",
            "CLLLC_iTankModSensed_pu",
            "UNIPD_wptHfcActuatorFault",
            "UNIPD_wptHfcPhaseLast_pu",
            "UNIPD_wptHfcPhasePeak_pu",
            "UNIPD_wptHfcPhaseTicksLast",
            "UNIPD_wptHfcActiveCycles",
            "UNIPD_wptHfcFaultVdc_Volts",
            "UNIPD_wptHfcEpwm1TbphsRawLast",
            "UNIPD_wptHfcEpwm2TbphsRawLast",
            "UNIPD_wptHfcEpwm1TbprdLast",
            "UNIPD_wptHfcEpwm2TbprdLast",
            "UNIPD_wptHfcEpwm1TbctlLast",
            "UNIPD_wptHfcEpwm2TbctlLast",
            "UNIPD_wptHfcManualPhaseEnable",
            "UNIPD_wptHfcManualPhase_pu",
            "UNIPD_wptHfcRemoteRoleInvalidCycles"
        ];

        private static readonly List<string> CAPFieldNames =
        [
            "armed",
            "frozen",
            "count",
            "length",
            "decimation",
            "trigger_reason"
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

            deviceChangeDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            deviceChangeDebounceTimer.Tick += DeviceChangeDebounceTimer_Tick;

            stationReceiveTimeoutTimer = CreateReceiveTimeoutTimer(
                stationReceiveBuffer, stationMessages);
            vehicleReceiveTimeoutTimer = CreateReceiveTimeoutTimer(
                vehicleReceiveBuffer, vehicleMessages);

            // Il HWND non esiste ancora finché la finestra non è stata mostrata/renderizzata,
            // quindi agganciamo la registrazione all'evento SourceInitialized.
            SourceInitialized += MainWindow_SourceInitialized;
        }

        /// <summary>
        /// Wrapper per le API Win32 di device notification (RegisterDeviceNotification),
        /// usate per intercettare hot-plug/unplug di adattatori seriali USB senza WMI.
        /// </summary>
        internal static class DeviceNotificationNative
        {
            public const int WM_DEVICECHANGE = 0x0219;
            public const int DBT_DEVICEARRIVAL = 0x8000;
            public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

            private const int DBT_DEVTYP_DEVICEINTERFACE = 5;
            private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;

            // GUID_DEVINTERFACE_COMPORT — identifica le interfacce di device "porta seriale"
            // (sia fisiche RS232 che virtuali esposte da adattatori USB-seriale/FTDI/CH340/CP210x)
            private static readonly Guid GUID_DEVINTERFACE_COMPORT =
                new("86E0D1E0-8089-11D0-9CE4-08003E301F73");

            [StructLayout(LayoutKind.Sequential)]
            private struct DEV_BROADCAST_DEVICEINTERFACE
            {
                public int dbcc_size;
                public int dbcc_devicetype;
                public int dbcc_reserved;
                public Guid dbcc_classguid;
                // dbcc_name: char array a lunghezza variabile, non ci serve leggerlo
            }

            [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            private static extern IntPtr RegisterDeviceNotification(
                IntPtr hRecipient, IntPtr notificationFilter, int flags);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool UnregisterDeviceNotification(IntPtr handle);

            /// <summary>
            /// Registra la finestra (via hwnd) per ricevere notifiche WM_DEVICECHANGE
            /// filtrate sull'interfaccia COM port. Ritorna l'handle di registrazione,
            /// da passare a Unregister quando la finestra viene chiusa.
            /// </summary>
            public static IntPtr Register(IntPtr windowHandle)
            {
                var dbi = new DEV_BROADCAST_DEVICEINTERFACE
                {
                    dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE,
                    dbcc_reserved = 0,
                    dbcc_classguid = GUID_DEVINTERFACE_COMPORT
                };
                dbi.dbcc_size = Marshal.SizeOf(dbi);

                IntPtr buffer = Marshal.AllocHGlobal(dbi.dbcc_size);
                try
                {
                    Marshal.StructureToPtr(dbi, buffer, false);
                    return RegisterDeviceNotification(windowHandle, buffer, DEVICE_NOTIFY_WINDOW_HANDLE);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            public static void Unregister(IntPtr notificationHandle)
            {
                if (notificationHandle != IntPtr.Zero)
                    UnregisterDeviceNotification(notificationHandle);
            }
        }

        /// <summary>
        /// Popola le ComboBox con l'elenco delle porte seriali disponibili,
        /// escludendo quelle presenti in BannedPorts (separate da ';').
        /// </summary>
        private void PopulateSerialPorts()
        {
            string[] allPorts = SerialPort.GetPortNames();

            var bannedPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string bannedSetting = Properties.Settings.Default.BannedPorts;

            if (!string.IsNullOrWhiteSpace(bannedSetting))
            {
                var split = bannedSetting.Split([';'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in split)
                    bannedPorts.Add(p.Trim());
            }

            var availablePorts = allPorts
                .Where(p => !bannedPorts.Contains(p.Trim()))
                .ToList();

            var previousStation = cbStationPort.SelectedItem?.ToString();
            var previousVehicle = cbVehiclePort.SelectedItem?.ToString();

            cbStationPort.Items.Clear();
            cbVehiclePort.Items.Clear();

            foreach (string port in availablePorts)
            {
                cbStationPort.Items.Add(port);
                cbVehiclePort.Items.Add(port);
            }

            if (previousStation != null && availablePorts.Contains(previousStation))
                cbStationPort.SelectedItem = previousStation;
            else if (cbStationPort.Items.Count > 0)
                cbStationPort.SelectedIndex = 0;

            if (previousVehicle != null && availablePorts.Contains(previousVehicle))
                cbVehiclePort.SelectedItem = previousVehicle;
            else if (cbVehiclePort.Items.Count > 0)
                cbVehiclePort.SelectedIndex = Math.Min(1, cbVehiclePort.Items.Count - 1);

            int filteredCount = allPorts.Length - availablePorts.Count;
            txtStatus.Text = filteredCount > 0
                ? $"Found {availablePorts.Count} ports ({filteredCount} filtered by BannedPorts)"
                : $"Found {availablePorts.Count} serial ports";
        }

        /// <summary>
        /// Configura un SerialPort con i parametri dalle impostazioni utente
        /// </summary>
        private static void ConfigureSerialPort(SerialPort sp)
        {
            var settings = Properties.Settings.Default;

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

        private static void ScrollToLastItem(ListView listView)
        {
            if (listView.Items.Count > 0)
                listView.ScrollIntoView(listView.Items[^1]);
        }

        private DispatcherTimer CreateReceiveTimeoutTimer(
            StringBuilder receiveBuffer,
            ObservableCollection<SerialMessage> collection)
        {
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ReceiveMessageTimeoutMilliseconds)
            };
            timer.Tick += (_, _) => FlushReceiveBuffer(receiveBuffer, timer, collection);
            return timer;
        }

        private void ProcessReceivedData(
            StringBuilder receiveBuffer,
            DispatcherTimer timeoutTimer,
            ObservableCollection<SerialMessage> collection,
            string data)
        {
            timeoutTimer.Stop();

            foreach (char character in data)
            {
                if (character == '\r' || character == '\n')
                {
                    if (receiveBuffer.Length > 0)
                    {
                        AppendMessage(collection, receiveBuffer.ToString(), false);
                        receiveBuffer.Clear();
                    }
                }
                else
                {
                    receiveBuffer.Append(character);
                }
            }

            if (receiveBuffer.Length > 0)
                timeoutTimer.Start();
        }

        private void FlushReceiveBuffer(
            StringBuilder receiveBuffer,
            DispatcherTimer timeoutTimer,
            ObservableCollection<SerialMessage> collection)
        {
            timeoutTimer.Stop();

            if (receiveBuffer.Length == 0)
                return;

            AppendMessage(collection, receiveBuffer.ToString(), false);
            receiveBuffer.Clear();
        }

        private static void ResetReceiveBuffer(
            StringBuilder receiveBuffer,
            DispatcherTimer timeoutTimer)
        {
            timeoutTimer.Stop();
            receiveBuffer.Clear();
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

        private void SendCommand(SerialPort port, TextBox textBox, ObservableCollection<SerialMessage> collection)
        {
            string command = textBox.Text.Trim();

            if (string.IsNullOrEmpty(command))
                return;

            if (!port.IsOpen)
            {
                AppendMessage(collection, "[ERROR] Port not open!", false);
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

        private void SaveMessagesToFile(ObservableCollection<SerialMessage> collection, string portName)
        {
            if (collection.Count == 0)
            {
                MessageBox.Show($"No messages to save for {portName}.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = $"WLESS_{portName}_Messages_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Title = $"Save messages {portName}"
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
                    txtStatus.Text = $"Saved {collection.Count} messages in {System.IO.Path.GetFileName(dialog.FileName)}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving messages:\n{ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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

            // Verifica che la riga selezionata inizi con uno dei messaggi risposta validi (UQ, CAPS, HFC..)
            if (selectedMessage.Message.Trim().StartsWith(Properties.Resources.UQ_REPLY_HEADER))
                ParseUQResponse(collection, sourceName, startIndex);
            else if (selectedMessage.Message.Trim().StartsWith(Properties.Resources.HFC_REPLY_HEADER))
                ParseHFCResponse(collection, sourceName, startIndex);
            else if (selectedMessage.Message.Trim().StartsWith(Properties.Resources.CAPS_REPLY_HEADER))
                ParseCAPResponse(collection, sourceName, startIndex);
            else
            {
                MessageBox.Show("The selected row does not start with any of the expected headers.\n" +
                    "Please select the first row of a valid message.",
                    "Parse Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

        }

        /// <summary>
        /// Parsa la risposta UQ? a partire dalla riga selezionata dall'utente.
        /// Raccoglie la riga selezionata e tutte le successive contigue fino
        /// a un messaggio di sistema o un altro comando.
        /// </summary>
        private void ParseUQResponse(ObservableCollection<SerialMessage> collection,
            string sourceName, int startIndex)
        {
            var selectedMessage = collection[startIndex];

            // Ricostruisce la risposta dalla riga selezionata in poi
            var uqFragments = new List<string>();

            // Primo frammento: rimuovi "UQ,"
            string firstFragment = selectedMessage.Message.Trim()[3..];
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
                MessageBox.Show($"Parsing error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Parsa la risposta HFC a partire dalla riga selezionata dall'utente.
        /// Raccoglie la riga selezionata e tutte le successive contigue fino
        /// a un messaggio di sistema o un altro comando, poi normalizza i campi
        /// nella forma "CHIAVE=valore" tenendo solo il valore (es. "F=0" -> "0").
        /// </summary>
        private void ParseHFCResponse(ObservableCollection<SerialMessage> collection, string sourceName, int startIndex)
        {
            var selectedMessage = collection[startIndex];

            // Ricostruisce la risposta dalla riga selezionata in poi
            var hfcFragments = new List<string>();

            // Primo frammento: rimuovi "HFC,"
            string firstFragment = selectedMessage.Message.Trim()[4..];
            hfcFragments.Add(firstFragment);

            // Frammenti successivi: raccogli finché non trovi un messaggio di sistema o altro comando
            for (int i = startIndex + 1; i < collection.Count; i++)
            {
                string msg = collection[i].Message.Trim();

                // Interrompi se trovi un comando inviato, un errore di sistema, o un'altra risposta H,
                if (msg.StartsWith("[TX]") ||
                    msg.StartsWith("[SYSTEM]") ||
                    msg.StartsWith("[ERROR]") ||
                    msg.StartsWith("H,"))
                {
                    break;
                }

                hfcFragments.Add(msg);
            }

            try
            {
                // Concatena tutti i frammenti
                var sb = new StringBuilder();
                for (int i = 0; i < hfcFragments.Count; i++)
                {
                    string fragment = hfcFragments[i];

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

                // Trim, rimuovi l'eventuale "ID=" (es. "F=0" -> "0", "LAST_mpu=450" -> "450") e filtra i vuoti
                var values = new List<string>();
                foreach (var v in rawValues)
                {
                    string trimmed = v.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    int eqIndex = trimmed.IndexOf('=');
                    if (eqIndex >= 0)
                        trimmed = trimmed[(eqIndex + 1)..].Trim();

                    if (!string.IsNullOrWhiteSpace(trimmed))
                        values.Add(trimmed);
                }

                // Apri dialog
                var dialog = new StatusDialog(sourceName, HFCFieldNames, values)
                {
                    Owner = this
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Parsing error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ParseCAPResponse(ObservableCollection<SerialMessage> collection, string sourceName, int startIndex)
        {
            var selectedMessage = collection[startIndex];

            // Ricostruisce la risposta dalla riga selezionata in poi
            var uqFragments = new List<string>();

            // Primo frammento: rimuovi "CAP,"
            string firstFragment = selectedMessage.Message.Trim()[4..];
            uqFragments.Add(firstFragment);

            // Frammenti successivi: raccogli finché non trovi un messaggio di sistema o altro comando
            for (int i = startIndex + 1; i < collection.Count; i++)
            {
                string msg = collection[i].Message.Trim();

                // Interrompi se trovi un comando inviato, un errore di sistema, o un'altra risposta U,
                if (msg.StartsWith("[TX]") ||
                    msg.StartsWith("[SYSTEM]") ||
                    msg.StartsWith("[ERROR]") ||
                    msg.StartsWith("C,"))
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
                var dialog = new StatusDialog(sourceName, CAPFieldNames, values)
                {
                    Owner = this
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Parsing error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                    AppendMessage(messageCollection, "[ERROR] Port not open — sequence canceled.", false);
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
                        AppendMessage(messageCollection, "[ERROR] Connection lost during sequence.", false);
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


        /// <summary>
        /// Intercetta i messaggi Win32 della finestra. Ci interessa solo WM_DEVICECHANGE
        /// con subtype arrivo/rimozione device, per rilevare l'hot-plug di adattatori
        /// seriali USB senza dover fare polling o usare WMI.
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == DeviceNotificationNative.WM_DEVICECHANGE)
            {
                int eventType = wParam.ToInt32();

                if (eventType == DeviceNotificationNative.DBT_DEVICEARRIVAL ||
                    eventType == DeviceNotificationNative.DBT_DEVICEREMOVECOMPLETE)
                {
                    // Riavvia il debounce: eventi multipli ravvicinati sono comuni
                    // durante l'enumerazione di device composite.
                    deviceChangeDebounceTimer.Stop();
                    deviceChangeDebounceTimer.Start();
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Verifica se una delle porte attualmente connesse (Station/Vehicle) è
        /// sparita dall'elenco delle porte di sistema (device fisicamente rimosso)
        /// e, in tal caso, esegue il cleanup della connessione.
        /// </summary>
        private void CheckForDisconnectedActivePorts()
        {
            var currentPorts = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);

            if (isStationConnected && !currentPorts.Contains(spStation.PortName))
            {
                HandlePortDisconnected(spStation, ref isStationConnected, btnStationConnect,
                    ledStation, cbStationPort, stationMessages, "Station");
            }

            if (isVehicleConnected && !currentPorts.Contains(spVehicle.PortName))
            {
                HandlePortDisconnected(spVehicle, ref isVehicleConnected, btnVehicleConnect,
                    ledVehicle, cbVehiclePort, vehicleMessages, "Vehicle");
            }
        }

        /// <summary>
        /// Esegue il cleanup di una connessione seriale quando il device fisico
        /// è stato rimosso mentre era in uso: chiude la SerialPort, ripristina
        /// lo stato UI (LED, bottone, combo) e notifica l'evento all'utente.
        /// </summary>
        private void HandlePortDisconnected(SerialPort port, ref bool isConnectedFlag, Button connectButton,
            Ellipse led, ComboBox portComboBox, ObservableCollection<SerialMessage> collection, string roleName)
        {
            string portName = port.PortName;

            try
            {
                // A questo punto il device non esiste più fisicamente: Close() può
                // comunque fallire (es. I/O pendente), ma va tentato per rilasciare
                // le risorse gestite dal SerialPort.
                if (port.IsOpen) port.Close();
            }
            catch (Exception ex)
            {
                AppendMessage(collection, $"[ERROR] Error while closing {roleName} port after disconnect: {ex.Message}", false);
            }

            isConnectedFlag = false;
            connectButton.Content = "CONNECT";
            led.Fill = Brushes.Gray;
            portComboBox.IsEnabled = true;

            AppendMessage(collection, $"[SYSTEM] {roleName} port {portName} disconnected (device removed)", false);

            txtStatus.Text = $"{roleName} port {portName} was disconnected — device removed";
        }

        private static bool IsValidScriptDrop(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return false;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            return files.Length == 1 &&
                   Path.GetExtension(files[0]).Equals(Properties.Resources.ScriptFileExtension, StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetDroppedScriptFile(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return null;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            return files.FirstOrDefault(f =>
                Path.GetExtension(f).Equals(Properties.Resources.ScriptFileExtension, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Apre il dialog multi-linea e, se confermato, invia i comandi in sequenza con ritardo.
        /// Se filePath è specificato, precarica le righe da script invece del contenuto
        /// della TextBox associata.
        /// </summary>
        private void OpenCmdLinesDialog(SerialPort port, TextBox associatedTextBox,
            ObservableCollection<SerialMessage> messageCollection, string? filePath = null)
        {
            var dialog = filePath != null
                ? new CmdLinesDialog(filePath) { Owner = this }
                : new CmdLinesDialog { Owner = this };

            // Pre-popola con il contenuto attuale della TextBox (solo se non stiamo caricando da file)
            if (filePath == null && !string.IsNullOrWhiteSpace(associatedTextBox.Text))
            {
                dialog.txtCmdLines.Text = associatedTextBox.Text;
            }

            if (dialog.ShowDialog() == true)
            {
                _ = SendCommandsSequenceAsync(port, dialog.CmdLines, dialog.CmdDelay,
                    messageCollection);
            }
        }

        // ==================== EVENT HANDLERS ====================

        private void SpStation_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = spStation.ReadExisting();
                _ = uiDispatcher.BeginInvoke(() => ProcessReceivedData(
                    stationReceiveBuffer, stationReceiveTimeoutTimer, stationMessages, data));
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
                _ = uiDispatcher.BeginInvoke(() => ProcessReceivedData(
                    vehicleReceiveBuffer, vehicleReceiveTimeoutTimer, vehicleMessages, data));
            }
            catch (Exception ex)
            {
                AppendMessage(vehicleMessages, $"[ERROR] {ex.Message}", false);
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
                    ResetReceiveBuffer(stationReceiveBuffer, stationReceiveTimeoutTimer);
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
                FlushReceiveBuffer(stationReceiveBuffer, stationReceiveTimeoutTimer, stationMessages);
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
                    ResetReceiveBuffer(vehicleReceiveBuffer, vehicleReceiveTimeoutTimer);
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
                FlushReceiveBuffer(vehicleReceiveBuffer, vehicleReceiveTimeoutTimer, vehicleMessages);
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

        private void LvMessages_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (sender is not ListView listView || listView.SelectedItems.Count == 0)
                    return;

                var sb = new StringBuilder();

                foreach (SerialMessage msg in listView.SelectedItems.Cast<SerialMessage>())
                {
                    sb.AppendLine($"{msg.Message}");
                }

                Clipboard.SetText(sb.ToString());
                txtStatus.Text = $"Copied {listView.SelectedItems.Count} messages to clipboard";
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

        private void DeviceChangeDebounceTimer_Tick(object sender, EventArgs e)
        {
            deviceChangeDebounceTimer.Stop();
            PopulateSerialPorts();
            CheckForDisconnectedActivePorts();
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            hwndSource = (HwndSource)PresentationSource.FromVisual(this);
            
            if (hwndSource == null)
                return;

            hwndSource.AddHook(WndProc);
            deviceNotificationHandle = DeviceNotificationNative.Register(hwndSource.Handle);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                deviceChangeDebounceTimer.Stop();

                if (deviceNotificationHandle != IntPtr.Zero)
                {
                    DeviceNotificationNative.Unregister(deviceNotificationHandle);
                    deviceNotificationHandle = IntPtr.Zero;
                }
                hwndSource?.RemoveHook(WndProc);

                if (spStation.IsOpen) spStation.Close();
                if (spVehicle.IsOpen) spVehicle.Close();
                spStation.Dispose();
                spVehicle.Dispose();
            }
            catch { }

            base.OnClosing(e);
        }

        // ==================== CONTEXT MENU ====================
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

        private void MenuParseStation_Click(object sender, RoutedEventArgs e)
        {
            ParseResponse(stationMessages, lvStationMessages, "Station");
        }

        private void MenuParseVehicle_Click(object sender, RoutedEventArgs e)
        {
            ParseResponse(vehicleMessages, lvVehicleMessages, "Vehicle");
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

        // ==================== MULTILINE DIALOG OPENING ====================

        private void TxtStationCommand_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenCmdLinesDialog(spStation, txtStationCommand, stationMessages);
        }

        private void TxtVehicleCommand_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenCmdLinesDialog(spVehicle, txtVehicleCommand, vehicleMessages);
        }

        // ==================== MAIN MENU ====================

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

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }


        // ==================== LOAD SCRIPT ====================

        private void MenuLoadStationScript_Click(object sender, RoutedEventArgs e)
        {
            LoadScriptFromDialog(spStation, txtStationCommand, stationMessages);
        }

        private void MenuLoadVehicleScript_Click(object sender, RoutedEventArgs e)
        {
            LoadScriptFromDialog(spVehicle, txtVehicleCommand, vehicleMessages);
        }

        private void LoadScriptFromDialog(SerialPort port, TextBox associatedTextBox,
            ObservableCollection<SerialMessage> messageCollection)
        {
            var openDialog = new OpenFileDialog
            {
                Filter = $"WLESS Command Script (*{Properties.Resources.ScriptFileExtension})|*{Properties.Resources.ScriptFileExtension}|All files (*.*)|*.*",
                Title = "Load Command Script"
            };

            if (openDialog.ShowDialog() == true)
            {
                OpenCmdLinesDialog(port, associatedTextBox, messageCollection, openDialog.FileName);
            }
        }

        // ==================== DRAG & DROP SCRIPT (.wcx) ====================

        private void StationCommand_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = IsValidScriptDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void StationCommand_Drop(object sender, DragEventArgs e)
        {
            string? filePath = GetDroppedScriptFile(e);
            if (filePath == null)
            {
                MessageBox.Show($"Only {Properties.Resources.ScriptFileExtension} script files are supported.",
                    "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OpenCmdLinesDialog(spStation, txtStationCommand, stationMessages, filePath);
        }

        private void VehicleCommand_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = IsValidScriptDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void VehicleCommand_Drop(object sender, DragEventArgs e)
        {
            string? filePath = GetDroppedScriptFile(e);
            if (filePath == null)
            {
                MessageBox.Show($"Only {Properties.Resources.ScriptFileExtension} script files are supported.",
                    "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OpenCmdLinesDialog(spVehicle, txtVehicleCommand, vehicleMessages, filePath);
        }
    }
}
