using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace TheLibrary.Models
{
    public class ScryImageUris
    {
        [JsonPropertyName("small")] public string Small { get; set; }
        [JsonPropertyName("normal")] public string Normal { get; set; }
        [JsonPropertyName("large")] public string Large { get; set; }
        [JsonPropertyName("png")] public string Png { get; set; }
        [JsonPropertyName("art_crop")] public string ArtCrop { get; set; }
        [JsonPropertyName("border_crop")] public string BorderCrop { get; set; }
    }

    public class ScryPrices
    {
        [JsonPropertyName("usd")] public string Usd { get; set; }
        [JsonPropertyName("usd_foil")] public string UsdFoil { get; set; }
        [JsonPropertyName("usd_etched")] public string UsdEtched { get; set; }
        [JsonPropertyName("eur")] public string Eur { get; set; }
    }

    public class ScryFace
    {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("printed_name")] public string PrintedName { get; set; }
        [JsonPropertyName("mana_cost")] public string ManaCost { get; set; }
        [JsonPropertyName("type_line")] public string TypeLine { get; set; }
        [JsonPropertyName("artist")] public string Artist { get; set; }
        [JsonPropertyName("image_uris")] public ScryImageUris ImageUris { get; set; }
    }

    public class ScryCard
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("oracle_id")] public string OracleId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("printed_name")] public string PrintedName { get; set; }
        [JsonPropertyName("lang")] public string Lang { get; set; }
        [JsonPropertyName("set")] public string Set { get; set; }
        [JsonPropertyName("set_name")] public string SetName { get; set; }
        [JsonPropertyName("collector_number")] public string CollectorNumber { get; set; }
        [JsonPropertyName("rarity")] public string Rarity { get; set; }
        [JsonPropertyName("type_line")] public string TypeLine { get; set; }
        [JsonPropertyName("printed_type_line")] public string PrintedTypeLine { get; set; }
        [JsonPropertyName("mana_cost")] public string ManaCost { get; set; }
        [JsonPropertyName("artist")] public string Artist { get; set; }
        [JsonPropertyName("released_at")] public string ReleasedAt { get; set; }
        [JsonPropertyName("scryfall_uri")] public string ScryfallUri { get; set; }
        [JsonPropertyName("prints_search_uri")] public string PrintsSearchUri { get; set; }
        [JsonPropertyName("colors")] public List<string> Colors { get; set; }
        [JsonPropertyName("color_identity")] public List<string> ColorIdentity { get; set; }
        [JsonPropertyName("finishes")] public List<string> Finishes { get; set; }
        [JsonPropertyName("image_uris")] public ScryImageUris ImageUris { get; set; }
        [JsonPropertyName("card_faces")] public List<ScryFace> CardFaces { get; set; }
        [JsonPropertyName("prices")] public ScryPrices Prices { get; set; }
        [JsonPropertyName("digital")] public bool Digital { get; set; }

        private ScryImageUris EffectiveImages
        {
            get
            {
                if (ImageUris != null) return ImageUris;
                if (CardFaces != null && CardFaces.Count > 0) return CardFaces[0].ImageUris;
                return null;
            }
        }

        [JsonIgnore] public string SmallImage => EffectiveImages?.Small;
        [JsonIgnore] public string NormalImage => EffectiveImages?.Normal ?? EffectiveImages?.Large;
        [JsonIgnore] public string ArtCropImage => EffectiveImages?.ArtCrop;

        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(PrintedName) ? Name : PrintedName;

        [JsonIgnore]
        public string EffectiveTypeLine =>
            !string.IsNullOrWhiteSpace(TypeLine) ? TypeLine
            : (CardFaces != null && CardFaces.Count > 0 ? CardFaces[0].TypeLine : null);

        [JsonIgnore]
        public string EffectiveManaCost =>
            !string.IsNullOrWhiteSpace(ManaCost) ? ManaCost
            : (CardFaces != null && CardFaces.Count > 0 ? CardFaces[0].ManaCost : null);

        [JsonIgnore]
        public string EffectiveArtist =>
            !string.IsNullOrWhiteSpace(Artist) ? Artist
            : (CardFaces != null && CardFaces.Count > 0 ? CardFaces[0].Artist : null);

        [JsonIgnore]
        public bool HasFoil => Finishes != null && Finishes.Any(f =>
            string.Equals(f, "foil", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f, "etched", StringComparison.OrdinalIgnoreCase));

        [JsonIgnore]
        public string SetLine => string.Format("{0} ({1}) #{2} · {3}",
            SetName, (Set ?? "").ToUpperInvariant(), CollectorNumber, (Lang ?? "en").ToUpperInvariant());

        [JsonIgnore]
        public string PriceLine
        {
            get
            {
                var usd = GetPriceUsd(false);
                var foil = GetPriceUsd(true);
                if (usd.HasValue && foil.HasValue && foil.Value != usd.Value)
                    return string.Format(CultureInfo.InvariantCulture, "${0:0.00} · foil ${1:0.00}", usd.Value, foil.Value);
                if (usd.HasValue) return string.Format(CultureInfo.InvariantCulture, "${0:0.00}", usd.Value);
                if (foil.HasValue) return string.Format(CultureInfo.InvariantCulture, "foil ${0:0.00}", foil.Value);
                return "sem preço";
            }
        }

        /// <summary>Preço em USD (normal ou foil). Retorna null quando o Scryfall não tem cotação.</summary>
        public decimal? GetPriceUsd(bool foil)
        {
            if (Prices == null) return null;
            string raw = foil ? (Prices.UsdFoil ?? Prices.UsdEtched) : Prices.Usd;
            if (string.IsNullOrWhiteSpace(raw)) raw = foil ? Prices.Usd : (Prices.UsdFoil ?? Prices.UsdEtched);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            decimal v;
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return null;
        }
    }

    public class ScrySearchResult
    {
        [JsonPropertyName("object")] public string Object { get; set; }
        [JsonPropertyName("total_cards")] public int TotalCards { get; set; }
        [JsonPropertyName("has_more")] public bool HasMore { get; set; }
        [JsonPropertyName("next_page")] public string NextPage { get; set; }
        [JsonPropertyName("data")] public List<ScryCard> Data { get; set; }
    }
}
