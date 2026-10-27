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

        // Rows that could not be written (another program briefly locked the file, e.g. OneDrive or a viewer) wait
        // here and go out ahead of the next row, so a short lock loses nothing. Capped so a permanent lock cannot
        // grow without bound.
        private const int MaxPending = 6000;
        private static readonly List<string> Pending = new List<string>();
        private static bool _warned;

        internal static string LogDir => Path.Combine(Main.Mod.Path, "logs");

        internal static void Write(RowBuilder row)
        {
            lock (Lock)
            {
                if (_writer == null)
                {
                    Directory.CreateDirectory(LogDir);
                    string path = Path.Combine(LogDir, $"ae_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                    _writer.WriteLine(string.Join(",", row.Names));
                    Main.Mod.Logger.Log("Logging to " + path);
                }

                Pending.Add(string.Join(",", row.Values));
                try
                {
                    while (Pending.Count > 0)
                    {
                        _writer.WriteLine(Pending[0]);
                        Pending.RemoveAt(0);
                    }
                    _warned = false;
                }
                catch (IOException e)
                {
                    // Keep the rows and try again on the next sample.
                    if (Pending.Count > MaxPending) Pending.RemoveRange(0, Pending.Count - MaxPending);
                    if (!_warned)
                    {
                        _warned = true;
                        Main.Mod.Logger.Warning("Log file is locked by another program; rows are held and written when it is free: " + e.Message);
                    }
                }
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
                    try
                    {
                        foreach (string line in Pending) _writer.WriteLine(line);
                    }
                    catch (IOException)
                    {
                    }
                    Pending.Clear();
                    try { _writer.Dispose(); } catch (IOException) { }
                    _writer = null;
                }
            }
        }
    }
}
