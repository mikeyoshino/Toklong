# TOKLONG mobile theme

## Compact token summary

- Framework: .NET MAUI XAML; native iOS/Android controls with custom Border wrappers.
- Font: `NotoSansThai` throughout.
- Ink: `#101828`; secondary ink `#475467`; muted `#667085`.
- Brand blue: `#2B7FFF`; deep blue `#145FC7`; soft blue `#EEF7FF`.
- Surface: `#FFFFFF`; soft surface `#F6F9FC`; default page `#FBFDFF`.
- Border: `#E4EAF1`; amount border `#B8D9FF`.
- Danger: `#C52F4D` on `#FFF1F3`; success `#087C68` on `#EAFBF7`.
- Spacing scale: 4, 8, 12, 20, 28.
- Screen padding: 20 horizontal and top, 28 bottom.
- Input: 52px minimum, 14px radius; multiline: 112px minimum.
- Primary action: 52px minimum, 16px radius, bold 15px white on brand blue.
- Cards: white/translucent white, 20px radius, hairline neutral border.
- Type scale: helper 13, body/label 14, input 16, section title 18, page title 30.
- Current Create Offer background: radial pale-blue-to-white gradient.
- Target platform: mobile first; reference viewport approximately 690 × 1536 px at 2x density.

## Raw source

The complete source of truth is `src/Toklong.Mobile/App.xaml` (409 lines). The visual tokens and relevant component styles used by Create Offer are:

```xml
<Color x:Key="Ink">#101828</Color>
<Color x:Key="InkSoft">#475467</Color>
<Color x:Key="Muted">#667085</Color>
<Color x:Key="Line">#E4EAF1</Color>
<Color x:Key="Surface">#FFFFFF</Color>
<Color x:Key="SurfaceSoft">#F6F9FC</Color>
<Color x:Key="SurfaceBlue">#EEF7FF</Color>
<Color x:Key="BrandBlue">#2B7FFF</Color>
<Color x:Key="BrandBlueDeep">#145FC7</Color>
<Color x:Key="Mint">#65D6BF</Color>
<Color x:Key="Danger">#C52F4D</Color>
<Color x:Key="DangerSoft">#FFF1F3</Color>
<Color x:Key="Success">#087C68</Color>
<Color x:Key="SuccessSoft">#EAFBF7</Color>

<x:Double x:Key="SpacingXs">4</x:Double>
<x:Double x:Key="SpacingSm">8</x:Double>
<x:Double x:Key="SpacingMd">12</x:Double>
<x:Double x:Key="SpacingLg">20</x:Double>
<x:Double x:Key="SpacingXl">28</x:Double>
<x:Double x:Key="InputMinimumHeight">52</x:Double>
<x:Double x:Key="CompactControlMinimumHeight">44</x:Double>
<x:Double x:Key="MultilineInputMinimumHeight">112</x:Double>
<x:Double x:Key="PrimaryButtonMinimumHeight">52</x:Double>
<x:Double x:Key="SecondaryButtonMinimumHeight">48</x:Double>
<Thickness x:Key="RefinedScreenPadding">20,20,20,28</Thickness>

<Style TargetType="ContentPage">
    <Setter Property="BackgroundColor" Value="#FBFDFF" />
</Style>
<Style TargetType="Label">
    <Setter Property="FontFamily" Value="NotoSansThai" />
    <Setter Property="TextColor" Value="{StaticResource Ink}" />
    <Setter Property="FontSize" Value="14" />
</Style>
<Style x:Key="PageTitle" TargetType="Label">
    <Setter Property="FontSize" Value="30" />
    <Setter Property="FontAttributes" Value="Bold" />
    <Setter Property="TextColor" Value="{StaticResource Ink}" />
    <Setter Property="CharacterSpacing" Value="-0.2" />
</Style>
<Style x:Key="RefinedScreenContent" TargetType="VerticalStackLayout">
    <Setter Property="MaximumWidthRequest" Value="720" />
    <Setter Property="HorizontalOptions" Value="Fill" />
    <Setter Property="Padding" Value="{StaticResource RefinedScreenPadding}" />
    <Setter Property="Spacing" Value="{StaticResource SpacingXl}" />
</Style>
<Style x:Key="RefinedFormSection" TargetType="VerticalStackLayout">
    <Setter Property="Spacing" Value="{StaticResource SpacingSm}" />
</Style>
<Style x:Key="RefinedFormLabel" TargetType="Label">
    <Setter Property="FontFamily" Value="NotoSansThai" />
    <Setter Property="FontSize" Value="14" />
    <Setter Property="FontAttributes" Value="Bold" />
    <Setter Property="TextColor" Value="{StaticResource Ink}" />
</Style>
<Style x:Key="RefinedInputBorder" TargetType="Border">
    <Setter Property="BackgroundColor" Value="White" />
    <Setter Property="Stroke" Value="{StaticResource Line}" />
    <Setter Property="StrokeThickness" Value="1" />
    <Setter Property="MinimumHeightRequest" Value="{StaticResource InputMinimumHeight}" />
    <Setter Property="Padding" Value="16,0" />
    <Setter Property="StrokeShape" Value="RoundRectangle 14" />
</Style>
<Style x:Key="RefinedEntry" TargetType="Entry">
    <Setter Property="FontFamily" Value="NotoSansThai" />
    <Setter Property="FontSize" Value="16" />
    <Setter Property="BackgroundColor" Value="Transparent" />
    <Setter Property="TextColor" Value="{StaticResource Ink}" />
    <Setter Property="PlaceholderColor" Value="#98A2B3" />
</Style>
<Style x:Key="RefinedHelperText" TargetType="Label">
    <Setter Property="FontFamily" Value="NotoSansThai" />
    <Setter Property="FontSize" Value="13" />
    <Setter Property="TextColor" Value="{StaticResource Muted}" />
    <Setter Property="LineHeight" Value="1.35" />
</Style>
<Style x:Key="RefinedPrimaryButton" TargetType="Button">
    <Setter Property="FontFamily" Value="NotoSansThai" />
    <Setter Property="BackgroundColor" Value="{StaticResource BrandBlue}" />
    <Setter Property="TextColor" Value="White" />
    <Setter Property="FontAttributes" Value="Bold" />
    <Setter Property="FontSize" Value="15" />
    <Setter Property="MinimumHeightRequest" Value="{StaticResource PrimaryButtonMinimumHeight}" />
    <Setter Property="CornerRadius" Value="16" />
    <Setter Property="Padding" Value="18,12" />
</Style>
<Style x:Key="RefinedSecondaryActionButton" TargetType="Button">
    <Setter Property="FontFamily" Value="NotoSansThai" />
    <Setter Property="BackgroundColor" Value="{StaticResource SurfaceBlue}" />
    <Setter Property="TextColor" Value="{StaticResource BrandBlueDeep}" />
    <Setter Property="BorderColor" Value="#B8D9FF" />
    <Setter Property="BorderWidth" Value="1" />
    <Setter Property="FontAttributes" Value="Bold" />
    <Setter Property="FontSize" Value="15" />
    <Setter Property="MinimumHeightRequest" Value="52" />
    <Setter Property="CornerRadius" Value="16" />
</Style>
<Style x:Key="RefinedAmountBorder" TargetType="Border">
    <Setter Property="MinimumHeightRequest" Value="52" />
    <Setter Property="Padding" Value="16,0" />
    <Setter Property="BackgroundColor" Value="{StaticResource SurfaceBlue}" />
    <Setter Property="Stroke" Value="#B8D9FF" />
    <Setter Property="StrokeThickness" Value="1" />
    <Setter Property="StrokeShape" Value="RoundRectangle 14" />
</Style>
<Style x:Key="RefinedAmountEntry" TargetType="Entry">
    <Setter Property="FontFamily" Value="NotoSansThai" />
    <Setter Property="FontSize" Value="18" />
    <Setter Property="FontAttributes" Value="Bold" />
    <Setter Property="BackgroundColor" Value="Transparent" />
    <Setter Property="TextColor" Value="{StaticResource Ink}" />
</Style>
```
