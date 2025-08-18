using System.Configuration;

namespace NetworkFindTool
{
    public static class CredentialStore
    {
        public static void Save(string username, string password)
        {
            Properties.Settings.Default.Username = username;
            Properties.Settings.Default.Password = password;
            Properties.Settings.Default.Save();
        }

        public static (string Username, string Password) Load()
        {
            return (Properties.Settings.Default.Username, Properties.Settings.Default.Password);
        }
    }
}
