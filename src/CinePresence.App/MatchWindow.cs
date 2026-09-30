using System.Windows;
using System.Windows.Media.Animation;
using CinePresence.App.Services;
namespace CinePresence.App;
public sealed class MatchWindow : Window
{
    public MatchWindow(AppController controller, ParsedTitle input)
    {
        Title = "Change match · CinePresence"; Width = 490; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)FindResource(typeof(Window));
        var picker = new MatchPicker(controller, input) { Margin = new Thickness(24), MaxHeight = SystemParameters.WorkArea.Height - 110 };
        Content = picker; picker.Applied += (_, _) => DialogResult = true;
        Loaded += (_, _) => { if (SystemParameters.ClientAreaAnimation) picker.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))); };
        Closed += (_, _) => picker.Dispose();
    }
}
