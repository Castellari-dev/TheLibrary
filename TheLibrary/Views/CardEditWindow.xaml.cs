using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media.Imaging;
using TheLibrary.Models;
using TheLibrary.Services;

namespace TheLibrary.Views
{
    public partial class CardEditWindow : Window
    {
        private readonly CardEntry _card;
        private ScryCard _lastPicked;
        private string _priceSource = "";

        public bool Deleted { get; private set; }

        public CardEditWindow(CardEntry card, bool isNew = false)
        {
            InitializeComponent();

            _card = card;
            BtnDelete.Visibility = isNew || card.Id <= 0 ? Visibility.Collapsed : Visibility.Visible;
            Title = isNew ? "Adicionar carta" : "Editar carta";

            CmbCondition.ItemsSource = new[] { "M", "NM", "SP", "MP", "HP", "D" };

            Bind();
        }

        private void Bind()
        {
            TxtName.Text = _card.DisplayName;
            TxtSet.Text = string.Format("{0} ({1}) #{2}", _card.SetName, (_card.SetCode ?? "").ToUpperInvariant(),
                _card.CollectorNumber);
            TxtType.Text = _card.TypeLine ?? "";
            TxtArtist.Text = string.IsNullOrWhiteSpace(_card.Artist) ? "" : "Arte: " + _card.Artist;

            TxtQty.Text = _card.Quantity.ToString(CultureInfo.CurrentCulture);
            TxtMinPrice.Text = _card.MinPriceUsd.ToString("0.00", CultureInfo.CurrentCulture);
            TxtLang.Text = ScryfallClient.LangToDisplay(_card.Lang);
            TxtNotes.Text = _card.Notes ?? "";
            ChkFoil.IsChecked = _card.IsFoil;

            CmbCondition.SelectedItem = string.IsNullOrWhiteSpace(_card.Condition) ? "NM" : _card.Condition.ToUpperInvariant();
            if (CmbCondition.SelectedItem == null) CmbCondition.SelectedItem = "NM";

            UpdateMarketLabel();

            LoadImage(_card.ImageUrl);
        }

        private void UpdateMarketLabel()
        {
            if (!_card.MarketPriceUsd.HasValue)
            {
                TxtMarket.Text = "Sem cotação do Scryfall em nenhuma impressão desta carta.";
                return;
            }

            string origem = string.IsNullOrWhiteSpace(_priceSource) || _priceSource == "impressão"
                ? ""
                : " (" + _priceSource + ")";

            TxtMarket.Text = "Preço atual no Scryfall: "
                             + NumberHelper.FormatUsd(_card.MarketPriceUsd.Value) + origem;
        }

        private void LoadImage(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) { ImgCard.Source = null; return; }
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(url, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                ImgCard.Source = bmp;
            }
            catch
            {
                ImgCard.Source = null;
            }
        }

        private async void ChangeArt_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ArtPickerWindow(_card.NameEn, _card.OracleId) { Owner = this };
            if (picker.ShowDialog() != true || picker.SelectedCard == null) return;

            _lastPicked = picker.SelectedCard;
            bool foil = ChkFoil.IsChecked == true;

            CardMapper.ApplyPrinting(_card, picker.SelectedCard, foil, true);

            try
            {
                Busy.Set(true);
                var price = await PriceResolver.ResolveAsync(picker.SelectedCard, foil);
                _card.MarketPriceUsd = price.Price;
                if (price.Found) _card.MinPriceUsd = price.Price.Value;
                _priceSource = price.Source;
            }
            catch { }
            finally { Busy.Set(false); }

            Bind();
        }

        private async void Foil_Click(object sender, RoutedEventArgs e)
        {
            if (_lastPicked == null) return;
            try
            {
                Busy.Set(true);
                var price = await PriceResolver.ResolveAsync(_lastPicked, ChkFoil.IsChecked == true);
                _card.MarketPriceUsd = price.Price;
                _priceSource = price.Source;
                UpdateMarketLabel();
            }
            catch { }
            finally { Busy.Set(false); }
        }

        private void UseMarket_Click(object sender, RoutedEventArgs e)
        {
            if (!_card.MarketPriceUsd.HasValue)
            {
                Dialogs.Warn("Esta impressão não tem cotação no Scryfall.");
                return;
            }
            TxtMinPrice.Text = _card.MarketPriceUsd.Value.ToString("0.00", CultureInfo.CurrentCulture);
        }

        private void OpenScryfall_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_card.ScryfallUri)) return;
            try
            {
                Process.Start(new ProcessStartInfo(_card.ScryfallUri) { UseShellExecute = true });
            }
            catch { }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            int qty;
            if (!int.TryParse((TxtQty.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out qty) || qty < 0)
            {
                Dialogs.Warn("Quantidade inválida.");
                TxtQty.Focus();
                return;
            }

            decimal min;
            if (!NumberHelper.TryParseDecimal(TxtMinPrice.Text, out min) || min < 0)
            {
                Dialogs.Warn("Valor mínimo inválido. Use algo como 1,50 ou 1.50.");
                TxtMinPrice.Focus();
                return;
            }

            _card.Quantity = qty;
            _card.MinPriceUsd = min;
            _card.Condition = (CmbCondition.SelectedItem as string) ?? "NM";
            _card.IsFoil = ChkFoil.IsChecked == true;
            _card.Notes = TxtNotes.Text;

            try
            {
                Busy.Set(true);
                Session.Db.SaveCard(_card);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                Dialogs.Error("Não foi possível salvar:\n\n" + ex.Message);
            }
            finally
            {
                Busy.Set(false);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!Dialogs.Confirm("Remover \"" + _card.DisplayName + "\" da coleção?")) return;
            try
            {
                Session.Db.DeleteCard(_card.Id);
                Deleted = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                Dialogs.Error("Não foi possível excluir:\n\n" + ex.Message);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
