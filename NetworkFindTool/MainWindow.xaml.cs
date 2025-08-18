using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net.NetworkInformation;
using Renci.SshNet;
using System.IO;

namespace NetworkFindTool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {   
        //TODO 
            /* 
             1. design the result
             2. design the credential 
             3. format the data
             4. executable and build zip
             */

        bool IsSwitchInputValid = false;
        bool IsSwitchValid = false;
        public MainWindow()
        {
            InitializeComponent();
        }
        public class BoolToVisibilityConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                bool isEmpty = (bool)value;
                return isEmpty ? Visibility.Visible : Visibility.Collapsed;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }
        bool IsIPv4(string input)
        {
            return IPAddress.TryParse(input, out var addr) && addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        }
        static bool IsHostname(string input)
        {
            // Basic hostname pattern, allow also single word hostnames
            var hostnameRegex = new Regex(@"^([a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)*[a-zA-Z0-9][a-zA-Z0-9\-]{0,61}[a-zA-Z0-9]$");
            return hostnameRegex.IsMatch(input);
        }
        bool IsMacAddress(string input)
        {
            // Accept formats: 00:11:22:33:44:55, 00-11-22-33-44-55, 0011.2233.4455
            var macRegex = new Regex(@"^([0-9A-Fa-f]{2}([:\-]?)){5}[0-9A-Fa-f]{2}$|^[0-9A-Fa-f]{4}\.[0-9A-Fa-f]{4}\.[0-9A-Fa-f]{4}$");
            return macRegex.IsMatch(input);
        }

        public async void ValidateSwitch()
        {
            string text = SwitchInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Host field is empty. Cannot connect.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                SubmitButton.Content = "Submit";
                return;
            }

            if (IsIPv4(text) || IsHostname(text))
            {
                IsSwitchInputValid = true;
                await IsServerUpAsync(); // only called if host is non-empty and valid
            }
            else
            {
                IsSwitchInputValid = false;
                MessageBox.Show("Invalid Switch info format 💀", "User error", MessageBoxButton.OK, MessageBoxImage.Error);
                SubmitButton.Content = "Submit";
            }
        }

        private async Task<List<result>> FetchHostsFromSwitchAsync()
        {
            var results = new List<result>();

            // 🔽 Show credential popup
            var credentialWindow = new CredentialWindow { Owner = this };
            if (credentialWindow.ShowDialog() != true)
                return results;

            string host = SwitchInput.Text;
            string username = credentialWindow.Username;
            string password = credentialWindow.Password;

            // 🔽 If credentials are empty, return an empty list or placeholder list
            if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("No credentials provided. Returning all available hosts (simulated).", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                // Optionally, populate with dummy or cached data if you have any
                return results;
            }

            try
            {
                await Task.Run(() =>
                {
                    string macOutput = string.Empty;
                    string arpOutput = string.Empty;

                    // Run show mac address-table in first connection
                    using (var client = new SshClient(host, username, password))
                    {
                        client.Connect();
                        macOutput = client.RunCommand("show mac address-table").Result;
                        client.Disconnect();
                    }

                    // Run show ip arp in second connection
                    using (var client = new SshClient(host, username, password))
                    {
                        client.Connect();
                        arpOutput = client.RunCommand("show ip arp").Result;
                        client.Disconnect();
                    }

                    // 🔽 Parse ARP table
                    var arpDict = new Dictionary<string, (string Ip, string Age)>();
                    using (var reader = new StringReader(arpOutput))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            var parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 4 && parts[0] == "Internet")
                            {
                                string ip = parts[1];
                                string age = parts[2];
                                string mac = parts[3].Replace(".", "").ToUpper();
                                arpDict[mac] = (ip, age);
                            }
                        }
                    }

                    // 🔽 Parse MAC address table
                    using (var reader = new StringReader(macOutput))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            var parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 4 && parts[0].All(char.IsDigit))
                            {
                                string vlan = parts[0];
                                string mac = parts[1].Replace(".", "").ToUpper();
                                string type = parts[2];
                                string port = parts[3];

                                var res = new result
                                {
                                    Vlan = vlan,
                                    Mac = mac,
                                    Type = type,
                                    Port = port
                                };

                                if (arpDict.TryGetValue(mac, out var arpInfo))
                                {
                                    res.Ip = arpInfo.Ip;
                                    res.Age = arpInfo.Age;
                                }

                                results.Add(res);
                            }
                        }
                    }
                });

                // 🔽 Normalize MAC helper
                string NormalizeMac(string mac)
                {
                    return mac.Replace(":", "")
                              .Replace("-", "")
                              .Replace(".", "")
                              .ToUpper();
                }

                // 🔽 Apply filters only if user typed something
                if (!string.IsNullOrWhiteSpace(HostIpInput.Text))
                {
                    if (!IsIPv4(HostIpInput.Text))
                    {
                        MessageBox.Show("Invalid IP address format for host filter.", "User error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        results = results.Where(r => r.Ip == HostIpInput.Text).ToList();
                    }
                }

                if (!string.IsNullOrWhiteSpace(HostIpMac.Text))
                {
                    if (!IsMacAddress(HostIpMac.Text))
                    {
                        MessageBox.Show("Invalid MAC address format for host filter.", "User error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        string macSearch = NormalizeMac(HostIpMac.Text);
                        results = results.Where(r => NormalizeMac(r.Mac) == macSearch).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to fetch hosts: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return results;
        }

        public async Task IsServerUpAsync()
        {
            Ping pingSender = new Ping();
            PingOptions options = new PingOptions();
            options.DontFragment = true;

            string data = "U up man?";
            byte[] buffer = Encoding.ASCII.GetBytes(data);
            int timeout = 1000;
            SubmitButton.Content = "Pinging switch ...";

            // Read SwitchInput.Text on UI thread
            string switchAddress = SwitchInput.Text;

            try
            {
                PingReply reply = await Task.Run(() => pingSender.Send(switchAddress, timeout, buffer, options));
                if (reply.Status == IPStatus.Success)
                {
                    IsSwitchValid = true;
                    SubmitButton.Content = "Fetching info ...";

                    // 🔽 Simulate fetching hosts
                    var hostList = await FetchHostsFromSwitchAsync();

                    // 🔽 Show results
                    var resultWindow = new ResultWindow(hostList);
                    resultWindow.Show();

                    SubmitButton.Content = "Submit";
                }
                else
                {
                    MessageBox.Show("Cannot ping switch", "Ping error", MessageBoxButton.OK, MessageBoxImage.Error);
                    SubmitButton.Content = "Submit";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ping failed: " + ex.Message);
                SubmitButton.Content = "Submit";
            }
        }


        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ValidateSwitch();
        }
    }
}
