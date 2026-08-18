using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TheLibrary.Services
{
    public class CsvTable
    {
        public List<string> Headers { get; set; } = new List<string>();
        public List<string[]> Rows { get; set; } = new List<string[]>();
        public char Delimiter { get; set; } = ',';
        public string EncodingName { get; set; } = "UTF-8";

        public int IndexOf(string header)
        {
            for (int i = 0; i < Headers.Count; i++)
                if (string.Equals(Headers[i], header, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }

    public static class CsvParser
    {
        public static CsvTable Parse(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string encName;
            string text = DecodeText(bytes, out encName);
            var table = ParseText(text);
            table.EncodingName = encName;
            return table;
        }

        private static string DecodeText(byte[] bytes, out string encodingName)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {

                try
                {
                    var strictBom = new UTF8Encoding(false, true);
                    string s = strictBom.GetString(bytes, 3, bytes.Length - 3);
                    encodingName = "UTF-8 (BOM)";
                    return s;
                }
                catch (DecoderFallbackException)
                {
                    encodingName = "ISO-8859-1 (com BOM)";
                    return Encoding.Latin1.GetString(bytes, 3, bytes.Length - 3);
                }
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encodingName = "UTF-16 LE";
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            try
            {
                var strict = new UTF8Encoding(false, true);
                string s = strict.GetString(bytes);
                encodingName = "UTF-8";
                return s;
            }
            catch (DecoderFallbackException)
            {
                encodingName = "ISO-8859-1";
                return Encoding.Latin1.GetString(bytes);
            }
        }

        public static CsvTable ParseText(string text)
        {
            var table = new CsvTable();
            if (string.IsNullOrEmpty(text)) return table;

            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

            table.Delimiter = DetectDelimiter(text);

            var records = SplitRecords(text, table.Delimiter);
            if (records.Count == 0) return table;

            foreach (var h in records[0])
                table.Headers.Add((h ?? "").Trim());

            for (int i = 1; i < records.Count; i++)
            {
                var row = records[i];
                if (row.Length == 1 && string.IsNullOrWhiteSpace(row[0])) continue;

                if (row.Length < table.Headers.Count)
                {
                    var padded = new string[table.Headers.Count];
                    Array.Copy(row, padded, row.Length);
                    for (int k = row.Length; k < padded.Length; k++) padded[k] = "";
                    row = padded;
                }
                table.Rows.Add(row);
            }
            return table;
        }

        private static char DetectDelimiter(string text)
        {
            int end = text.IndexOf('\n');
            string first = end < 0 ? text : text.Substring(0, end);

            int comma = 0, semi = 0, tab = 0;
            bool q = false;
            foreach (char c in first)
            {
                if (c == '"') { q = !q; continue; }
                if (q) continue;
                if (c == ',') comma++;
                else if (c == ';') semi++;
                else if (c == '\t') tab++;
            }
            if (semi > comma && semi >= tab) return ';';
            if (tab > comma && tab > semi) return '\t';
            return ',';
        }

        private static List<string[]> SplitRecords(string text, char delimiter)
        {
            var records = new List<string[]>();
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                    continue;
                }

                if (c == '"') { inQuotes = true; continue; }

                if (c == delimiter)
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                if (c == '\n')
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                    records.Add(fields.ToArray());
                    fields.Clear();
                    continue;
                }

                sb.Append(c);
            }

            if (sb.Length > 0 || fields.Count > 0)
            {
                fields.Add(sb.ToString());
                records.Add(fields.ToArray());
            }
            return records;
        }
    }
}
