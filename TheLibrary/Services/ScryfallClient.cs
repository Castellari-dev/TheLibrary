using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TheLibrary.Models;

namespace TheLibrary.Services
{

    public static class ScryfallClient
    {
        private const string Base = "https://api.scryfall.com";
        private const int DelayMs = 110;

        private static readonly HttpClient Http = CreateClient();
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var c = new HttpClient(handler);
            c.Timeout = TimeSpan.FromSeconds(30);
            // O Scryfall exige User-Agent e Accept explícitos.
            c.DefaultRequestHeaders.Add("User-Agent", "TheLibrary/1.0 (Windows desktop; colecao pessoal)");
            c.DefaultRequestHeaders.Add("Accept", "application/json;q=0.9,*/*;q=0.8");
            return c;
        }

        private static async Task<string> GetRawAsync(string url, CancellationToken ct)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using (var resp = await Http.GetAsync(url, ct).ConfigureAwait(false))
                {
                    string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    await Task.Delay(DelayMs, ct).ConfigureAwait(false);

                    if (resp.StatusCode == HttpStatusCode.NotFound) return null;
                    if (!resp.IsSuccessStatusCode)
                    {
                        if ((int)resp.StatusCode == 429)
                            throw new InvalidOperationException("Scryfall retornou 429 (muitas requisições). Aguarde alguns segundos.");
                        return null;
                    }
                    return body;
                }
            }
            finally
            {
                Gate.Release();
            }
        }

        private static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<T>(json, JsonOpts); }
            catch { return null; }
        }

        public static async Task<List<ScryCard>> SearchAsync(string query, bool uniquePrints = true,
            bool includeMultilingual = false, int maxPages = 3, CancellationToken ct = default)
        {
            var result = new List<ScryCard>();
            if (string.IsNullOrWhiteSpace(query)) return result;

            string url = Base + "/cards/search?q=" + Uri.EscapeDataString(query)
                       + "&unique=" + (uniquePrints ? "prints" : "cards")
                       + "&order=released&dir=desc"
                       + (includeMultilingual ? "&include_multilingual=true" : "");

            int page = 0;
            while (!string.IsNullOrEmpty(url) && page < maxPages)
            {
                ct.ThrowIfCancellationRequested();
                var json = await GetRawAsync(url, ct).ConfigureAwait(false);
                var res = Parse<ScrySearchResult>(json);
                if (res == null || res.Data == null) break;

                result.AddRange(res.Data);
                url = res.HasMore ? res.NextPage : null;
                page++;
            }
            return result;
        }

        public static Task<List<ScryCard>> SearchExactNameAsync(string nameEn, bool includeMultilingual = false,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(nameEn)) return Task.FromResult(new List<ScryCard>());
            return SearchAsync("!\"" + nameEn.Replace("\"", "") + "\"", true, includeMultilingual, 3, ct);
        }

        public static Task<List<ScryCard>> GetPrintingsAsync(string oracleId, bool includeMultilingual = false,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(oracleId)) return Task.FromResult(new List<ScryCard>());
            return SearchAsync("oracleid:" + oracleId, true, includeMultilingual, 4, ct);
        }

        public static async Task<ScryCard> GetBySetNumberAsync(string setCode, string collectorNumber,
            string lang = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(setCode) || string.IsNullOrWhiteSpace(collectorNumber)) return null;

            string url = Base + "/cards/" + Uri.EscapeDataString(setCode.Trim().ToLowerInvariant())
                       + "/" + Uri.EscapeDataString(collectorNumber.Trim());
            if (!string.IsNullOrWhiteSpace(lang) && lang != "en")
                url += "/" + Uri.EscapeDataString(lang);

            var json = await GetRawAsync(url, ct).ConfigureAwait(false);
            return Parse<ScryCard>(json);
        }

        public static async Task<ScryCard> GetByFuzzyNameAsync(string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string url = Base + "/cards/named?fuzzy=" + Uri.EscapeDataString(name);
            var json = await GetRawAsync(url, ct).ConfigureAwait(false);
            return Parse<ScryCard>(json);
        }

        public static async Task<ScryCard> GetByIdAsync(string scryfallId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(scryfallId)) return null;
            var json = await GetRawAsync(Base + "/cards/" + Uri.EscapeDataString(scryfallId), ct).ConfigureAwait(false);
            return Parse<ScryCard>(json);
        }

        public static async Task<List<string>> AutocompleteAsync(string partial, CancellationToken ct = default)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(partial) || partial.Trim().Length < 2) return list;

            var json = await GetRawAsync(Base + "/cards/autocomplete?q=" + Uri.EscapeDataString(partial), ct)
                .ConfigureAwait(false);
            if (json == null) return list;

            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    JsonElement data;
                    if (doc.RootElement.TryGetProperty("data", out data))
                        foreach (var el in data.EnumerateArray())
                            list.Add(el.GetString());
                }
            }
            catch { }
            return list;
        }

        public static string MapLang(string csvLang)
        {
            if (string.IsNullOrWhiteSpace(csvLang)) return "en";
            switch (csvLang.Trim().ToUpperInvariant())
            {
                case "PT":
                case "BR":
                case "PTBR":
                case "PT-BR": return "pt";
                case "EN": return "en";
                case "DE": return "de";
                case "ES": return "es";
                case "FR": return "fr";
                case "IT": return "it";
                case "JP":
                case "JA": return "ja";
                case "KO": return "ko";
                case "RU": return "ru";
                case "TW":
                case "ZHT": return "zht";
                case "CN":
                case "ZHS": return "zhs";
                case "PH": return "ph";
                default: return csvLang.Trim().ToLowerInvariant();
            }
        }

        public static string LangToDisplay(string scryLang)
        {
            if (string.IsNullOrWhiteSpace(scryLang)) return "EN";
            switch (scryLang.ToLowerInvariant())
            {
                case "pt": return "PT";
                case "ja": return "JP";
                case "zht": return "TW";
                case "zhs": return "CN";
                default: return scryLang.ToUpperInvariant();
            }
        }
    }
}
