using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TheLibrary.Models;

namespace TheLibrary.Services
{
    public class PriceLookup
    {
        public decimal? Price { get; set; }
        public string Source { get; set; } = "";
        public ScryCard From { get; set; }

        public bool Found => Price.HasValue && Price.Value > 0m;
    }

    public static class PriceResolver
    {
        private static readonly Dictionary<string, ScryCard> EnglishCache =
            new Dictionary<string, ScryCard>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, List<ScryCard>> PrintingsCache =
            new Dictionary<string, List<ScryCard>>(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache()
        {
            EnglishCache.Clear();
            PrintingsCache.Clear();
        }

        public static async Task<PriceLookup> ResolveAsync(ScryCard card, bool foil, CancellationToken ct = default)
        {
            if (card == null) return new PriceLookup { Source = "—" };

            var own = RawPrice(card, foil) ?? RawPrice(card, !foil);
            if (own.HasValue && own.Value > 0m)
                return new PriceLookup { Price = own, Source = "impressão", From = card };

            if (!IsEnglish(card.Lang)
                && !string.IsNullOrWhiteSpace(card.Set)
                && !string.IsNullOrWhiteSpace(card.CollectorNumber))
            {
                var en = await GetEnglishAsync(card.Set, card.CollectorNumber, ct).ConfigureAwait(false);
                if (en != null)
                {
                    var p = RawPrice(en, foil) ?? RawPrice(en, !foil);
                    if (p.HasValue && p.Value > 0m)
                        return new PriceLookup { Price = p, Source = "versão EN", From = en };
                }
            }

            var cheapest = await GetCheapestAsync(card, foil, ct).ConfigureAwait(false);
            if (cheapest != null) return cheapest;

            return new PriceLookup { Source = "sem cotação" };
        }

        public static async Task<decimal?> ResolvePriceAsync(ScryCard card, bool foil, CancellationToken ct = default)
        {
            var r = await ResolveAsync(card, foil, ct).ConfigureAwait(false);
            return r.Price;
        }

        private static async Task<PriceLookup> GetCheapestAsync(ScryCard card, bool foil, CancellationToken ct)
        {
            var prints = await GetPrintingsAsync(card, ct).ConfigureAwait(false);
            if (prints == null || prints.Count == 0) return null;

            bool[] order = foil ? new[] { true, false } : new[] { false, true };

            foreach (bool wantFoil in order)
            {
                PriceLookup best = null;
                foreach (var p in prints)
                {
                    if (p == null || p.Digital) continue;

                    var v = RawPrice(p, wantFoil);
                    if (!v.HasValue || v.Value <= 0m) continue;

                    if (best == null || v.Value < best.Price.Value)
                    {
                        best = new PriceLookup
                        {
                            Price = v,
                            From = p,
                            Source = "menor · " + (p.Set ?? "").ToUpperInvariant() + (wantFoil ? " foil" : "")
                        };
                    }
                }
                if (best != null) return best;
            }
            return null;
        }

        private static async Task<ScryCard> GetEnglishAsync(string set, string collectorNumber, CancellationToken ct)
        {
            string key = set + "/" + collectorNumber;

            ScryCard cached;
            if (EnglishCache.TryGetValue(key, out cached)) return cached;

            var en = await ScryfallClient.GetBySetNumberAsync(set, collectorNumber, "en", ct).ConfigureAwait(false);
            EnglishCache[key] = en;
            return en;
        }

        private static async Task<List<ScryCard>> GetPrintingsAsync(ScryCard card, CancellationToken ct)
        {
            bool hasOracle = !string.IsNullOrWhiteSpace(card.OracleId);
            string key = hasOracle ? "o:" + card.OracleId : "n:" + (card.Name ?? "");

            List<ScryCard> cached;
            if (PrintingsCache.TryGetValue(key, out cached)) return cached;

            List<ScryCard> prints;
            if (hasOracle)
                prints = await ScryfallClient.GetPrintingsAsync(card.OracleId, false, ct).ConfigureAwait(false);
            else if (!string.IsNullOrWhiteSpace(card.Name))
                prints = await ScryfallClient.SearchExactNameAsync(card.Name, false, ct).ConfigureAwait(false);
            else
                prints = new List<ScryCard>();

            PrintingsCache[key] = prints;
            return prints;
        }
        
        private static decimal? RawPrice(ScryCard c, bool foil)
        {
            if (c == null || c.Prices == null) return null;

            string raw = foil ? (c.Prices.UsdFoil ?? c.Prices.UsdEtched) : c.Prices.Usd;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            decimal v;
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return null;
        }

        private static bool IsEnglish(string lang) =>
            string.IsNullOrWhiteSpace(lang) || string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);
    }
}
