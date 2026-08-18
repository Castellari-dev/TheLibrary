using System;
using System.Windows;
using System.Windows.Controls;
using TheLibrary.Models;
using TheLibrary.Services;

namespace TheLibrary.Views
{
    public partial class SetupWindow : Window
    {
        private int _step = 1;
        private bool _loaded;
        private Database _testedDb;

        public SetupWindow()
        {
            InitializeComponent();

            var cfg = ConfigService.Current;

            CmbProvider.SelectedIndex = cfg.Provider == DbProvider.Postgres ? 1 : 0;
            TxtHost.Text = string.IsNullOrWhiteSpace(cfg.Host) ? "localhost" : cfg.Host;
            TxtPort.Text = string.IsNullOrWhiteSpace(cfg.Port) ? DefaultPort() : cfg.Port;
            TxtDatabase.Text = string.IsNullOrWhiteSpace(cfg.Database) ? "TheLibrary" : cfg.Database;
            TxtDbUser.Text = cfg.User ?? "";
            ChkIntegrated.IsChecked = cfg.IntegratedSecurity;
            ChkTrust.IsChecked = cfg.TrustServerCertificate;
            ChkRaw.IsChecked = cfg.UseRawConnectionString;
            TxtRaw.Text = ConfigService.GetConnectionString();

            CmbTheme.ItemsSource = ThemeManager.Themes;
            CmbTheme.SelectedItem = string.Equals(cfg.Theme, ThemeManager.ThemeDark, StringComparison.OrdinalIgnoreCase)
                ? ThemeManager.ThemeDark : ThemeManager.ThemeLight;

            CmbAccent.ItemsSource = ThemeManager.Accents;
            CmbAccent.SelectedItem = ThemeManager.FindAccent(cfg.Accent);

            _loaded = true;
            UpdateProviderUi();
            UpdateStepUi();
        }

        private DbProvider SelectedProvider =>
            CmbProvider.SelectedIndex == 1 ? DbProvider.Postgres : DbProvider.SqlServer;

        private string DefaultPort() => SelectedProvider == DbProvider.Postgres ? "5432" : "1433";

        private void CmbProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            TxtPort.Text = DefaultPort();
            UpdateProviderUi();
        }

        private void UpdateProviderUi()
        {
            bool isSql = SelectedProvider == DbProvider.SqlServer;
            ChkIntegrated.Visibility = isSql ? Visibility.Visible : Visibility.Collapsed;
            ChkTrust.Visibility = isSql ? Visibility.Visible : Visibility.Collapsed;
            if (!isSql) ChkIntegrated.IsChecked = false;
            UpdateCredentialsUi();
        }

        private void ChkIntegrated_Changed(object sender, RoutedEventArgs e) => UpdateCredentialsUi();

        private void UpdateCredentialsUi()
        {
            bool integrated = SelectedProvider == DbProvider.SqlServer && ChkIntegrated.IsChecked == true;
            if (PnlCredentials != null)
                PnlCredentials.Visibility = integrated ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ChkRaw_Changed(object sender, RoutedEventArgs e)
        {
            if (PnlFields == null || PnlRaw == null) return;
            bool raw = ChkRaw.IsChecked == true;
            PnlFields.Visibility = raw ? Visibility.Collapsed : Visibility.Visible;
            PnlRaw.Visibility = raw ? Visibility.Visible : Visibility.Collapsed;
        }

        private string BuildConnectionString()
        {
            if (ChkRaw.IsChecked == true) return (TxtRaw.Text ?? "").Trim();

            return ConnectionBuilder.Build(
                SelectedProvider,
                TxtHost.Text,
                TxtPort.Text,
                TxtDatabase.Text,
                TxtDbUser.Text,
                TxtDbPass.Password,
                SelectedProvider == DbProvider.SqlServer && ChkIntegrated.IsChecked == true,
                ChkTrust.IsChecked == true);
        }

        private void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Busy.Set(true);
                var db = new Database(SelectedProvider, BuildConnectionString());
                db.TestConnection();
                db.EnsureSchema();
                _testedDb = db;

                TxtDbStatus.Foreground = (System.Windows.Media.Brush)FindResource("Ok");
                TxtDbStatus.Text = "Conexão OK. Tabelas APP_USER e CARD verificadas/criadas.";
            }
            catch (Exception ex)
            {
                _testedDb = null;
                TxtDbStatus.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
                TxtDbStatus.Text = "Falha: " + ex.Message;
            }
            finally
            {
                Busy.Set(false);
            }
        }

        private void CreateDatabase_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Busy.Set(true);
                string cs = BuildConnectionString();
                string dbName = ConnectionBuilder.GetDatabaseName(SelectedProvider, cs);
                string admin = ConnectionBuilder.ToAdmin(SelectedProvider, cs);

                Database.CreateDatabaseIfMissing(SelectedProvider, admin, dbName);

                TxtDbStatus.Foreground = (System.Windows.Media.Brush)FindResource("Ok");
                TxtDbStatus.Text = "Banco \"" + dbName + "\" disponível. Clique em Testar conexão.";
            }
            catch (Exception ex)
            {
                TxtDbStatus.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
                TxtDbStatus.Text = "Falha ao criar o banco: " + ex.Message;
            }
            finally
            {
                Busy.Set(false);
            }
        }


        private void Appearance_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            var accent = CmbAccent.SelectedItem as AccentOption;
            ThemeManager.Apply(CmbTheme.SelectedItem as string, accent != null ? accent.Name : "Verde");
        }

        private void UpdateStepUi()
        {
            Step1.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;

            BtnBack.IsEnabled = _step > 1;
            BtnNext.Content = _step == 3 ? "Concluir" : "Avançar";

            switch (_step)
            {
                case 1: TxtStepTitle.Text = "Passo 1 de 3 — Banco de dados"; break;
                case 2: TxtStepTitle.Text = "Passo 2 de 3 — Usuário e senha"; break;
                default: TxtStepTitle.Text = "Passo 3 de 3 — Aparência"; break;
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_step > 1) _step--;
            UpdateStepUi();
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_step == 1)
            {
                if (_testedDb == null)
                {
                    TestConnection_Click(sender, e);
                    if (_testedDb == null)
                    {
                        MessageBox.Show("Conecte-se ao banco antes de continuar.", "Configuração",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                _step = 2;
                UpdateStepUi();
                TxtUser.Focus();
                return;
            }

            if (_step == 2)
            {
                string user = (TxtUser.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(user))
                {
                    TxtUserStatus.Text = "Informe um nome de usuário.";
                    return;
                }
                string err = PasswordHasher.Validate(TxtPass.Password);
                if (err != null) { TxtUserStatus.Text = err; return; }
                if (TxtPass.Password != TxtPass2.Password)
                {
                    TxtUserStatus.Text = "As senhas não conferem.";
                    return;
                }
                if (_testedDb.FindUser(user) != null)
                {
                    TxtUserStatus.Text = "Já existe um usuário com esse nome no banco.";
                    return;
                }

                TxtUserStatus.Text = "";
                _step = 3;
                UpdateStepUi();
                return;
            }

            Finish();
        }

        private void Finish()
        {
            try
            {
                Busy.Set(true);

                var accent = CmbAccent.SelectedItem as AccentOption;
                string accentName = accent != null ? accent.Name : "Verde";
                string theme = (CmbTheme.SelectedItem as string) ?? ThemeManager.ThemeLight;

                string user = (TxtUser.Text ?? "").Trim();
                int id = _testedDb.CreateUser(user, TxtPass.Password, true, theme, accentName);

                var cfg = ConfigService.Current;
                cfg.Provider = SelectedProvider;
                cfg.Configured = true;
                cfg.Theme = theme;
                cfg.Accent = accentName;
                cfg.LastUser = user;
                cfg.Host = TxtHost.Text;
                cfg.Port = TxtPort.Text;
                cfg.Database = TxtDatabase.Text;
                cfg.User = TxtDbUser.Text;
                cfg.IntegratedSecurity = ChkIntegrated.IsChecked == true;
                cfg.TrustServerCertificate = ChkTrust.IsChecked == true;
                cfg.UseRawConnectionString = ChkRaw.IsChecked == true;
                ConfigService.SetConnectionString(_testedDb.ConnectionString);
                ConfigService.Save(cfg);

                Session.Db = _testedDb;
                Session.User = new AppUser
                {
                    Id = id,
                    Username = user,
                    IsAdmin = true,
                    Theme = theme,
                    Accent = accentName
                };

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Não foi possível concluir a configuração:\n\n" + ex.Message,
                    "Configuração", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Busy.Set(false);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
