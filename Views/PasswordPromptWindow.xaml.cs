using System.Windows;
using TheLibrary.Services;

namespace TheLibrary.Views
{
    public partial class PasswordPromptWindow : Window
    {
        public string Password { get; private set; } = "";

        public PasswordPromptWindow(string prompt)
        {
            InitializeComponent();
            TxtPrompt.Text = prompt;
            Loaded += (s, e) => TxtPass.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            string err = PasswordHasher.Validate(TxtPass.Password);
            if (err != null) { TxtError.Text = err; return; }
            if (TxtPass.Password != TxtPass2.Password) { TxtError.Text = "As senhas não conferem."; return; }

            Password = TxtPass.Password;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
