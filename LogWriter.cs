using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AutoEngineerBrakeFix
{
    /// <summary>Builds one CSV row; the header is taken from the column names of the first row written.</summary>
    internal sealed class RowBuilder
    {
        public readonly List<string> Names = new List<string>();
        public readonly List<string> Values = new List<string>();

        public RowBuilder Add(string name, float value)
        {
            Names.Add(name);
            Values.Add(float.IsNaN(value) || float.IsInfinity(value)
                ? ""
                : value.ToString("0.####", CultureInfo.InvariantCulture));
            return this;
        }

        public RowBuilder Add(string name, int value)
        {
            Names.Add(name);
            Values.Add(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public RowBuilder Add(string name, string value)
        {
            Names.Add(name);
            value = value ?? "";
            if (value.IndexOfAny(new[] { ',', '"', '\n' }) >= 0)
            {
                value = "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            Values.Add(value);
            return this;
        }
    }

    internal static class LogWriter
    {
        private static readonly object Lock = new object();
        private static StreamWriter _writer;

        internal static string LogDir => Path.Combine(Main.Mod.Path, "logs");

        internal static void Write(RowBuilder row)
        {
            lock (Lock)
            {
                if (_writer == null)
                {
                    Directory.CreateDirectory(LogDir);
                    string path = Path.Combine(LogDir, $"ae_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    _writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
                    _writer.WriteLine(string.Join(",", row.Names));
                    Main.Mod.Logger.Log("Logging to " + path);
                }
                _writer.WriteLine(string.Join(",", row.Values));
            }
        }

        internal static void WriteTextFile(string prefix, string text)
        {
            Directory.CreateDirectory(LogDir);
            string path = Path.Combine(LogDir, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, text, new UTF8Encoding(false));
            Main.Mod.Logger.Log("Wrote " + path);
        }

        internal static void Close()
        {
            lock (Lock)
            {
                if (_writer != null)
                {
                    _writer.Dispose();
                    _writer = null;
                }
            }
        }
    }
}
