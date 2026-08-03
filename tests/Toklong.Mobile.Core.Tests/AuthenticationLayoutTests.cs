namespace Toklong.Mobile.Core.Tests;

public sealed class AuthenticationLayoutTests
{
    [Theory]
    [InlineData("WelcomePage.xaml")]
    [InlineData("SignInPage.xaml")]
    [InlineData("SignUpPage.xaml")]
    [InlineData("VerifyCodePage.xaml")]
    [InlineData("CompleteRegistrationPage.xaml")]
    public void Authentication_pages_use_the_shared_mist_background(
        string page)
    {
        var xaml = ReadPage(page);

        Assert.Contains(
            "{StaticResource CleanLedgerRootBackground}",
            xaml);
        Assert.DoesNotContain("RadialGradientBrush", xaml);
    }

    [Fact]
    public void Welcome_uses_centered_brand_and_removes_old_hero_art()
    {
        var xaml = ReadPage("WelcomePage.xaml");

        Assert.Contains("CenteredAuthBrandView", xaml);
        Assert.Contains("ซื้อขายออนไลน์ ง่ายขึ้น", xaml);
        Assert.DoesNotContain("ui_shield", xaml);
        Assert.DoesNotContain("ui_truck", xaml);
    }

    [Fact]
    public void SignIn_is_explicitly_phone_and_sms_first()
    {
        var xaml = ReadPage("SignInPage.xaml");

        Assert.Contains("เข้าสู่ระบบด้วยเบอร์มือถือ", xaml);
        Assert.Contains("ThaiMobilePhoneField", xaml);
        Assert.Contains("ส่งรหัสทาง SMS", xaml);
        Assert.DoesNotContain("+66", xaml);
    }

    [Fact]
    public void SignUp_collects_phone_only_before_sms()
    {
        var xaml = ReadPage("SignUpPage.xaml");

        Assert.Contains("สมัครด้วยเบอร์มือถือ", xaml);
        Assert.Contains("ThaiMobilePhoneField", xaml);
        Assert.DoesNotContain("ชื่อและนามสกุล", xaml);
        Assert.DoesNotContain("อีเมล", xaml);
    }

    [Fact]
    public void CompleteRegistration_collects_profile_after_verified_phone()
    {
        var xaml = ReadPage("CompleteRegistrationPage.xaml");

        Assert.Contains("ตั้งค่าบัญชีให้เสร็จ", xaml);
        Assert.Contains("Text=\"ชื่อ\"", xaml);
        Assert.Contains("Text=\"นามสกุล\"", xaml);
        Assert.Contains("Text=\"{Binding FirstName}\"", xaml);
        Assert.Contains("Text=\"{Binding LastName}\"", xaml);
        Assert.DoesNotContain("{Binding FullName}", xaml);
        Assert.Contains(
            "อีเมลสำหรับใบเสร็จและการคืนเงิน",
            xaml);
        Assert.Contains(
            "สร้างบัญชีและเริ่มใช้งาน",
            xaml);
    }

    private static string ReadPage(string fileName) =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Ui",
                "Pages",
                fileName));
}
