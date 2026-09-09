        /// <summary>
        /// Invia una sequenza di comandi con delay specificato tra uno e l'altro.
        /// Eseguito in background per non bloccare l'interfaccia utente.
        /// 
        /// Se un comando ha un delay per-riga (@numero), usa quello.
        /// Altrimenti usa il delay globale (delayMs).
        /// </summary>
        private async System.Threading.Tasks.Task SendCommandsSequenceAsync(
            SerialPort port,
            List<CommandWithDelay> commands,
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
                AppendMessage(messageCollection, $"[SYSTEM] Starting sequence of {commands.Count} commands (default delay: {delayMs}ms)", false);
            });

            for (int i = 0; i < commands.Count; i++)
            {
                CommandWithDelay cmdWithDelay = commands[i];
                string cmd = cmdWithDelay.Command.Trim();

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

                // Determina il delay da applicare (per-riga o globale)
                int effectiveDelay = cmdWithDelay.Delay > 0 ? cmdWithDelay.Delay : delayMs;

                // Applica il delay prima del prossimo comando (non dopo l'ultimo)
                if (i < commands.Count - 1 && effectiveDelay > 0)
                {
                    await System.Threading.Tasks.Task.Delay(effectiveDelay);
                }
            }

            uiDispatcher.Invoke(() =>
            {
                AppendMessage(messageCollection, $"[SYSTEM] Sequence completed ({commands.Count} commands sent)", false);
            });
        }
