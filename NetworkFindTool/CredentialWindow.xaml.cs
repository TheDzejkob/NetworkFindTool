using System.Windows;

namespace NetworkFindTool
{
    public partial class CredentialWindow : Window
    {
        public string Username { get; private set; }
        public string Password { get; private set; }

        public CredentialWindow()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Username = UsernameInput.Text;
            Password = PasswordInput.Password;
            this.DialogResult = true;
            this.Close();
        }
    }
}
