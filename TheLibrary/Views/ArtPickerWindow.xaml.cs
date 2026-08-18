using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TheLibrary.Models;
using TheLibrary.Services;

namespace TheLibrary.Views
{
    public partial class ArtPickerWindow : Window
    {
        private readonly string _oracleId;
        private CancellationTokenSource _cts;

        public ScryCard SelectedCard { get; private set; }

        public ArtPickerWindow(string nameEn, string oracleId = null, bool freeSearch = false)
        {
            InitializeComponent();

            _oracleId = oracleId;
            TxtSearch.Text = nameEn ?? "";

            if (freeSearch)
            {
                Title = "Buscar carta no Scryfall";
                TxtInfo.Text = "Digite o nome da carta e pressione Enter.";
            }

            Loaded += async (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(nameEn) || !string.IsNullOrWhiteSpace(oracleId))
                    await LoadAsync();
                else
                    TxtSearch.Focus();
            };
        }

        private async Task LoadAsync()
        {
            if (_cts != null) _cts.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            Progress.Visibility = Visibility.Visible;
            TxtInfo.Text = "Consultando o Scryfall...";
            LstArts.ItemsSource = null;

            try
            {
                bool multi = ChkMultilingual.IsChecked == true;
                List<ScryCard> prints;

                if (!string.IsNullOrWhiteSpace(_oracleId))
                {
                    prints = await ScryfallClient.GetPrintingsAsync(_oracleId, multi, ct);
                }
                else
                {
                    string q = (TxtSearch.Text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(q))
                    {
                        TxtInfo.Text = "Informe um termo de busca.";
                        return;
                    }

                    prints = await ScryfallClient.SearchExactNameAsync(q, multi, ct);
                    if (prints.Count == 0)
                        prints = await ScryfallClient.SearchAsync(q, true, multi, 2, ct);
                }

                if (ct.IsCancellationRequested) return;

                LstArts.ItemsSource = prints;
                TxtInfo.Text = prints.Count == 0
                    ? "Nenhuma impressão encontrada."
                    : prints.Count + " impressão(ões) encontrada(s). Clique para selecionar.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                TxtInfo.Text = "Erro: " + ex.Message;
            }
            finally
            {
                Progress.Visibility = Visibility.Collapsed;
            }
        }

        private async void Search_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await LoadAsync();
        }

        private void LstArts_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstArts.SelectedItem is ScryCard) Confirm_Click(sender, null);
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            var card = LstArts.SelectedItem as ScryCard;
            if (card == null)
            {
                Dialogs.Warn("Selecione uma arte na lista.");
                return;
            }
            SelectedCard = card;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null) _cts.Cancel();
            DialogResult = false;
            Close();
        }
    }
}
