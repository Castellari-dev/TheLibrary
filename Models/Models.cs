using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace TheLibrary.Models
{
    public enum DbProvider
    {
        SqlServer = 0,
        Postgres = 1
    }

    /// <summary>Configuração local (%APPDATA%\TheLibrary\config.json).</summary>
    public class AppConfig
    {
        public DbProvider Provider { get; set; } = DbProvider.SqlServer;

        /// <summary>Connection string protegida com DPAPI (base64).</summary>
        public string ConnectionProtected { get; set; } = "";

        public bool Configured { get; set; } = false;

        public string Theme { get; set; } = "Claro";
        public string Accent { get; set; } = "Verde";

        public string LastUser { get; set; } = "";

        // Campos apenas para repopular a tela de configuração (sem a senha).
        public string Host { get; set; } = "localhost";
        public string Port { get; set; } = "";
        public string Database { get; set; } = "TheLibrary";
        public string User { get; set; } = "";
        public bool IntegratedSecurity { get; set; } = true;
        public bool TrustServerCertificate { get; set; } = true;
        public bool UseRawConnectionString { get; set; } = false;
    }

    public class AppUser
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public bool IsAdmin { get; set; }
        public string Theme { get; set; } = "Claro";
        public string Accent { get; set; } = "Verde";
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Uma carta (impressão específica) na coleção.</summary>
    public class CardEntry : INotifyPropertyChanged
    {
        private int _quantity = 1;
        private decimal _minPriceUsd;
        private decimal? _marketPriceUsd;
        private string _imageUrl;
        private string _setCode;
        private string _setName;
        private string _collectorNumber;
        private string _lang;
        private bool _isFoil;
        private string _condition = "NM";
        private string _namePt;
        private string _artist;

        public int Id { get; set; }
        public string ScryfallId { get; set; } = "";
        public string OracleId { get; set; } = "";
        public string NameEn { get; set; } = "";

        public string NamePt { get => _namePt; set { _namePt = value; OnChanged(); OnChanged(nameof(DisplayName)); } }
        public string SetCode { get => _setCode; set { _setCode = value; OnChanged(); OnChanged(nameof(SetDisplay)); } }
        public string SetName { get => _setName; set { _setName = value; OnChanged(); OnChanged(nameof(SetDisplay)); } }
        public string CollectorNumber { get => _collectorNumber; set { _collectorNumber = value; OnChanged(); } }
        public string Lang { get => _lang; set { _lang = value; OnChanged(); } }
        public bool IsFoil { get => _isFoil; set { _isFoil = value; OnChanged(); OnChanged(nameof(FoilDisplay)); } }
        public string Condition { get => _condition; set { _condition = value; OnChanged(); } }
        public string Artist { get => _artist; set { _artist = value; OnChanged(); } }

        public string Rarity { get; set; }
        public string TypeLine { get; set; }
        public string ManaCost { get; set; }
        public string Colors { get; set; }
        public string ArtCropUrl { get; set; }
        public string ScryfallUri { get; set; }
        public string Notes { get; set; }

        public string ImageUrl
        {
            get => _imageUrl;
            set { _imageUrl = value; OnChanged(); }
        }

        public int Quantity
        {
            get => _quantity;
            set { _quantity = value; OnChanged(); OnChanged(nameof(TotalMinUsd)); }
        }

        /// <summary>Valor mínimo em dólar que o usuário aceita por esta arte/edição.</summary>
        public decimal MinPriceUsd
        {
            get => _minPriceUsd;
            set { _minPriceUsd = value; OnChanged(); OnChanged(nameof(TotalMinUsd)); }
        }

        /// <summary>Último preço lido do Scryfall (referência).</summary>
        public decimal? MarketPriceUsd
        {
            get => _marketPriceUsd;
            set { _marketPriceUsd = value; OnChanged(); }
        }

        public decimal TotalMinUsd => MinPriceUsd * Quantity;

        public string DisplayName => string.IsNullOrWhiteSpace(NamePt) ? NameEn : NamePt;
        public string SetDisplay => string.IsNullOrWhiteSpace(SetCode) ? SetName : SetCode.ToUpperInvariant();
        public string FoilDisplay => IsFoil ? "Foil" : "";

        public string Summary =>
            string.Format(CultureInfo.InvariantCulture, "{0} · {1} #{2} · {3}{4}",
                NameEn, SetDisplay, CollectorNumber, (Lang ?? "en").ToUpperInvariant(), IsFoil ? " · Foil" : "");

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public CardEntry Clone() => (CardEntry)MemberwiseClone();
    }
}
