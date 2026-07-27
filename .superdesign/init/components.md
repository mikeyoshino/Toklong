# Shared UI components

Framework: .NET MAUI 10 with XAML controls and a custom resource dictionary. There is no third-party UI component library.

## `src/Toklong.Mobile/Controls/BrandLockupView.xaml`

Brand mark and wordmark used on authentication and entry screens.

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentView
    x:Class="Toklong.Mobile.Controls.BrandLockupView"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
    <Grid
        ColumnDefinitions="52,Auto"
        ColumnSpacing="12"
        HorizontalOptions="Start">
        <Border
            WidthRequest="52"
            HeightRequest="52"
            Stroke="#32FFFFFF"
            StrokeThickness="1"
            StrokeShape="RoundRectangle 17">
            <Border.Background>
                <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                    <GradientStop Color="#3C8AF1" Offset="0" />
                    <GradientStop Color="#216ACB" Offset="0.72" />
                    <GradientStop Color="#5D43C4" Offset="1" />
                </LinearGradientBrush>
            </Border.Background>
            <Image
                Margin="10"
                Source="brand_mark.png"
                SemanticProperties.Description="โลโก้ TOKLONG" />
        </Border>
        <VerticalStackLayout
            Grid.Column="1"
            VerticalOptions="Center"
            Spacing="0">
            <Label
                FontAttributes="Bold"
                FontSize="18"
                CharacterSpacing="1.1"
                Text="TOKLONG"
                TextColor="{StaticResource Ink}" />
            <Label
                FontSize="11"
                Text="มั่นใจทุกดีล ใช้ TOKLONG"
                TextColor="{StaticResource Muted}" />
        </VerticalStackLayout>
    </Grid>
</ContentView>
```

## `src/Toklong.Mobile/Controls/FormLabelView.xaml`

Reusable form label with an optional red required indicator.

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentView
    x:Class="Toklong.Mobile.Controls.FormLabelView"
    x:Name="Root"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
    <HorizontalStackLayout Spacing="{StaticResource SpacingXs}">
        <Label
            Style="{StaticResource RefinedFormLabel}"
            Text="{Binding Text, Source={x:Reference Root}}" />
        <Label
            IsVisible="{Binding IsRequired, Source={x:Reference Root}}"
            Style="{StaticResource RefinedRequiredIndicator}"
            Text="*" />
    </HorizontalStackLayout>
</ContentView>
```

## `src/Toklong.Mobile/Controls/OtpCodeInput.xaml`

Six-cell OTP entry used by account verification screens.

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentView
    x:Class="Toklong.Mobile.Controls.OtpCodeInput"
    x:Name="Root"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
    <Grid HeightRequest="64">
        <Grid x:Name="DigitsLayout" ColumnDefinitions="*,*,*,*,*,*" ColumnSpacing="10" InputTransparent="True">
            <Grid RowDefinitions="48,3" RowSpacing="3">
                <Label x:Name="DigitOne" HorizontalTextAlignment="Center" VerticalTextAlignment="Center" FontAttributes="Bold" FontSize="25" />
                <BoxView x:Name="LineOne" Grid.Row="1" CornerRadius="2" />
            </Grid>
            <Grid Grid.Column="1" RowDefinitions="48,3" RowSpacing="3">
                <Label x:Name="DigitTwo" HorizontalTextAlignment="Center" VerticalTextAlignment="Center" FontAttributes="Bold" FontSize="25" />
                <BoxView x:Name="LineTwo" Grid.Row="1" CornerRadius="2" />
            </Grid>
            <Grid Grid.Column="2" RowDefinitions="48,3" RowSpacing="3">
                <Label x:Name="DigitThree" HorizontalTextAlignment="Center" VerticalTextAlignment="Center" FontAttributes="Bold" FontSize="25" />
                <BoxView x:Name="LineThree" Grid.Row="1" CornerRadius="2" />
            </Grid>
            <Grid Grid.Column="3" RowDefinitions="48,3" RowSpacing="3">
                <Label x:Name="DigitFour" HorizontalTextAlignment="Center" VerticalTextAlignment="Center" FontAttributes="Bold" FontSize="25" />
                <BoxView x:Name="LineFour" Grid.Row="1" CornerRadius="2" />
            </Grid>
            <Grid Grid.Column="4" RowDefinitions="48,3" RowSpacing="3">
                <Label x:Name="DigitFive" HorizontalTextAlignment="Center" VerticalTextAlignment="Center" FontAttributes="Bold" FontSize="25" />
                <BoxView x:Name="LineFive" Grid.Row="1" CornerRadius="2" />
            </Grid>
            <Grid Grid.Column="5" RowDefinitions="48,3" RowSpacing="3">
                <Label x:Name="DigitSix" HorizontalTextAlignment="Center" VerticalTextAlignment="Center" FontAttributes="Bold" FontSize="25" />
                <BoxView x:Name="LineSix" Grid.Row="1" CornerRadius="2" />
            </Grid>
        </Grid>
        <Entry
            x:Name="CodeEntry"
            Style="{StaticResource RefinedEntry}"
            AutomationId="OtpCodeEntry"
            BackgroundColor="Transparent"
            CursorPosition="0"
            HorizontalTextAlignment="Center"
            IsSpellCheckEnabled="False"
            IsTextPredictionEnabled="False"
            Keyboard="Numeric"
            MaxLength="6"
            Opacity="0.01"
            SemanticProperties.Description="รหัสยืนยัน 6 หลัก"
            TextChanged="OnCodeEntryTextChanged"
            HandlerChanged="OnCodeEntryHandlerChanged"
            TextColor="Transparent" />
    </Grid>
</ContentView>
```

## `src/Toklong.Mobile/Controls/ThaiMobilePhoneEntry.cs`

Custom Entry that displays and normalizes Thai mobile numbers. It inherits all visuals from `RefinedEntry`; its implementation is behavioral rather than visual.
