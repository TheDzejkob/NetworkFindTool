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
using System.Reflection.Metadata;
using System.Net.Sockets;

namespace NetworkFindTool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {   
        //TODO 
            /* 
             1. design the result ✔
             2. design the credential ✔
             3. format the data ✔
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

            // Remove format validation, allow both IP and hostname
            IsSwitchInputValid = true;
            await IsServerUpAsync(); // only called if host is non-empty and valid
        }
        private static IEnumerable<int> ParsePort(string port)
        {
            // Najde všechny čísla v portu a převede je na int
            var matches = System.Text.RegularExpressions.Regex.Matches(port, @"\d+");
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                yield return int.Parse(match.Value);
            }
        }

        private static IEnumerable<int> ParseIp(string ip)
        {
            var parts = (ip ?? "").Split('.');
            for (int i = 0; i < 4; i++)
            {
                if (i < parts.Length && int.TryParse(parts[i], out int num))
                    yield return num;
                else
                    yield return 0;
            }
        }
        private class IpComparer : IComparer<IEnumerable<int>>
        {
            public int Compare(IEnumerable<int> x, IEnumerable<int> y)
            {
                var xList = x.ToList();
                var yList = y.ToList();
                int minLen = Math.Min(xList.Count, yList.Count);
                for (int i = 0; i < minLen; i++)
                {
                    int cmp = xList[i].CompareTo(yList[i]);
                    if (cmp != 0)
                        return cmp;
                }
                return xList.Count.CompareTo(yList.Count);
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
                    string intStatusOutput = string.Empty;

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
                    // Run show int status in third connection
                    using (var client = new SshClient(host, username, password))
                    {
                        client.Connect();
                        intStatusOutput = client.RunCommand("show int status").Result;
                        client.Disconnect();
                    }

                    //// Debug: Save the raw output to see what we're actually getting
                    //MessageBox.Show($"Debug - show int status output:\n{intStatusOutput.Substring(0, Math.Min(500, intStatusOutput.Length))}...", "Debug", MessageBoxButton.OK);

                    // Parse show int status
                    var intStatusDict = new Dictionary<string, (string Name, string Status)>();
                    using (var reader = new StringReader(intStatusOutput))
                    {
                        string line;
                        bool headerSkipped = false;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (!headerSkipped)
                            {
                                if (line.Trim().StartsWith("Port"))
                                {
                                    headerSkipped = true;
                                }
                                continue;
                            }
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            
                            // Parse Cisco interface status format
                            // Typical format: Port Name Status Vlan Duplex Speed Type
                            // But let's be more flexible since formats vary
                            var parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            
                            if (parts.Length >= 3)
                            {
                                string port = parts[0];
                                string name = "";
                                string status = "";
                                
                                // Simple approach: assume name is in position 1, status in position 2
                                if (parts.Length >= 2) name = parts[1];
                                if (parts.Length >= 3) status = parts[2];
                                
                                // If name looks like a status (connected/notconnect/disabled), shift everything
                                if (name.ToLower().Contains("connect") || name.ToLower().Contains("disabled"))
                                {
                                    status = name;
                                    name = ""; // No name provided
                                }
                                
                                // Handle multi-word names by checking if status field contains valid status
                                if (parts.Length > 3 && !status.ToLower().Contains("connect") && !status.ToLower().Contains("disabled"))
                                {
                                    // Reconstruct name from multiple parts until we find status
                                    var nameParts = new List<string>();
                                    int statusIndex = -1;
                                    
                                    for (int i = 1; i < parts.Length; i++)
                                    {
                                        string part = parts[i].ToLower();
                                        if (part.Contains("connect") || part.Contains("disabled") || part == "up" || part == "down")
                                        {
                                            statusIndex = i;
                                            status = parts[i];
                                            break;
                                        }
                                        nameParts.Add(parts[i]);
                                    }
                                    
                                    if (statusIndex > 1)
                                    {
                                        name = string.Join(" ", nameParts);
                                    }
                                }
                                
                                if (!string.IsNullOrEmpty(port))
                                    intStatusDict[port] = (name, status);
                            }
                        }
                    }
                    // Parse ARP table
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
                    // Parse MAC address table
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
                                // Fill Name, Status from intStatusDict if available
                                if (intStatusDict.TryGetValue(port, out var intInfo))
                                {
                                    res.Name = intInfo.Name;
                                    res.Status = intInfo.Status;
                                }
                                else
                                {
                                    res.Name = "";
                                    res.Status = string.IsNullOrWhiteSpace(res.Ip) ? "Disconnected" : "Connected";
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
            results = results
                .GroupBy(r => r.Mac)
                .Select(g => g.First())
                .OrderBy(r => ParseIp(r.Ip), new IpComparer())
                .ThenBy(r => ParsePort(r.Port), new PortComparer())
                .ToList();
            
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

            string switchAddress = SwitchInput.Text;

            PingReply reply = null;
            bool pingFailed = false;
            try
            {
                reply = await Task.Run(() => pingSender.Send(switchAddress, timeout, buffer, options));
            }
            catch (PingException)
            {
                pingFailed = true;
            }
            catch (SocketException)
            {
                pingFailed = true;
            }

            if (reply != null && reply.Status == IPStatus.Success)
            {
                IsSwitchValid = true;
                SubmitButton.Content = "Fetching info ...";
            }
            else
            {
                // Even if ping fails, try SSH
                IsSwitchValid = false;
                if (pingFailed)
                {
                    // Only show info, not error
                    SubmitButton.Content = "Ping failed, trying SSH ...";
                }
                else
                {
                    SubmitButton.Content = "Fetching info ...";
                }
            }

            try
            {
                var hostList = await FetchHostsFromSwitchAsync();
                var resultWindow = new ResultWindow(hostList);
                resultWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("SSH failed: " + ex.Message);
            }
            SubmitButton.Content = "Submit";
        }
        

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ValidateSwitch();
        }

        private class PortComparer : IComparer<IEnumerable<int>>
        {
            public int Compare(IEnumerable<int> x, IEnumerable<int> y)
            {
                var xList = x.ToList();
                var yList = y.ToList();
                int minLen = Math.Min(xList.Count, yList.Count);
                for (int i = 0; i < minLen; i++)
                {
                    int cmp = xList[i].CompareTo(yList[i]);
                    if (cmp != 0)
                        return cmp;
                }
                return xList.Count.CompareTo(yList.Count);
            }
        }
    }
}
