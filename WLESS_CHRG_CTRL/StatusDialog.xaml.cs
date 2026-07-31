using System.Collections.Generic;
using System.Windows;

namespace WLESS_CHRG_CTRL
{
    /// <summary>
    /// Modello dati per una riga del ListView Field/Value
    /// </summary>
    public class StatusField
    {
        public string FieldName { get; set; } = string.Empty;
        public string FieldValue { get; set; } = string.Empty;
    }

    public partial class StatusDialog : Window
    {
        public StatusDialog(string sourceName, List<string> fieldNames, List<string> values)
        {
            InitializeComponent();
            Title = $"Parsed Status - {sourceName}";
            txtHeaderTitle.Text = $"{sourceName} Response";   // <-- riga opzionale
            PopulateListView(fieldNames, values);
        }
        /// <summary>
        /// Popola il ListView con coppie Field/Value
        /// </summary>
        private void PopulateListView(List<string> fieldNames, List<string> values)
        {
            var items = new List<StatusField>();

            for (int i = 0; i < fieldNames.Count; i++)
            {
                items.Add(new StatusField
                {
                    FieldName = fieldNames[i],
                    FieldValue = (i < values.Count) ? values[i] : "N/A"
                });
            }

            // Valori extra non mappati
            if (values.Count > fieldNames.Count)
            {
                for (int i = fieldNames.Count; i < values.Count; i++)
                {
                    items.Add(new StatusField
                    {
                        FieldName = $"[EXTRA {i - fieldNames.Count + 1}]",
                        FieldValue = values[i]
                    });
                }
            }

            lvValues.ItemsSource = items;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}