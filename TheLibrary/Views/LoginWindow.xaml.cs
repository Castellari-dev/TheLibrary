using System;
using System.Windows;
using System.Windows.Input;
using TheLibrary.Services;

namespace TheLibrary.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();

            var cfg = ConfigService.Current;
            TxtDbInfo.Text = (cfg.Provider == Models.DbProvider.Postgres ? "PostgreSQL" : "SQL Server")
                             + " · " + ConnectionBuilder.GetDatabaseName(cfg.Provider, ConfigService.GetConnectionString());

            TxtUser.Text = cfg.LastUser ?? "";
            if (string.IsNullOrEmpty(TxtUser.Text)) TxtUser.Focus();
            else TxtPass.Focus();
        }

        private void TxtPass_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Login_Click(sender, null);
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            TxtError.Text = "";
            string user = (TxtUser.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(user))
            {
                TxtError.Text = "Informe o usuário.";
                return;
            }

            try
            {
                Busy.Set(true);

                if (Session.Db == null)
                    Session.Db = new Database(ConfigService.Current.Provider, ConfigService.GetConnectionString());

                var u = Session.Db.FindUser(user);
                if (u == null || !PasswordHasher.Verify(TxtPass.Password, u.PasswordHash))
                {
                    TxtError.Text = "Usuário ou senha inválidos.";
                    TxtPass.Clear();
                    TxtPass.Focus();
                    return;
                }

                Session.User = u;

                var cfg = ConfigService.Current;
                cfg.LastUser = u.Username;
                if (!string.IsNullOrWhiteSpace(u.Theme)) cfg.Theme = u.Theme;
                if (!string.IsNullOrWhiteSpace(u.Accent)) cfg.Accent = u.Accent;
                ConfigService.Save(cfg);

                ThemeManager.Apply(cfg.Theme, cfg.Accent);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                TxtError.Text = "Erro ao conectar: " + ex.Message;
            }
            finally
            {
                Busy.Set(false);
            }
        }

        private void Reconfigure_Click(object sender, RoutedEventArgs e)
        {
            var setup = new SetupWindow();
            if (setup.ShowDialog() == true)
            {
                DialogResult = true;
                Close();
            }
        }
    }
}
