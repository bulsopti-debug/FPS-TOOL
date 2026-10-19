using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BulsOpti
{
    // Тема в духе нового сайта: почти чёрный фон, волосяные линии, белая главная кнопка, фиолетовый — только акцент
    static class Th
    {
        public static readonly Color Bg = C("#050506");
        public static readonly Color Raised = C("#0C0C0F");
        public static readonly Color Hover = C("#111115");
        public static readonly Color Line = C("#1B1B20");
        public static readonly Color LineStrong = C("#2A2A31");
        public static readonly Color Text = C("#F2F2F3");
        public static readonly Color Text2 = C("#A1A1AA");
        public static readonly Color Text3 = C("#6B6B74");
        public static readonly Color Accent = C("#8B5CF6");
        public static readonly Color Good = C("#86D19A");
        public static readonly Color Warn = C("#E7C27D");

        public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI");

        public static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }

        public static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        static ResourceDictionary styles;
        public static Style S(string key) { return (Style)styles[key]; }

        public static void Install(Application app)
        {
            styles = (ResourceDictionary)XamlReader.Parse(Xaml);
            app.Resources.MergedDictionaries.Add(styles);
        }

        public static ImageSource Res(string name)
        {
            var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("BulsOpti." + name);
            if (s == null) return null;
            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = s; bi.EndInit(); bi.Freeze();
            s.Dispose();
            return bi;
        }

        public static Stream ResStream(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream("BulsOpti." + name);
        }

        // ---------- Простые строители ----------
        public static TextBlock T(string text, double size, Color color, FontWeight weight)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = B(color), FontWeight = weight, TextWrapping = TextWrapping.Wrap, FontFamily = Font };
        }
        public static TextBlock T(string text, double size, Color color) { return T(text, size, color, FontWeights.Normal); }

        public static Border Hair(double top, double bottom)
        {
            return new Border { Height = 1, Background = B(Line), Margin = new Thickness(0, top, 0, bottom), SnapsToDevicePixels = true };
        }

        public static Button Btn(string style, string text, RoutedEventHandler click)
        {
            var b = new Button { Style = S(style), Content = text };
            if (click != null) b.Click += click;
            return b;
        }

        public static CheckBox Check(string text, bool on, RoutedEventHandler changed)
        {
            var c = new CheckBox { Style = S("Check"), IsChecked = on, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } };
            if (changed != null) { c.Checked += changed; c.Unchecked += changed; }
            return c;
        }

        public static Grid Cols(params GridLength[] widths)
        {
            var g = new Grid();
            foreach (var w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            return g;
        }

        public static GridLength Star { get { return new GridLength(1, GridUnitType.Star); } }
        public static GridLength Auto { get { return GridLength.Auto; } }
        public static GridLength Px(double v) { return new GridLength(v); }

        public static E At<E>(E el, int col) where E : UIElement { Grid.SetColumn(el, col); return el; }
        public static E Row<E>(E el, int row) where E : UIElement { Grid.SetRow(el, row); return el; }

        const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <Style x:Key='Primary' TargetType='Button'>
    <Setter Property='Foreground' Value='#050506'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Padding' Value='18,0'/>
    <Setter Property='MinHeight' Value='36'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid>
            <Border x:Name='ring' CornerRadius='9' Margin='-3' BorderThickness='2' BorderBrush='Transparent'/>
            <Border x:Name='bg' CornerRadius='6' Background='#F2F2F3' Padding='{TemplateBinding Padding}' MinHeight='{TemplateBinding MinHeight}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Background' Value='#FFFFFF'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='bg' Property='Background' Value='#D4D4D8'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='ring' Property='BorderBrush' Value='#8B5CF6'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='bg' Property='Background' Value='#17171B'/>
              <Setter Property='Foreground' Value='#55555E'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Secondary' TargetType='Button'>
    <Setter Property='Foreground' Value='#F2F2F3'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Padding' Value='16,0'/>
    <Setter Property='MinHeight' Value='36'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid>
            <Border x:Name='ring' CornerRadius='9' Margin='-3' BorderThickness='2' BorderBrush='Transparent'/>
            <Border x:Name='bg' CornerRadius='6' Background='Transparent' BorderBrush='#2A2A31' BorderThickness='1' Padding='{TemplateBinding Padding}' MinHeight='{TemplateBinding MinHeight}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Background' Value='#111115'/><Setter TargetName='bg' Property='BorderBrush' Value='#3A3A42'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='bg' Property='Background' Value='#17171B'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='ring' Property='BorderBrush' Value='#8B5CF6'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='bg' Property='BorderBrush' Value='#17171B'/>
              <Setter Property='Foreground' Value='#55555E'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Link' TargetType='Button'>
    <Setter Property='Foreground' Value='#A1A1AA'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='HorizontalAlignment' Value='Left'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bg' Background='Transparent' Padding='0,2'>
            <ContentPresenter VerticalAlignment='Center'>
              <ContentPresenter.ContentTemplate>
                <DataTemplate>
                  <TextBlock x:Name='t' Text='{Binding}' TextWrapping='Wrap'/>
                  <DataTemplate.Triggers>
                    <DataTrigger Binding='{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=Button}}' Value='True'>
                      <Setter TargetName='t' Property='TextDecorations' Value='Underline'/>
                    </DataTrigger>
                    <DataTrigger Binding='{Binding IsKeyboardFocused, RelativeSource={RelativeSource AncestorType=Button}}' Value='True'>
                      <Setter TargetName='t' Property='TextDecorations' Value='Underline'/>
                    </DataTrigger>
                  </DataTemplate.Triggers>
                </DataTemplate>
              </ContentPresenter.ContentTemplate>
            </ContentPresenter>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='#F2F2F3'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter Property='Foreground' Value='#F2F2F3'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Foreground' Value='#55555E'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Nav' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='#A1A1AA'/>
    <Setter Property='FontSize' Value='13.5'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Padding' Value='14,8,10,8'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Grid Background='Transparent'>
            <Border x:Name='bg' CornerRadius='6' Background='Transparent'/>
            <Border x:Name='bar' Width='2' HorizontalAlignment='Left' Margin='0,8' CornerRadius='1' Background='#8B5CF6' Visibility='Collapsed'/>
            <ContentPresenter Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/>
            <Border x:Name='ring' CornerRadius='6' BorderThickness='1.5' BorderBrush='Transparent'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='#F2F2F3'/><Setter TargetName='bg' Property='Background' Value='#0C0C0F'/></Trigger>
            <Trigger Property='IsChecked' Value='True'><Setter Property='Foreground' Value='#F2F2F3'/><Setter TargetName='bar' Property='Visibility' Value='Visible'/><Setter TargetName='bg' Property='Background' Value='#0C0C0F'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='ring' Property='BorderBrush' Value='#8B5CF6'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Tab' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='#A1A1AA'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Margin' Value='0,0,22,0'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Grid Background='Transparent'>
            <ContentPresenter Margin='0,4,0,10' VerticalAlignment='Center'/>
            <Border x:Name='line' Height='2' VerticalAlignment='Bottom' Background='Transparent'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='#F2F2F3'/></Trigger>
            <Trigger Property='IsChecked' Value='True'><Setter Property='Foreground' Value='#F2F2F3'/><Setter TargetName='line' Property='Background' Value='#F2F2F3'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='line' Property='Background' Value='#8B5CF6'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Check' TargetType='CheckBox'>
    <Setter Property='Foreground' Value='#F2F2F3'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Grid Background='Transparent'>
            <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
            <Border x:Name='box' Width='16' Height='16' CornerRadius='4' BorderThickness='1.5' BorderBrush='#3A3A42' Background='Transparent' VerticalAlignment='Top' Margin='0,2,10,0'>
              <Path x:Name='mark' Data='M2.4,6.6 L5.1,9.3 L10.4,3.7' Stroke='#050506' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' Visibility='Collapsed'/>
            </Border>
            <ContentPresenter Grid.Column='1' VerticalAlignment='Top'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='box' Property='BorderBrush' Value='#6B6B74'/></Trigger>
            <Trigger Property='IsChecked' Value='True'><Setter TargetName='box' Property='Background' Value='#F2F2F3'/><Setter TargetName='box' Property='BorderBrush' Value='#F2F2F3'/><Setter TargetName='mark' Property='Visibility' Value='Visible'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='box' Property='BorderBrush' Value='#8B5CF6'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Switch' TargetType='CheckBox'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Grid Width='36' Height='20' Background='Transparent'>
            <Border x:Name='ring' CornerRadius='13' Margin='-3' BorderThickness='2' BorderBrush='Transparent'/>
            <Border x:Name='track' CornerRadius='10' BorderThickness='1.5' BorderBrush='#3A3A42' Background='Transparent'/>
            <Ellipse x:Name='knob' Width='12' Height='12' Fill='#8A8A93' HorizontalAlignment='Left' Margin='4,0,0,0'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='track' Property='BorderBrush' Value='#6B6B74'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='track' Property='Background' Value='#8B5CF6'/><Setter TargetName='track' Property='BorderBrush' Value='#8B5CF6'/>
              <Setter TargetName='knob' Property='Fill' Value='#FFFFFF'/><Setter TargetName='knob' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='knob' Property='Margin' Value='0,0,4,0'/>
            </Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='ring' Property='BorderBrush' Value='#8B5CF6'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='10'/>
    <Setter Property='MinWidth' Value='10'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType='Thumb'>
                      <Border x:Name='t' Background='#26262C' CornerRadius='3' Width='6' Margin='0,2'/>
                      <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='t' Property='Background' Value='#3A3A42'/></Trigger></ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'><Setter Property='Visibility' Value='Collapsed'/></Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToolTip'>
          <Border Background='#111115' BorderBrush='#2A2A31' BorderThickness='1' CornerRadius='6' Padding='10,8' MaxWidth='420'>
            <ContentPresenter>
              <ContentPresenter.Resources>
                <Style TargetType='TextBlock'><Setter Property='TextWrapping' Value='Wrap'/><Setter Property='Foreground' Value='#D4D4D8'/><Setter Property='FontSize' Value='12'/></Style>
              </ContentPresenter.Resources>
            </ContentPresenter>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

</ResourceDictionary>";
    }

    // Линейка «Чертёж»: деления по числу рекомендаций и полоса под ними, как на знаке сайта
    class Ruler : FrameworkElement
    {
        public int Total, Done;
        public Ruler() { Height = 22; SnapsToDevicePixels = true; }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            if (w <= 0) return;
            int n = Math.Max(1, Total);
            var tick = new Pen(Th.B(Th.Text3), 1); tick.Freeze();
            for (int i = 0; i <= n; i++)
            {
                double x = Math.Round(i * (w - 1) / n) + 0.5;
                double h = (i == 0 || i == n || i % 4 == 0) ? 9 : 5;
                dc.DrawLine(tick, new Point(x, 0), new Point(x, h));
            }
            dc.DrawRectangle(Th.B(Th.Line), null, new Rect(0, 14, w, 4));
            double fill = Total > 0 ? w * Math.Min(Done, Total) / Total : 0;
            if (fill > 0) dc.DrawRectangle(Th.B(Th.Accent), null, new Rect(0, 14, fill, 4));
        }
    }
}
