using Renci.SshNet;
using Renci.SshNet.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net; // DNS resolve
using System.Text; // StringBuilder
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace NetworkFindTool
{
    public partial class DiscoveryWindow : Window
    {
        public class DiscoveredDevice
        {
            public string Ip { get; set; }
            public string Name { get; set; }
        }

        public string SelectedIp { get; private set; }
        private bool _discovering;

        public DiscoveryWindow()
        {
            InitializeComponent();
            var creds = CredentialStore.Load();
            UsernameTextBox.Text = creds.Username;
            PasswordBox.Password = creds.Password;
        }

        private async void DiscoverButton_Click(object sender, RoutedEventArgs e)
        {
            if (_discovering) return;
            _discovering = true;

            DevicesListBox.ItemsSource = null;
            StatusText.Text = "Discovering ...";
            DiscoverButton.IsEnabled = false;

            string start = StartAddressTextBox.Text?.Trim();
            string user = UsernameTextBox.Text?.Trim();
            string pass = PasswordBox.Password;
            string enablePass = null;
            try { enablePass = EnablePasswordBox.Password; } catch { }

            if (string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(user))
            {
                MessageBox.Show("Please enter start address and username.");
                StatusText.Text = "";
                DiscoverButton.IsEnabled = true;
                _discovering = false;
                return;
            }

            try
            {
                var devices = await Task.Run(() => DiscoverTopology(start, user, pass, enablePass));
                DevicesListBox.ItemsSource = devices
                    .GroupBy(d => d.Ip)
                    .Select(g => g.First())
                    .OrderBy(d => d.Ip)
                    .ToList();
                StatusText.Text = $"Found {((System.Collections.IList)DevicesListBox.ItemsSource).Count} devices";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Discovery failed: " + ex.Message);
                StatusText.Text = "";
            }
            finally
            {
                DiscoverButton.IsEnabled = true;
                _discovering = false;
            }
        }

        private void UseSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (DevicesListBox.SelectedItem is DiscoveredDevice dev)
            {
                SelectedIp = dev.Ip;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Select a device first.");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void DevicesListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            UseSelectedButton_Click(sender, e);
        }

        private List<DiscoveredDevice> DiscoverTopology(string startIp, string username, string password, string enablePassword)
        {
            var devices = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var enqueued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();

            queue.Enqueue(startIp);
            enqueued.Add(startIp);

            while (queue.Count > 0)
            {
                var ip = queue.Dequeue();
                if (visited.Contains(ip)) continue;

                try
                {
                    using var client = new SshClient(ip, username, password);
                    client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
                    client.Connect();

                    using var shell = OpenShell(client);

                    EnsurePrompt(shell, TimeSpan.FromSeconds(8));
                    TryEnable(shell, string.IsNullOrEmpty(enablePassword) ? password : enablePassword, TimeSpan.FromSeconds(8));
                    RunOnShell(shell, "terminal length 0", TimeSpan.FromSeconds(5));
                    RunOnShell(shell, "terminal width 511", TimeSpan.FromSeconds(5));
                    RunOnShell(shell, "no page", TimeSpan.FromSeconds(5));
                    RunOnShell(shell, "set cli pagination off", TimeSpan.FromSeconds(5));
                    RunOnShell(shell, "screen-length disable", TimeSpan.FromSeconds(5));
                    RunOnShell(shell, "screen-length 0 temporary", TimeSpan.FromSeconds(5));

                    string name = GetDeviceName(shell);

                    visited.Add(ip);
                    devices[ip] = new DiscoveredDevice { Ip = ip, Name = name };

                    var neighbors = new List<(string ip, string name)>();

                    var cdpDetail = RunOnShell(shell, "show cdp neighbors detail", TimeSpan.FromSeconds(30));
                    if (!string.IsNullOrWhiteSpace(cdpDetail))
                        neighbors.AddRange(ParseCdpNeighbors(cdpDetail));

                    if (neighbors.All(n => string.IsNullOrWhiteSpace(n.ip)))
                    {
                        var cdpSummary = RunOnShell(shell, "show cdp neighbors", TimeSpan.FromSeconds(12));
                        var ids = ParseCdpSummaryDeviceIds(cdpSummary).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        foreach (var id in ids)
                        {
                            if (string.IsNullOrWhiteSpace(id)) continue;
                            var entry = RunOnShell(shell, $"show cdp entry {id}", TimeSpan.FromSeconds(15));
                            neighbors.AddRange(ParseCdpNeighbors(entry));
                        }
                    }

                    var lldpDetail = RunOnShell(shell, "show lldp neighbors detail", TimeSpan.FromSeconds(30));
                    if (!string.IsNullOrWhiteSpace(lldpDetail))
                        neighbors.AddRange(ParseLldpNeighbors(lldpDetail));
                    if (neighbors.All(n => string.IsNullOrWhiteSpace(n.ip)))
                    {
                        var lldpHp = RunOnShell(shell, "show lldp info remote-device detail", TimeSpan.FromSeconds(30));
                        if (!string.IsNullOrWhiteSpace(lldpHp))
                            neighbors.AddRange(ParseLldpNeighbors(lldpHp));
                        var lldpHuawei = RunOnShell(shell, "display lldp neighbor-information verbose", TimeSpan.FromSeconds(30));
                        if (!string.IsNullOrWhiteSpace(lldpHuawei))
                            neighbors.AddRange(ParseLldpNeighbors(lldpHuawei));
                    }

                    // DNS resolve fallback when only name is present
                    var resolved = new List<(string ip, string name)>();
                    foreach (var n in neighbors)
                    {
                        if (!string.IsNullOrWhiteSpace(n.ip))
                        {
                            resolved.Add(n);
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(n.name))
                        {
                            var dnsIp = ResolveToIPv4(n.name);
                            if (!string.IsNullOrWhiteSpace(dnsIp))
                                resolved.Add((dnsIp, n.name));
                        }
                    }

                    foreach (var n in resolved)
                    {
                        if (string.IsNullOrWhiteSpace(n.ip)) continue;
                        if (IPMatchSelf(n.ip, ip)) continue; // avoid re-adding self

                        if (!devices.ContainsKey(n.ip))
                            devices[n.ip] = new DiscoveredDevice { Ip = n.ip, Name = n.name };

                        if (!visited.Contains(n.ip) && !enqueued.Contains(n.ip))
                        {
                            queue.Enqueue(n.ip);
                            enqueued.Add(n.ip);
                        }
                    }

                    shell.Close();
                    client.Disconnect();
                }
                catch
                {
                    // Skip unreachable/unauthorized devices and continue
                }
            }

            return devices.Values.ToList();
        }

        private static bool IPMatchSelf(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveToIPv4(string host)
        {
            try
            {
                var addrs = Dns.GetHostAddresses(host);
                var ip = addrs.FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                return ip?.ToString();
            }
            catch { return null; }
        }

        private static ShellStream OpenShell(SshClient client)
        {
            var modes = new Dictionary<TerminalModes, uint>
            {
                { TerminalModes.ECHO, 0 }
            };
            return client.CreateShellStream("xterm", 80, 24, 800, 600, 1024 * 16, modes);
        }

        private static void EnsurePrompt(ShellStream shell, TimeSpan timeout)
        {
            var end = DateTime.UtcNow + timeout;
            var sb = new StringBuilder();

            shell.Write("\n");
            while (DateTime.UtcNow < end)
            {
                System.Threading.Thread.Sleep(50);
                while (shell.DataAvailable)
                {
                    var chunk = shell.Read();
                    sb.Append(chunk);
                }

                var lastLine = GetLastNonEmptyLine(sb.ToString());
                if (LooksLikePrompt(lastLine)) return;
            }
        }

        private static void TryEnable(ShellStream shell, string password, TimeSpan timeout)
        {
            var end = DateTime.UtcNow + timeout;
            var sb = new StringBuilder();

            shell.Write("\n");
            System.Threading.Thread.Sleep(100);
            while (shell.DataAvailable) sb.Append(shell.Read());
            var last = GetLastNonEmptyLine(sb.ToString());
            if (string.IsNullOrEmpty(last) || last.TrimEnd().EndsWith("#")) return;

            sb.Clear();
            while (shell.DataAvailable) shell.Read();
            shell.WriteLine("enable");

            while (DateTime.UtcNow < end)
            {
                System.Threading.Thread.Sleep(50);
                while (shell.DataAvailable)
                {
                    var chunk = shell.Read();
                    sb.Append(chunk);
                }
                var text = sb.ToString();
                var lastLine = GetLastNonEmptyLine(text);
                if (LooksLikePrompt(lastLine)) return;
                if (text.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    while (shell.DataAvailable) shell.Read();
                    shell.WriteLine(password ?? string.Empty);

                    var end2 = DateTime.UtcNow + TimeSpan.FromSeconds(6);
                    var sb2 = new StringBuilder();
                    while (DateTime.UtcNow < end2)
                    {
                        System.Threading.Thread.Sleep(50);
                        while (shell.DataAvailable)
                        {
                            var chunk2 = shell.Read();
                            sb2.Append(chunk2);
                        }
                        var last2 = GetLastNonEmptyLine(sb2.ToString());
                        if (LooksLikePrompt(last2)) return;
                    }
                    return;
                }
            }
        }

        private static string RunOnShell(ShellStream shell, string command, TimeSpan timeout)
        {
            var end = DateTime.UtcNow + timeout;
            var sb = new StringBuilder();
            int moreKickCount = 0;

            while (shell.DataAvailable) shell.Read();
            shell.WriteLine(command);

            while (DateTime.UtcNow < end)
            {
                System.Threading.Thread.Sleep(30);
                while (shell.DataAvailable)
                {
                    var chunk = shell.Read();
                    sb.Append(chunk);
                }

                var text = sb.ToString();

                if (ContainsMorePrompt(text))
                {
                    if (moreKickCount++ < 200)
                    {
                        shell.Write(" ");
                        sb.Replace("--More--", string.Empty)
                          .Replace("<--More-->", string.Empty)
                          .Replace("\b", string.Empty)
                          .Replace("\x08", string.Empty);
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }

                var lastLine = GetLastNonEmptyLine(text);
                if (LooksLikePrompt(lastLine))
                {
                    var lines = text.Replace("\r", "").Split('\n').ToList();
                    if (lines.Count > 0 && lines[0].Trim().Equals(command, StringComparison.OrdinalIgnoreCase))
                        lines.RemoveAt(0);
                    while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines.Last())) lines.RemoveAt(lines.Count - 1);
                    if (lines.Count > 0 && LooksLikePrompt(lines.Last())) lines.RemoveAt(lines.Count - 1);
                    return string.Join("\n", lines);
                }
            }

            return sb.ToString();
        }

        private static bool ContainsMorePrompt(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            // Common patterns across vendors
            if (text.Contains("--More--", StringComparison.OrdinalIgnoreCase)) return true;
            if (Regex.IsMatch(text, @"(?i)<-+\s*More\s*-+>")) return true;
            if (Regex.IsMatch(text, @"(?i)Press\s+(any|SPACE)\s+key\s+to\s+continue")) return true;
            if (Regex.IsMatch(text, @"(?i)More:\s*$")) return true; // HP/Aruba
            if (Regex.IsMatch(text, @"(?i)\[K\]")) return false; // ignore ANSI clear-line
            return false;
        }

        private static string GetDeviceName(ShellStream shell)
        {
            var candidates = new[]
            {
                "show hostname",
                "show system",
                "show version",
                "show running-config",
                "display current-configuration",
                "display version",
            };

            foreach (var cmd in candidates)
            {
                var text = RunOnShell(shell, cmd, TimeSpan.FromSeconds(12));
                var name = ExtractHostnameFromAny(text);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            var pipes = new[]
            {
                "show running-config | i ^hostname",
                "show running-config | include hostname",
                "show version | include uptime",
            };
            foreach (var cmd in pipes)
            {
                var text = RunOnShell(shell, cmd, TimeSpan.FromSeconds(10));
                var name = ExtractHostnameFromAny(text);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            return string.Empty;
        }

        private static bool LooksLikePrompt(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            line = line.TrimEnd();
            return line.EndsWith("#") || line.EndsWith(">");
        }

        private static string GetLastNonEmptyLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var lines = text.Replace("\r", "").Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var l = lines[i].TrimEnd();
                if (!string.IsNullOrWhiteSpace(l))
                    return l;
            }
            return string.Empty;
        }

        // --- Parsing helpers ---

        private static string ExtractHostnameFromAny(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var m = Regex.Match(text, @"(?im)^\s*hostname\s+(?<n>\S+)\s*$");
            if (m.Success) return m.Groups["n"].Value;

            m = Regex.Match(text, @"(?im)^\s*sysname\s+(?<n>\S+)\s*$");
            if (m.Success) return m.Groups["n"].Value;

            m = Regex.Match(text, @"(?im)^\s*Host(?:\s*Name)?\s*[:=]\s*(?<n>[A-Za-z0-9._\-]+)\s*$");
            if (m.Success) return m.Groups["n"].Value;

            m = Regex.Match(text, @"(?im)^(?<n>\S+)\s+uptime\s+is\b");
            if (m.Success) return m.Groups["n"].Value;

            m = Regex.Match(text, @"(?im)^\s*System\s+Name\s*[:=]\s*(?<n>[A-Za-z0-9._\-]+)\s*$");
            if (m.Success) return m.Groups["n"].Value;

            return string.Empty;
        }

        private static IEnumerable<(string ip, string name)> ParseCdpNeighbors(string output)
        {
            string currentName = null;
            string currentIp = null;
            var list = new List<(string ip, string name)>();
            foreach (var rawLine in (output ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var line = rawLine.Trim();

                var idMatch = Regex.Match(line, @"(?i)^Device\s*ID:\s*(?<id>.+)$");
                if (idMatch.Success)
                {
                    Flush();
                    currentName = idMatch.Groups["id"].Value.Trim();
                    continue;
                }

                // Accept 'IP address:' or 'IP Address:' or 'IPv4 Address:'
                var ipMatch1 = Regex.Match(line, @"(?i)^(IPv4\s+Address|IP\s+address|IP\s+Address):\s*(?<ip>\d+\.\d+\.\d+\.\d+)");
                if (ipMatch1.Success)
                {
                    currentIp = ipMatch1.Groups["ip"].Value.Trim();
                    Flush();
                    continue;
                }

                var mgmtIpMatch = Regex.Match(line, @"(?i)^Management\s+address:\s*(?<ip>\d+\.\d+\.\d+\.\d+)");
                if (mgmtIpMatch.Success)
                {
                    currentIp = mgmtIpMatch.Groups["ip"].Value.Trim();
                    Flush();
                    continue;
                }
            }

            Flush();
            return list;

            void Flush()
            {
                if (!string.IsNullOrEmpty(currentIp) || !string.IsNullOrEmpty(currentName))
                {
                    list.Add((currentIp, currentName));
                    currentIp = null; currentName = null;
                }
            }
        }

        private static IEnumerable<string> ParseCdpSummaryDeviceIds(string output)
        {
            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(output)) return ids;
            // Skip header, collect first column (Device ID)
            var lines = output.Replace("\r", "").Split('\n');
            bool headerSeen = false;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!headerSeen)
                {
                    if (Regex.IsMatch(line, @"(?i)^Device\s+ID\b"))
                        headerSeen = true;
                    continue;
                }
                // End if separator line appears
                if (Regex.IsMatch(line, @"^-+")) continue;
                var parts = Regex.Split(line, "\\s+");
                if (parts.Length > 0)
                {
                    var id = parts[0].Trim();
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
                }
            }
            return ids;
        }

        private static IEnumerable<(string ip, string name)> ParseLldpNeighbors(string output)
        {
            string currentName = null;
            string currentIp = null;
            var list = new List<(string ip, string name)>();
            foreach (var rawLine in (output ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var line = rawLine.Trim();

                var nameMatch = Regex.Match(line, @"(?i)^System\s+Name:\s*(?<id>.+)$");
                if (nameMatch.Success)
                {
                    Flush();
                    currentName = nameMatch.Groups["id"].Value.Trim();
                    continue;
                }

                var ipMatch = Regex.Match(line, @"(?i)^Management\s+Address:\s*(?<ip>\d+\.\d+\.\d+\.\d+)");
                if (ipMatch.Success)
                {
                    currentIp = ipMatch.Groups["ip"].Value.Trim();
                    Flush();
                    continue;
                }

                var ipMatchAlt = Regex.Match(line, @"(?i)^Mgmt\s+Address\s*:\s*(?<ip>\d+\.\d+\.\d+\.\d+)");
                if (ipMatchAlt.Success)
                {
                    currentIp = ipMatchAlt.Groups["ip"].Value.Trim();
                    Flush();
                    continue;
                }
            }

            Flush();
            return list;

            void Flush()
            {
                if (!string.IsNullOrEmpty(currentIp) || !string.IsNullOrEmpty(currentName))
                {
                    list.Add((currentIp, currentName));
                    currentIp = null; currentName = null;
                }
            }
        }
    }
}