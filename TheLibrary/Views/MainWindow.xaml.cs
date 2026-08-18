using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TheLibrary.Models;
using TheLibrary.Services;

namespace TheLibrary.Views
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<CardEntry> _view = new ObservableCollection<CardEntry>();
        private List<CardEntry> _all = new List<CardEntry>();

        private readonly ObservableCollection<ImportRow> _importRows = new ObservableCollection<ImportRow>();
        private CancellationTokenSource _resolveCts;

        private ScryCard _addSelected;
        private bool _settingsLoaded;
        private bool _uiReady;

        private static readonly string[] Conditions = { "M", "NM", "SP", "MP", "HP", "D" };

        public MainWindow()
        {
            InitializeComponent();

            GridCards.ItemsSource = _view;
            GridImport.ItemsSource = _importRows;

            CmbAddCondition.ItemsSource = Conditions;
            CmbAddCondition.SelectedItem = "NM";

            var cfg = ConfigService.Current;
            TxtDbBadge.Text = (cfg.Provider == DbProvider.Postgres ? "PostgreSQL" : "SQL Server")
                              + " · " + ConnectionBuilder.GetDatabaseName(cfg.Provider, ConfigService.GetConnectionString());
            TxtUserBadge.Text = Session.User != null ? Session.User.Username : "";

            LoadSettingsTab();

            _uiReady = true;
            LoadCards();
        }

        private void LoadCards()
        {
            try
            {
                Busy.Set(true);
                _all = Session.Db.ListCards();
                ApplyFilter();
                TxtStatus.Text = _all.Count + " impressão(ões) cadastrada(s).";
            }
            catch (Exception ex)
            {
                Dialogs.Error("Não foi possível carregar a coleção:\n\n" + ex.Message);
            }
            finally
            {
                Busy.Set(false);
            }
        }

        private void ApplyFilter()
        {
            string q = (TxtFilter.Text ?? "").Trim().ToLowerInvariant();

            IEnumerable<CardEntry> src = _all;
            if (q.Length > 0)
            {
                src = _all.Where(c =>
                    (c.NameEn ?? "").ToLowerInvariant().Contains(q) ||
                    (c.NamePt ?? "").ToLowerInvariant().Contains(q) ||
                    (c.SetCode ?? "").ToLowerInvariant().Contains(q) ||
                    (c.SetName ?? "").ToLowerInvariant().Contains(q) ||
                    (c.TypeLine ?? "").ToLowerInvariant().Contains(q));
            }

            _view.Clear();
            foreach (var c in src) _view.Add(c);

            int qty = _view.Sum(c => c.Quantity);
            decimal total = _view.Sum(c => c.TotalMinUsd);
            TxtTotals.Text = string.Format(CultureInfo.CurrentCulture,
                "{0} carta(s) · {1} impressão(ões) · mínimo total {2}",
                qty, _view.Count, NumberHelper.FormatUsd(total));
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_uiReady) return;
            ApplyFilter();
        }

        private void GridCards_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_uiReady) return;
            var c = GridCards.SelectedItem as CardEntry;
            if (c == null)
            {
                ImgPreview.Source = null;
                TxtPreviewName.Text = "";
                TxtPreviewSet.Text = "";
                TxtPreviewType.Text = "";
                TxtPreviewArtist.Text = "";
                TxtPreviewPrices.Text = "";
                return;
            }

            ImgPreview.Source = LoadBitmap(c.ImageUrl);
            TxtPreviewName.Text = c.DisplayName;
            TxtPreviewSet.Text = string.Format("{0} ({1}) #{2} · {3}{4}",
                c.SetName, (c.SetCode ?? "").ToUpperInvariant(), c.CollectorNumber,
                ScryfallClient.LangToDisplay(c.Lang), c.IsFoil ? " · Foil" : "");
            TxtPreviewType.Text = c.TypeLine ?? "";
            TxtPreviewArtist.Text = string.IsNullOrWhiteSpace(c.Artist) ? "" : "Arte: " + c.Artist;
            TxtPreviewPrices.Text = string.Format("Mínimo {0} · Scryfall {1}",
                NumberHelper.FormatUsd(c.MinPriceUsd), NumberHelper.FormatUsd(c.MarketPriceUsd));
        }

        private static BitmapImage LoadBitmap(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(url, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                return bmp;
            }
            catch { return null; }
        }

        private void GridCards_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditCard_Click(sender, null);

        private void EditCard_Click(object sender, RoutedEventArgs e)
        {
            var c = GridCards.SelectedItem as CardEntry;
            if (c == null) { Dialogs.Warn("Selecione uma carta."); return; }

            var win = new CardEditWindow(c) { Owner = this };
            if (win.ShowDialog() == true) LoadCards();
        }

        private async void SwapArt_Click(object sender, RoutedEventArgs e)
        {
            var c = GridCards.SelectedItem as CardEntry;
            if (c == null) { Dialogs.Warn("Selecione uma carta."); return; }

            var picker = new ArtPickerWindow(c.NameEn, c.OracleId) { Owner = this };
            if (picker.ShowDialog() != true || picker.SelectedCard == null) return;

            try
            {
                CardMapper.ApplyPrinting(c, picker.SelectedCard, c.IsFoil, true);

                var price = await PriceResolver.ResolveAsync(picker.SelectedCard, c.IsFoil);
                c.MarketPriceUsd = price.Price;
                if (price.Found) c.MinPriceUsd = price.Price.Value;

                Session.Db.UpdateCard(c);
                LoadCards();
                TxtStatus.Text = "Arte atualizada para " + picker.SelectedCard.SetLine;
            }
            catch (Exception ex)
            {
                Dialogs.Error("Não foi possível trocar a arte:\n\n" + ex.Message);
            }
        }

        private void DeleteCard_Click(object sender, RoutedEventArgs e)
        {
            var c = GridCards.SelectedItem as CardEntry;
            if (c == null) { Dialogs.Warn("Selecione uma carta."); return; }
            if (!Dialogs.Confirm("Remover \"" + c.DisplayName + "\" da coleção?")) return;

            try
            {
                Session.Db.DeleteCard(c.Id);
                LoadCards();
            }
            catch (Exception ex)
            {
                Dialogs.Error("Não foi possível excluir:\n\n" + ex.Message);
            }
        }

        private async void RefreshPrices_Click(object sender, RoutedEventArgs e)
        {
            var targets = _view.Where(c => !string.IsNullOrWhiteSpace(c.ScryfallId)).ToList();
            if (targets.Count == 0) { Dialogs.Warn("Nenhuma carta resolvida no Scryfall para atualizar."); return; }
            if (!Dialogs.Confirm("Atualizar o preço de referência de " + targets.Count + " impressão(ões)?\n\n" +
                                 "Onde o valor mínimo estiver zerado, ele será preenchido com o preço encontrado."))
                return;

            PriceResolver.ClearCache();

            int done = 0, updated = 0, semCotacao = 0, minimosPreenchidos = 0;
            foreach (var c in targets)
            {
                try
                {
                    var s = await ScryfallClient.GetByIdAsync(c.ScryfallId);
                    if (s != null)
                    {
                        var price = await PriceResolver.ResolveAsync(s, c.IsFoil);

                        c.MarketPriceUsd = price.Price;

                        if (c.MinPriceUsd <= 0m && price.Found)
                        {
                            c.MinPriceUsd = price.Price.Value;
                            minimosPreenchidos++;
                        }

                        Session.Db.UpdateCard(c);

                        if (price.Found) updated++;
                        else semCotacao++;
                    }
                }
                catch { }

                done++;
                TxtStatus.Text = "Atualizando preços... " + done + "/" + targets.Count;
            }

            TxtStatus.Text = string.Format(
                "{0} preço(s) atualizado(s) · {1} mínimo(s) preenchido(s) · {2} sem cotação em nenhuma impressão.",
                updated, minimosPreenchidos, semCotacao);

            LoadCards();
        }

        private async void TxtAddSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await RunAddSearch();
        }

        private async void AddSearch_Click(object sender, RoutedEventArgs e) => await RunAddSearch();

        private async Task RunAddSearch()
        {
            string q = (TxtAddSearch.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q)) { TxtAddInfo.Text = "Digite o nome da carta."; return; }

            AddProgress.Visibility = Visibility.Visible;
            TxtAddInfo.Text = "Consultando o Scryfall...";
            LstAddResults.ItemsSource = null;

            try
            {
                bool multi = ChkAddMultilingual.IsChecked == true;

                var prints = await ScryfallClient.SearchExactNameAsync(q, multi);
                if (prints.Count == 0) prints = await ScryfallClient.SearchAsync(q, true, multi, 2);

                LstAddResults.ItemsSource = prints;
                TxtAddInfo.Text = prints.Count == 0
                    ? "Nenhum resultado. Tente o nome em inglês."
                    : prints.Count + " impressão(ões). Clique na arte que você tem.";
            }
            catch (Exception ex)
            {
                TxtAddInfo.Text = "Erro: " + ex.Message;
            }
            finally
            {
                AddProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void LstAddResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _addSelected = LstAddResults.SelectedItem as ScryCard;
            if (_addSelected == null)
            {
                ImgAddPreview.Source = null;
                TxtAddName.Text = "";
                TxtAddSet.Text = "";
                return;
            }

            ImgAddPreview.Source = LoadBitmap(_addSelected.NormalImage);
            TxtAddName.Text = _addSelected.DisplayName;
            TxtAddSet.Text = _addSelected.SetLine;

            ChkAddFoil.IsEnabled = true;
            UpdateAddPrice();
        }

        private void AddFoil_Click(object sender, RoutedEventArgs e) => UpdateAddPrice();

        private void UpdateAddPrice()
        {
            if (_addSelected == null) return;
            var p = _addSelected.GetPriceUsd(ChkAddFoil.IsChecked == true);
            TxtAddMinPrice.Text = (p ?? 0m).ToString("0.00", CultureInfo.CurrentCulture);
        }

        private void LstAddResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstAddResults.SelectedItem is ScryCard) AddToCollection_Click(sender, null);
        }

        private void AddToCollection_Click(object sender, RoutedEventArgs e)
        {
            if (_addSelected == null) { Dialogs.Warn("Selecione uma arte na lista."); return; }

            int qty;
            if (!int.TryParse((TxtAddQty.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out qty) || qty <= 0)
            {
                Dialogs.Warn("Quantidade inválida.");
                return;
            }

            decimal min;
            if (!NumberHelper.TryParseDecimal(TxtAddMinPrice.Text, out min) || min < 0)
            {
                Dialogs.Warn("Valor mínimo inválido.");
                return;
            }

            bool foil = ChkAddFoil.IsChecked == true;
            string cond = (CmbAddCondition.SelectedItem as string) ?? "NM";

            try
            {
                Busy.Set(true);

                var existing = Session.Db.FindPrinting(_addSelected.Id, foil, cond);
                if (existing != null)
                {
                    if (Dialogs.Confirm("Esta impressão já está na coleção com " + existing.Quantity +
                                        " unidade(s). Somar +" + qty + "?"))
                    {
                        existing.Quantity += qty;
                        existing.MinPriceUsd = min;
                        Session.Db.UpdateCard(existing);
                    }
                    else return;
                }
                else
                {
                    var card = new CardEntry { Quantity = qty, MinPriceUsd = min, IsFoil = foil, Condition = cond };
                    CardMapper.ApplyPrinting(card, _addSelected, foil, false);
                    card.MinPriceUsd = min;
                    Session.Db.InsertCard(card);
                }

                LoadCards();
                Tabs.SelectedIndex = 0;
                TxtStatus.Text = "Carta adicionada: " + _addSelected.SetLine;
            }
            catch (Exception ex)
            {
                Dialogs.Error("Não foi possível adicionar:\n\n" + ex.Message);
            }
            finally
            {
                Busy.Set(false);
            }
        }

        private void PickCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Arquivos CSV (*.csv)|*.csv|Todos os arquivos (*.*)|*.*",
                Title = "Selecionar CSV da coleção"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                Busy.Set(true);

                var table = CsvParser.Parse(dlg.FileName);
                List<string> unmapped;
                var rows = CsvImporter.MapRows(table, out unmapped);

                _importRows.Clear();
                foreach (var r in rows) _importRows.Add(r);

                TxtCsvInfo.Text = string.Format(
                    "{0} linha(s) lida(s) · delimitador '{1}' · codificação {2}{3}",
                    rows.Count,
                    table.Delimiter == '\t' ? "TAB" : table.Delimiter.ToString(),
                    table.EncodingName,
                    unmapped.Count > 0 ? " · colunas ignoradas: " + string.Join(", ", unmapped) : "");

                BtnResolve.IsEnabled = rows.Count > 0;
                BtnImport.IsEnabled = rows.Count > 0;
                CsvProgress.Value = 0;
                UpdateImportSummary();
            }
            catch (Exception ex)
            {
                Dialogs.Error("Não foi possível ler o CSV:\n\n" + ex.Message);
            }
            finally
            {
                Busy.Set(false);
            }
        }

        private async void ResolveCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_importRows.Count == 0) return;

            _resolveCts = new CancellationTokenSource();
            BtnResolve.IsEnabled = false;
            BtnCancelResolve.IsEnabled = true;
            BtnImport.IsEnabled = false;

            int total = _importRows.Count;
            CsvProgress.Maximum = total;
            CsvProgress.Value = 0;

            var progress = new Progress<int>(done =>
            {
                CsvProgress.Value = done;
                TxtCsvInfo.Text = "Resolvendo no Scryfall... " + done + "/" + total;
            });

            try
            {
                await CsvImporter.ResolveAsync(_importRows, progress,
                    ChkOverwritePrices.IsChecked == true, _resolveCts.Token);

                TxtCsvInfo.Text = "Resolução concluída.";
            }
            catch (OperationCanceledException)
            {
                TxtCsvInfo.Text = "Resolução cancelada.";
            }
            catch (Exception ex)
            {
                Dialogs.Error("Erro durante a resolução:\n\n" + ex.Message);
            }
            finally
            {
                BtnResolve.IsEnabled = true;
                BtnCancelResolve.IsEnabled = false;
                BtnImport.IsEnabled = true;
                GridImport.Items.Refresh();
                UpdateImportSummary();
            }
        }

        private void CancelResolve_Click(object sender, RoutedEventArgs e)
        {
            if (_resolveCts != null) _resolveCts.Cancel();
        }

        private void GridImport_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = GridImport.SelectedItem as ImportRow;
            if (row == null) return;

            string name = !string.IsNullOrWhiteSpace(row.NameEn) ? row.NameEn : row.NamePt;
            string oracle = row.Resolved != null ? row.Resolved.OracleId : null;

            var picker = new ArtPickerWindow(name, oracle) { Owner = this };
            if (picker.ShowDialog() != true || picker.SelectedCard == null) return;

            row.Resolved = picker.SelectedCard;
            row.Status = ImportStatus.Ok;
            row.StatusDetail = "escolhido manualmente";
            row.Include = true;
            if (ChkOverwritePrices.IsChecked == true || row.MinPriceUsd <= 0m)
                row.MinPriceUsd = picker.SelectedCard.GetPriceUsd(row.IsFoil) ?? 0m;

            GridImport.Items.Refresh();
            UpdateImportSummary();
        }

        private void CheckAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _importRows) r.Include = true;
            GridImport.Items.Refresh();
            UpdateImportSummary();
        }

        private void CheckResolved_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _importRows) r.Include = r.Resolved != null;
            GridImport.Items.Refresh();
            UpdateImportSummary();
        }

        private void UpdateImportSummary()
        {
            int ok = _importRows.Count(r => r.Status == ImportStatus.Ok);
            int aprox = _importRows.Count(r => r.Status == ImportStatus.Aproximado);
            int nao = _importRows.Count(r => r.Status == ImportStatus.NaoEncontrado || r.Status == ImportStatus.Erro);
            int sel = _importRows.Count(r => r.Include);

            TxtImportSummary.Text = string.Format(
                "{0} exata(s) · {1} aproximada(s) · {2} sem correspondência · {3} marcada(s) para importar. " +
                "Dê duplo clique numa linha para escolher a arte manualmente.",
                ok, aprox, nao, sel);
        }

        private void ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            var selected = _importRows.Where(r => r.Include).ToList();
            if (selected.Count == 0) { Dialogs.Warn("Nenhuma linha marcada."); return; }
            if (!Dialogs.Confirm("Importar " + selected.Count + " linha(s) para o banco?")) return;

            int inserted = 0, merged = 0, failed = 0;
            bool sum = ChkSumQuantities.IsChecked == true;

            try
            {
                Busy.Set(true);

                foreach (var row in selected)
                {
                    try
                    {
                        var card = row.ToCardEntry();

                        CardEntry existing = null;
                        if (sum && !string.IsNullOrWhiteSpace(card.ScryfallId))
                            existing = Session.Db.FindPrinting(card.ScryfallId, card.IsFoil, card.Condition);

                        if (existing != null)
                        {
                            existing.Quantity += card.Quantity;
                            if (card.MinPriceUsd > 0m) existing.MinPriceUsd = card.MinPriceUsd;
                            existing.MarketPriceUsd = card.MarketPriceUsd;
                            Session.Db.UpdateCard(existing);
                            merged++;
                        }
                        else
                        {
                            Session.Db.InsertCard(card);
                            inserted++;
                        }
                    }
                    catch
                    {
                        failed++;
                    }
                }
            }
            finally
            {
                Busy.Set(false);
            }

            Dialogs.Info(string.Format("Importação concluída.\n\nInseridas: {0}\nSomadas a existentes: {1}\nFalhas: {2}",
                inserted, merged, failed));

            LoadCards();
            Tabs.SelectedIndex = 0;
        }

        private void LoadSettingsTab()
        {
            var cfg = ConfigService.Current;

            SetCmbProvider.SelectedIndex = cfg.Provider == DbProvider.Postgres ? 1 : 0;
            SetTxtHost.Text = cfg.Host ?? "localhost";
            SetTxtPort.Text = cfg.Port ?? "";
            SetTxtDatabase.Text = cfg.Database ?? "";
            SetTxtDbUser.Text = cfg.User ?? "";
            SetChkIntegrated.IsChecked = cfg.IntegratedSecurity;
            SetChkTrust.IsChecked = cfg.TrustServerCertificate;
            SetChkRaw.IsChecked = cfg.UseRawConnectionString;
            SetTxtRaw.Text = ConfigService.GetConnectionString();
            SetTxtConfigPath.Text = "Configuração local: " + ConfigService.ConfigPath;

            SetCmbTheme.ItemsSource = ThemeManager.Themes;
            SetCmbTheme.SelectedItem = string.Equals(cfg.Theme, ThemeManager.ThemeDark, StringComparison.OrdinalIgnoreCase)
                ? ThemeManager.ThemeDark : ThemeManager.ThemeLight;

            SetCmbAccent.ItemsSource = ThemeManager.Accents;
            SetCmbAccent.SelectedItem = ThemeManager.FindAccent(cfg.Accent);

            _settingsLoaded = true;

            UpdateSettingsProviderUi();
            SetRaw_Changed(null, null);
            ReloadUsers();
        }

        private DbProvider SettingsProvider =>
            SetCmbProvider.SelectedIndex == 1 ? DbProvider.Postgres : DbProvider.SqlServer;

        private void SetProvider_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_settingsLoaded) return;
            SetTxtPort.Text = SettingsProvider == DbProvider.Postgres ? "5432" : "1433";
            UpdateSettingsProviderUi();
        }

        private void UpdateSettingsProviderUi()
        {
            bool isSql = SettingsProvider == DbProvider.SqlServer;
            SetChkIntegrated.Visibility = isSql ? Visibility.Visible : Visibility.Collapsed;
            SetChkTrust.Visibility = isSql ? Visibility.Visible : Visibility.Collapsed;
            if (!isSql) SetChkIntegrated.IsChecked = false;
            SetIntegrated_Changed(null, null);
        }

        private void SetIntegrated_Changed(object sender, RoutedEventArgs e)
        {
            if (SetPnlCredentials == null) return;
            bool integrated = SettingsProvider == DbProvider.SqlServer && SetChkIntegrated.IsChecked == true;
            SetPnlCredentials.Visibility = integrated ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SetRaw_Changed(object sender, RoutedEventArgs e)
        {
            if (SetPnlFields == null || SetPnlRaw == null) return;
            bool raw = SetChkRaw.IsChecked == true;
            SetPnlFields.Visibility = raw ? Visibility.Collapsed : Visibility.Visible;
            SetPnlRaw.Visibility = raw ? Visibility.Visible : Visibility.Collapsed;
        }

        private string BuildSettingsConnectionString()
        {
            if (SetChkRaw.IsChecked == true) return (SetTxtRaw.Text ?? "").Trim();

            return ConnectionBuilder.Build(
                SettingsProvider,
                SetTxtHost.Text,
                SetTxtPort.Text,
                SetTxtDatabase.Text,
                SetTxtDbUser.Text,
                SetTxtDbPass.Password,
                SettingsProvider == DbProvider.SqlServer && SetChkIntegrated.IsChecked == true,
                SetChkTrust.IsChecked == true);
        }

        private void SetTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Busy.Set(true);
                var db = new Database(SettingsProvider, BuildSettingsConnectionString());
                db.TestConnection();
                db.EnsureSchema();
                SetTxtDbStatus.Foreground = (System.Windows.Media.Brush)FindResource("Ok");
                SetTxtDbStatus.Text = "Conexão OK e schema verificado.";
            }
            catch (Exception ex)
            {
                SetTxtDbStatus.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
                SetTxtDbStatus.Text = "Falha: " + ex.Message;
            }
            finally { Busy.Set(false); }
        }

        private void SetCreateDb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Busy.Set(true);
                string cs = BuildSettingsConnectionString();
                string name = ConnectionBuilder.GetDatabaseName(SettingsProvider, cs);
                Database.CreateDatabaseIfMissing(SettingsProvider, ConnectionBuilder.ToAdmin(SettingsProvider, cs), name);
                SetTxtDbStatus.Foreground = (System.Windows.Media.Brush)FindResource("Ok");
                SetTxtDbStatus.Text = "Banco \"" + name + "\" disponível.";
            }
            catch (Exception ex)
            {
                SetTxtDbStatus.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
                SetTxtDbStatus.Text = "Falha ao criar o banco: " + ex.Message;
            }
            finally { Busy.Set(false); }
        }

        private void SetSaveDb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Busy.Set(true);
                string cs = BuildSettingsConnectionString();
                var db = new Database(SettingsProvider, cs);
                db.TestConnection();
                db.EnsureSchema();

                if (db.CountUsers() == 0)
                {
                    if (!Dialogs.Confirm("Este banco ainda não tem usuários. Seu usuário atual será recriado nele. Continuar?"))
                        return;

                    string pwd = AskNewPasswordFor(Session.User.Username);
                    if (pwd == null)
                    {
                        SetTxtDbStatus.Text = "Operação cancelada: o novo banco precisa de um usuário.";
                        return;
                    }
                    db.CreateUser(Session.User.Username, pwd, true,
                        ThemeManager.CurrentTheme, ThemeManager.CurrentAccent);
                }

                var cfg = ConfigService.Current;
                cfg.Provider = SettingsProvider;
                cfg.Host = SetTxtHost.Text;
                cfg.Port = SetTxtPort.Text;
                cfg.Database = SetTxtDatabase.Text;
                cfg.User = SetTxtDbUser.Text;
                cfg.IntegratedSecurity = SetChkIntegrated.IsChecked == true;
                cfg.TrustServerCertificate = SetChkTrust.IsChecked == true;
                cfg.UseRawConnectionString = SetChkRaw.IsChecked == true;
                cfg.Configured = true;
                ConfigService.SetConnectionString(cs);
                ConfigService.Save(cfg);

                Session.Db = db;

                TxtDbBadge.Text = (cfg.Provider == DbProvider.Postgres ? "PostgreSQL" : "SQL Server")
                                  + " · " + ConnectionBuilder.GetDatabaseName(cfg.Provider, cs);

                SetTxtDbStatus.Foreground = (System.Windows.Media.Brush)FindResource("Ok");
                SetTxtDbStatus.Text = "Conectado e salvo.";

                ReloadUsers();
                LoadCards();
            }
            catch (Exception ex)
            {
                SetTxtDbStatus.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
                SetTxtDbStatus.Text = "Falha: " + ex.Message;
            }
            finally { Busy.Set(false); }
        }

        private string AskNewPasswordFor(string username)
        {
            var win = new PasswordPromptWindow("Defina a senha de \"" + username + "\" no novo banco") { Owner = this };
            return win.ShowDialog() == true ? win.Password : null;
        }

        private void SetAppearance_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_settingsLoaded) return;

            string theme = (SetCmbTheme.SelectedItem as string) ?? ThemeManager.ThemeLight;
            var accent = SetCmbAccent.SelectedItem as AccentOption;
            string accentName = accent != null ? accent.Name : "Verde";

            ThemeManager.Apply(theme, accentName);

            var cfg = ConfigService.Current;
            cfg.Theme = theme;
            cfg.Accent = accentName;
            ConfigService.Save(cfg);

            try
            {
                if (Session.User != null && Session.Db != null)
                {
                    Session.Db.UpdateUserPrefs(Session.User.Id, theme, accentName);
                    Session.User.Theme = theme;
                    Session.User.Accent = accentName;
                }
            }
            catch { /* preferência local já foi salva */ }
        }


        private void ReloadUsers()
        {
            try
            {
                GridUsers.ItemsSource = Session.Db.ListUsers();
            }
            catch (Exception ex)
            {
                SetTxtUserStatus.Text = "Não foi possível listar usuários: " + ex.Message;
            }
        }

        private void CreateUser_Click(object sender, RoutedEventArgs e)
        {
            string user = (SetTxtNewUser.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(user)) { SetTxtUserStatus.Text = "Informe o nome do usuário."; return; }

            string err = PasswordHasher.Validate(SetTxtNewPass.Password);
            if (err != null) { SetTxtUserStatus.Text = err; return; }

            try
            {
                if (Session.Db.FindUser(user) != null)
                {
                    SetTxtUserStatus.Text = "Já existe um usuário com esse nome.";
                    return;
                }

                Session.Db.CreateUser(user, SetTxtNewPass.Password, SetChkNewAdmin.IsChecked == true,
                    ThemeManager.CurrentTheme, ThemeManager.CurrentAccent);

                SetTxtNewUser.Clear();
                SetTxtNewPass.Clear();
                SetChkNewAdmin.IsChecked = false;
                SetTxtUserStatus.Text = "Usuário criado.";
                ReloadUsers();
            }
            catch (Exception ex)
            {
                SetTxtUserStatus.Text = "Erro: " + ex.Message;
            }
        }

        private void ChangeMyPassword_Click(object sender, RoutedEventArgs e)
        {
            var win = new PasswordPromptWindow("Nova senha para \"" + Session.User.Username + "\"") { Owner = this };
            if (win.ShowDialog() != true) return;

            try
            {
                Session.Db.UpdateUserPassword(Session.User.Id, win.Password);
                SetTxtUserStatus.Text = "Senha alterada.";
            }
            catch (Exception ex)
            {
                SetTxtUserStatus.Text = "Erro: " + ex.Message;
            }
        }

        private void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            var u = GridUsers.SelectedItem as AppUser;
            if (u == null) { SetTxtUserStatus.Text = "Selecione um usuário na lista."; return; }
            if (u.Id == Session.User.Id) { SetTxtUserStatus.Text = "Você não pode excluir o próprio usuário logado."; return; }
            if (!Dialogs.Confirm("Excluir o usuário \"" + u.Username + "\"?")) return;

            try
            {
                Session.Db.DeleteUser(u.Id);
                SetTxtUserStatus.Text = "Usuário excluído.";
                ReloadUsers();
            }
            catch (Exception ex)
            {
                SetTxtUserStatus.Text = "Erro: " + ex.Message;
            }
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            if (!Dialogs.Confirm("Sair do aplicativo?")) return;
            Application.Current.Shutdown();
        }
    }
}
