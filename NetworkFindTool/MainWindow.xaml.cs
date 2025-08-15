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

namespace NetworkFindTool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /*Todo 
            1. oznamit uzivateli ze probiha pokus o ping 
            2. oznamit uzivateli ze se ping povedl a ze se pokousi ziskat to info
         
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
            var ipv4Regex = new Regex(@"^(\d{1,3}\.){3}\d{1,3}$");
            return ipv4Regex.IsMatch(input);
        }
        static bool IsHostname(string input)
        {
            // Basic hostname pattern
            var hostnameRegex = new Regex(@"^([a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$");
            return hostnameRegex.IsMatch(input);
        }
        public void ValidateSwitch()
        {
            string text = SwitchInput.Text;
            if (IsIPv4(text) || IsHostname(text))
            {
                IsSwitchInputValid = true;
                IsServerUp();
            }
            else
            {
                IsSwitchInputValid = false;
                MessageBox.Show("Invalid Switch info format 💀","User error", MessageBoxButton.OK,MessageBoxImage.Error);
            }
        }

        public void IsServerUp()
        {
            Ping pingSender = new Ping();
            PingOptions options = new PingOptions();

            options.DontFragment = true;

            string data = "U up man?";
            byte[] buffer = Encoding.ASCII.GetBytes(data);
            int timeout = 120;
            PingReply reply = pingSender.Send(SwitchInput.Text, timeout, buffer, options);
            // 1
            if (reply.Status == IPStatus.Success)
            {
                IsSwitchValid = true;
            }
            else
            {
                MessageBox.Show()
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ValidateSwitch();
        }
    }
}
