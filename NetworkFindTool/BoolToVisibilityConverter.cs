using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NetworkFindTool
{
    // Global converter to use in XAML: converts bool (is empty) to Visibility
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                bool isEmpty = System.Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                return isEmpty ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                return Visibility.Collapsed;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
