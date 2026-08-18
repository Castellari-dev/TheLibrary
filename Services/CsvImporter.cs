using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TheLibrary.Models;

namespace TheLibrary.Services
{
    public enum ImportStatus
    {
        Pendente,
        Ok,
        Aproximado,
        NaoEncontrado,
        Erro
    }

    public class ImportRow : INotifyPropertyChanged
    {
        private ImportStatus _status = ImportStatus.Pendente;
        private string _statusDetail = "";
        private ScryCard _resolved;
        private int _quantity = 1;
        private decimal _minPriceUsd;
        private bool _include = true;
        private string _setCode;
        private string _collectorNumber;

        public int LineNumber { get; set; }

        public string SetNamePt { get; set; }
        public string SetNameEn { get; set; }
        public string SetCode { get => _setCode; set { _setCode = value; OnChanged(); } }
        public string NamePt { get; set; }
        public string NameEn { get; set; }
        public string CollectorNumber { get => _collectorNumber; set { _collectorNumber = value; OnChanged(); } }
        public string LangRaw { get; set; }
        public string LangCode { get; set; } = "en";
        public string Rarity { get; set; }
        public string Colors { get; set; }
        public string Condition { get; set; } = "NM";
        public bool IsFoil { get; set; }
        public string Notes { get; set; }

        public decimal? MarketPriceUsd { get; set; }

        public string PriceSource { get; set; } = "";

        public int Quantity
        {
            get => _quantity;
            set { _quantity = value; OnChanged(); }
        }

        public decimal MinPriceUsd
        {
            get => _minPriceUsd;
            set { _minPriceUsd = value; OnChanged(); }
        }

        public bool Include
        {
            get => _include;
            set { _include = value; OnChanged(); }
        }

        public ScryCard Resolved
        {
            get => _resolved;
            set
            {
                _resolved = value;
                OnChanged();
                OnChanged(nameof(ImageUrl));
                OnChanged(nameof(ResolvedSet));
                OnChanged(nameof(ArtLabel));
            }
        }

        public ImportStatus Status
        {
            get => _status;
            set { _status = value; OnChanged(); OnChanged(nameof(StatusText)); }
        }

        public string StatusDetail
        {
            get => _statusDetail;
            set { _statusDetail = value; OnChanged(); OnChanged(nameof(StatusText)); }
        }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case ImportStatus.Ok: return "OK";
                    case ImportStatus.Aproximado: return "Aproximado" + (string.IsNullOrEmpty(StatusDetail) ? "" : " · " + StatusDetail);
                    case ImportStatus.NaoEncontrado: return "Não encontrado";
                    case ImportStatus.Erro: return "Erro" + (string.IsNullOrEmpty(StatusDetail) ? "" : " · " + StatusDetail);
                    default: return "Pendente";
                }
            }
        }

        public string DisplayName => string.IsNullOrWhiteSpace(NamePt) ? NameEn : NamePt;
        public string ImageUrl => Resolved != null ? Resolved.SmallImage : null;
        public string ResolvedSet => Resolved != null ? Resolved.SetLine : "";
        public string ArtLabel => Resolved != null ? Resolved.EffectiveArtist : "";
        public string FoilLabel => IsFoil ? "Foil" : "";

        public CardEntry ToCardEntry()
        {
            var c = new CardEntry
            {
                Quantity = Quantity,
                MinPriceUsd = MinPriceUsd,
                Condition = Condition,
                IsFoil = IsFoil,
                Notes = Notes,
                NameEn = NameEn,
                NamePt = NamePt,
                SetCode = SetCode,
                SetName = SetNameEn,
                CollectorNumber = CollectorNumber,
                Lang = LangCode,
                Rarity = Rarity,
                Colors = Colors
            };

            if (Resolved != null) CardMapper.ApplyPrinting(c, Resolved, IsFoil, false);

            if (MarketPriceUsd.HasValue) c.MarketPriceUsd = MarketPriceUsd;
            c.MinPriceUsd = MinPriceUsd;

            return c;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public static class CardMapper
    {
        public static void ApplyPrinting(CardEntry c, ScryCard s, bool foil, bool overwriteMinPrice)
        {
            if (s == null) return;

            c.ScryfallId = s.Id;
            c.OracleId = s.OracleId;
            c.NameEn = s.Name;
            if (!string.IsNullOrWhiteSpace(s.PrintedName)) c.NamePt = s.PrintedName;
            c.SetCode = s.Set;
            c.SetName = s.SetName;
            c.CollectorNumber = s.CollectorNumber;
            c.Rarity = s.Rarity;
            c.TypeLine = s.EffectiveTypeLine;
            c.ManaCost = s.EffectiveManaCost;
            c.Colors = s.ColorIdentity != null ? string.Join("", s.ColorIdentity) : c.Colors;
            c.Lang = s.Lang;
            c.ImageUrl = s.NormalImage;
            c.ArtCropUrl = s.ArtCropImage;
            c.ScryfallUri = s.ScryfallUri;
            c.Artist = s.EffectiveArtist;

            var price = s.GetPriceUsd(foil);
            c.MarketPriceUsd = price;
            if (overwriteMinPrice || c.MinPriceUsd <= 0m)
                c.MinPriceUsd = price ?? 0m;
        }
    }

    public static class CsvImporter
    {
        private enum Field
        {
            None, SetNamePt, SetNameEn, SetCode, NamePt, NameEn, CollectorNumber,
            Quantity, Condition, Lang, Rarity, Colors, Extras, Notes, MinPrice
        }

        // Ordem importa: o primeiro prefixo que casar vence.
        private static readonly (string Prefix, Field Target)[] Aliases =
        {
            ("edicao (ptbr",     Field.SetNamePt),
            ("edicao (pt",       Field.SetNamePt),
            ("edicao (en",       Field.SetNameEn),
            ("edicao (sigla",    Field.SetCode),
            ("card (pt",         Field.NamePt),
            ("card (en",         Field.NameEn),
            ("card #",           Field.CollectorNumber),
            ("card number",      Field.CollectorNumber),
            ("collector",        Field.CollectorNumber),
            ("numero",           Field.CollectorNumber),
            ("quantidade",       Field.Quantity),
            ("quantity",         Field.Quantity),
            ("qtd",              Field.Quantity),
            ("qty",              Field.Quantity),
            ("count",            Field.Quantity),
            ("qualidade",        Field.Condition),
            ("condition",        Field.Condition),
            ("conservacao",      Field.Condition),
            ("idioma",           Field.Lang),
            ("language",         Field.Lang),
            ("lang",             Field.Lang),
            ("raridade",         Field.Rarity),
            ("rarity",           Field.Rarity),
            ("cor",              Field.Colors),
            ("color",            Field.Colors),
            ("extras",           Field.Extras),
            ("foil",             Field.Extras),
            ("finish",           Field.Extras),
            ("acabamento",       Field.Extras),
            ("comentario",       Field.Notes),
            ("comment",          Field.Notes),
            ("notes",            Field.Notes),
            ("observ",           Field.Notes),
            ("preco",            Field.MinPrice),
            ("price",            Field.MinPrice),
            ("valor",            Field.MinPrice),
            ("usd",              Field.MinPrice),
            ("sigla",            Field.SetCode),
            ("set code",         Field.SetCode),
            ("set",              Field.SetCode),
            ("edicao",           Field.SetNameEn),
            ("edition",          Field.SetNameEn),
            ("nome",             Field.NameEn),
            ("name",             Field.NameEn)
        };

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\uFEFF", "").Trim().Trim('"').Trim();

            var norm = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in norm)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat != UnicodeCategory.NonSpacingMark) sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        private static Field Match(string header)
        {
            string h = Normalize(header);
            if (h.Length == 0) return Field.None;
            foreach (var a in Aliases)
                if (h.StartsWith(a.Prefix, StringComparison.Ordinal)) return a.Target;
            return Field.None;
        }

        public static List<ImportRow> MapRows(CsvTable table, out List<string> unmappedHeaders)
        {
            unmappedHeaders = new List<string>();
            var map = new Dictionary<Field, int>();

            for (int i = 0; i < table.Headers.Count; i++)
            {
                var f = Match(table.Headers[i]);
                if (f == Field.None)
                {
                    if (!string.IsNullOrWhiteSpace(table.Headers[i])) unmappedHeaders.Add(table.Headers[i]);
                    continue;
                }
                if (!map.ContainsKey(f)) map[f] = i;
            }

            Func<string[], Field, string> get = (row, f) =>
            {
                int idx;
                if (!map.TryGetValue(f, out idx)) return "";
                if (idx >= row.Length) return "";
                return (row[idx] ?? "").Trim();
            };

            var list = new List<ImportRow>();
            for (int r = 0; r < table.Rows.Count; r++)
            {
                var row = table.Rows[r];

                var item = new ImportRow
                {
                    LineNumber = r + 2,
                    SetNamePt = get(row, Field.SetNamePt),
                    SetNameEn = get(row, Field.SetNameEn),
                    SetCode = get(row, Field.SetCode).ToLowerInvariant(),
                    NamePt = get(row, Field.NamePt),
                    NameEn = get(row, Field.NameEn),
                    CollectorNumber = get(row, Field.CollectorNumber),
                    LangRaw = get(row, Field.Lang),
                    Rarity = get(row, Field.Rarity),
                    Colors = get(row, Field.Colors),
                    Notes = get(row, Field.Notes)
                };

                if (string.IsNullOrWhiteSpace(item.SetNameEn)) item.SetNameEn = item.SetNamePt;

                int qty;
                item.Quantity = int.TryParse(get(row, Field.Quantity), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out qty) && qty > 0 ? qty : 1;

                string cond = get(row, Field.Condition).ToUpperInvariant();
                item.Condition = string.IsNullOrWhiteSpace(cond) ? "NM" : cond;

                string extras = Normalize(get(row, Field.Extras));
                item.IsFoil = extras.Contains("foil") || extras.Contains("etched");

                item.LangCode = ScryfallClient.MapLang(item.LangRaw);

                decimal price;
                if (NumberHelper.TryParseDecimal(get(row, Field.MinPrice), out price)) item.MinPriceUsd = price;

                bool empty = string.IsNullOrWhiteSpace(item.NameEn)
                             && string.IsNullOrWhiteSpace(item.NamePt)
                             && string.IsNullOrWhiteSpace(item.SetCode);
                if (empty) continue;

                list.Add(item);
            }
            return list;
        }

        public static async Task ResolveAsync(IList<ImportRow> rows, IProgress<int> progress,
            bool overwritePrices, CancellationToken ct)
        {
            int done = 0;
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await ResolveRowAsync(row, overwritePrices, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    row.Status = ImportStatus.Erro;
                    row.StatusDetail = ex.Message;
                }
                done++;
                if (progress != null) progress.Report(done);
            }
        }

        private static async Task ResolveRowAsync(ImportRow row, bool overwritePrices, CancellationToken ct)
        {
            ScryCard card = null;
            var status = ImportStatus.Ok;
            string detail = "";

            bool hasCode = !string.IsNullOrWhiteSpace(row.SetCode) && !string.IsNullOrWhiteSpace(row.CollectorNumber);

            if (hasCode)
            {
                card = await ScryfallClient.GetBySetNumberAsync(row.SetCode, row.CollectorNumber, row.LangCode, ct)
                    .ConfigureAwait(false);

                if (card == null && row.LangCode != "en")
                {
                    card = await ScryfallClient.GetBySetNumberAsync(row.SetCode, row.CollectorNumber, "en", ct)
                        .ConfigureAwait(false);
                    if (card != null) { status = ImportStatus.Aproximado; detail = "idioma indisponível"; }
                }
            }

            if (card == null && !string.IsNullOrWhiteSpace(row.NameEn))
            {
                var prints = await ScryfallClient.SearchExactNameAsync(row.NameEn, row.LangCode != "en", ct)
                    .ConfigureAwait(false);

                card = PickBest(prints, row, out status, out detail);
            }

            if (card == null && !string.IsNullOrWhiteSpace(row.NamePt))
            {
                var prints = await ScryfallClient.SearchAsync("\"" + row.NamePt.Replace("\"", "") + "\"",
                    true, true, 2, ct).ConfigureAwait(false);
                card = PickBest(prints, row, out status, out detail);
            }

            if (card == null)
            {
                row.Status = ImportStatus.NaoEncontrado;
                row.StatusDetail = "";
                row.Include = false;
                return;
            }

            row.Resolved = card;
            row.Status = status;
            row.StatusDetail = detail;

            var price = await PriceResolver.ResolveAsync(card, row.IsFoil, ct).ConfigureAwait(false);
            row.MarketPriceUsd = price.Price;
            row.PriceSource = price.Source;

            if (row.MinPriceUsd <= 0m || overwritePrices)
                row.MinPriceUsd = price.Price ?? 0m;
        }

        private static ScryCard PickBest(List<ScryCard> prints, ImportRow row,
            out ImportStatus status, out string detail)
        {
            status = ImportStatus.Aproximado;
            detail = "";

            if (prints == null || prints.Count == 0) return null;

            var exact = prints.FirstOrDefault(p =>
                Eq(p.Set, row.SetCode) && Eq(p.CollectorNumber, row.CollectorNumber) && Eq(p.Lang, row.LangCode));
            if (exact != null) { status = ImportStatus.Ok; return exact; }

            exact = prints.FirstOrDefault(p => Eq(p.Set, row.SetCode) && Eq(p.CollectorNumber, row.CollectorNumber));
            if (exact != null) { status = ImportStatus.Ok; detail = "idioma diferente"; return exact; }

            var bySet = prints.FirstOrDefault(p => Eq(p.Set, row.SetCode) && Eq(p.Lang, row.LangCode));
            if (bySet != null) { detail = "número diferente"; return bySet; }

            bySet = prints.FirstOrDefault(p => Eq(p.Set, row.SetCode));
            if (bySet != null) { detail = "número diferente"; return bySet; }

            var byLang = prints.FirstOrDefault(p => Eq(p.Lang, row.LangCode) && !p.Digital);
            if (byLang != null) { detail = "edição diferente"; return byLang; }

            var any = prints.FirstOrDefault(p => !p.Digital) ?? prints[0];
            detail = "edição diferente";
            return any;
        }

        private static bool Eq(string a, string b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
            string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static class NumberHelper
    {

        public static bool TryParseDecimal(string s, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(s)) return false;

            s = s.Trim().Replace("$", "").Replace("US", "").Replace("R", "").Trim();

            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out value)) return true;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;

            string alt = s.Replace(".", "").Replace(',', '.');
            return decimal.TryParse(alt, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        public static string FormatUsd(decimal v) =>
            v.ToString("C2", CultureInfo.GetCultureInfo("en-US"));

        public static string FormatUsd(decimal? v) =>
            v.HasValue ? FormatUsd(v.Value) : "—";
    }
}
