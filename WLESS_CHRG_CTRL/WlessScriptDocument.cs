using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace WLESS_CHRG_CTRL
{
    internal sealed class WlessScriptDocument
    {
        public const string CurrentFormatVersion = "1.0";
        private const string SchemaResourceName = "WLESS_CHRG_CTRL.WlessScript.xsd";

        public string FormatVersion { get; set; } = CurrentFormatVersion;
        public int DefaultCommandDelayMilliseconds { get; set; }
        public string StationSerialPort { get; set; } = string.Empty;
        public string VehicleSerialPort { get; set; } = string.Empty;
        public List<string> StationLines { get; set; } = new List<string>();
        public List<string> VehicleLines { get; set; } = new List<string>();

        public static WlessScriptDocument Load(string filePath)
        {
            var errors = new List<string>();
            var schemas = LoadSchema();
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                ValidationType = ValidationType.Schema,
                Schemas = schemas
            };
            settings.ValidationEventHandler += (sender, args) => errors.Add(args.Message);

            XDocument xml;
            using (var stream = File.OpenRead(filePath))
            using (var reader = XmlReader.Create(stream, settings))
            {
                xml = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            }

            if (errors.Count > 0)
                throw new InvalidDataException("Invalid WLESS script:\n" + string.Join("\n", errors));

            XElement root = xml.Root ?? throw new InvalidDataException("Missing WLESS_CHRG_SCRIPT root element.");
            XElement station = root.Element("STATION") ?? throw new InvalidDataException("Missing STATION section.");
            XElement vehicle = root.Element("VEHICLE") ?? throw new InvalidDataException("Missing VEHICLE section.");

            string defaultDelayText = (string)root.Attribute("defaultCommandDelayMs") ?? string.Empty;
            if (!int.TryParse(defaultDelayText, out int defaultDelay) || defaultDelay < 0)
                throw new InvalidDataException("defaultCommandDelayMs must be an integer between 0 and 2147483647.");

            return new WlessScriptDocument
            {
                FormatVersion = (string)root.Attribute("formatVersion") ?? string.Empty,
                DefaultCommandDelayMilliseconds = defaultDelay,
                StationSerialPort = ((string)station.Attribute("serialPort") ?? string.Empty).Trim(),
                VehicleSerialPort = ((string)vehicle.Attribute("serialPort") ?? string.Empty).Trim(),
                StationLines = ReadSectionLines(station.Value),
                VehicleLines = ReadSectionLines(vehicle.Value)
            };
        }

        public void Save(string filePath)
        {
            var document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("WLESS_CHRG_SCRIPT",
                    new XAttribute("formatVersion", CurrentFormatVersion),
                    new XAttribute("defaultCommandDelayMs", DefaultCommandDelayMilliseconds),
                    new XElement("STATION",
                        new XAttribute("serialPort", StationSerialPort),
                        string.Join("\n", StationLines)),
                    new XElement("VEHICLE",
                        new XAttribute("serialPort", VehicleSerialPort),
                        string.Join("\n", VehicleLines))));

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace
            };

            using (var writer = XmlWriter.Create(filePath, settings))
                document.Save(writer);
        }

        private static XmlSchemaSet LoadSchema()
        {
            Stream schemaStream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(SchemaResourceName);
            if (schemaStream == null)
                throw new InvalidOperationException("Embedded WLESS script schema not found.");

            using (schemaStream)
            using (var reader = XmlReader.Create(schemaStream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            }))
            {
                var schemas = new XmlSchemaSet { XmlResolver = null };
                schemas.Add(null, reader);
                schemas.Compile();
                return schemas;
            }
        }

        private static List<string> ReadSectionLines(string text)
        {
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = new List<string>(normalized.Split(new[] { '\n' }, StringSplitOptions.None));

            // Allow the customary formatting newline immediately inside each XML tag
            // without turning it into an executable script row.
            if (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
                lines.RemoveAt(0);
            if (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                lines.RemoveAt(lines.Count - 1);

            return lines;
        }
    }
}
