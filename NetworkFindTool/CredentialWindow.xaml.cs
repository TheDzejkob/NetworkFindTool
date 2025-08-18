using System.Windows;

namespace NetworkFindTool
{
    public partial class CredentialWindow : Window
    {
        public string Username { get; private set; }
        public string Password { get; private set; }
        public bool RememberCredentials => RememberCredentialsCheckBox.IsChecked == true;

        public CredentialWindow()
        {
            InitializeComponent();
            // Load saved credentials if available
            var creds = CredentialStore.Load();
            UsernameInput.Text = creds.Username;
            PasswordInput.Password = creds.Password;
            RememberCredentialsCheckBox.IsChecked = !string.IsNullOrEmpty(creds.Username) || !string.IsNullOrEmpty(creds.Password);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Username = UsernameInput.Text;
            Password = PasswordInput.Password;
            if (RememberCredentials)
            {
                CredentialStore.Save(Username, Password);
            }
            else
            {
                CredentialStore.Save("", "");
            }
            this.DialogResult = true;
            this.Close();
        }
    }
}
