using System.Xml.Linq;

namespace Toklong.Mobile.Core.Tests;

public sealed class EmailChangeLayoutTests
{
    private static readonly XNamespace Maui =
        "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2009/xaml";

    [Fact]
    public void Account_has_confirmed_email_edit_or_resume_entry_and_no_direct_save()
    {
        var account = LoadPage("AccountPage.xaml");
        var labels = account
            .Descendants(Maui + "Label")
            .Select(label => AttributeValue(label, "Text"))
            .ToArray();

        Assert.Contains("ข้อมูลติดต่อ", labels);
        Assert.Contains(
            account.Descendants(),
            element =>
                AttributeValue(element, "Command") ==
                "{Binding OpenEmailChangeCommand}");
        Assert.Contains(
            account.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding Email}" &&
                AttributeValue(
                    label,
                    "SemanticProperties.Description") ==
                "{Binding EmailSemanticDescription}");
        Assert.Contains(
            account.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding EmailStatus}");
        Assert.Contains(
            account.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "Text") ==
                "{Binding EmailActionText}" &&
                AttributeValue(button, "Command") ==
                "{Binding OpenEmailChangeCommand}");
        Assert.DoesNotContain(
            account.Descendants(Maui + "Entry"),
            entry =>
                AttributeValue(entry, "Text") ==
                "{Binding Email}");
        Assert.DoesNotContain(
            account.Descendants(),
            element =>
                AttributeValue(element, "Command") ==
                "{Binding SaveEmailCommand}");
        Assert.DoesNotContain("บันทึกอีเมล", labels);
    }

    [Fact]
    public void Request_email_is_scroll_safe_and_has_one_primary_action()
    {
        var request = LoadPage("ChangeEmailPage.xaml");
        var scroll = Assert.Single(
            request.Descendants(Maui + "ScrollView"));
        var primaryButtons = request
            .Descendants(Maui + "Button")
            .Where(button =>
                AttributeValue(button, "Style") ==
                "{StaticResource RefinedPrimaryButton}")
            .ToArray();
        var emailEntry = request
            .Descendants(Maui + "Entry")
            .Single(entry =>
                AttributeValue(entry, "Text") ==
                "{Binding Email, Mode=TwoWay}");

        Assert.Equal(
            "SoftInput",
            AttributeValue(scroll, "SafeAreaEdges"));
        Assert.Contains(
            scroll.Descendants(Maui + "VerticalStackLayout"),
            layout =>
                AttributeValue(layout, "Style") ==
                "{StaticResource RefinedScreenContent}");
        Assert.Contains(
            request.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "ขั้นที่ 1 จาก 2");
        Assert.Single(primaryButtons);
        Assert.Equal(
            "{Binding SubmitCommand}",
            AttributeValue(primaryButtons[0], "Command"));
        Assert.Equal(
            "ส่งรหัสยืนยัน",
            AttributeValue(primaryButtons[0], "Text"));
        Assert.Equal(
            "Email",
            AttributeValue(emailEntry, "Keyboard"));
        Assert.Equal(
            "254",
            AttributeValue(emailEntry, "MaxLength"));
        Assert.Contains(
            request.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding EmailError}" &&
                AttributeValue(label, "IsVisible") ==
                "{Binding HasEmailError}" &&
                AttributeValue(label, "Style") ==
                "{StaticResource RefinedValidationText}");
    }

    [Fact]
    public void Verify_email_is_scroll_safe_and_uses_one_accessible_code_input()
    {
        var verify = LoadPage("VerifyEmailChangePage.xaml");
        var scroll = Assert.Single(
            verify.Descendants(Maui + "ScrollView"));
        var codeInput = Assert.Single(
            verify.Descendants(),
            element =>
                element.Name.LocalName ==
                "OtpCodeInput");
        var primaryButtons = verify
            .Descendants(Maui + "Button")
            .Where(button =>
                AttributeValue(button, "Style") ==
                "{StaticResource RefinedPrimaryButton}")
            .ToArray();
        var maskedDestination = verify
            .Descendants()
            .Single(element =>
                AttributeValue(element, "AutomationId") ==
                "MaskedEmailDestination");
        var resend = verify
            .Descendants(Maui + "Button")
            .Single(button =>
                AttributeValue(button, "Command") ==
                "{Binding ResendCommand}");

        Assert.Equal(
            "SoftInput",
            AttributeValue(scroll, "SafeAreaEdges"));
        Assert.Contains(
            scroll.Descendants(Maui + "VerticalStackLayout"),
            layout =>
                AttributeValue(layout, "Style") ==
                "{StaticResource RefinedScreenContent}");
        Assert.Contains(
            verify.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "ขั้นที่ 2 จาก 2");
        Assert.Contains(
            verify.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "ยืนยันอีเมลใหม่");
        Assert.Single(primaryButtons);
        Assert.Equal(
            "{Binding ConfirmCommand}",
            AttributeValue(primaryButtons[0], "Command"));
        Assert.Equal(
            "{Binding Code, Mode=TwoWay}",
            AttributeValue(codeInput, "Code"));
        Assert.Null(
            AttributeValue(
                codeInput,
                "SemanticProperties.Description"));
        Assert.Equal(
            "{Binding MaskedEmailSemanticDescription}",
            AttributeValue(
                maskedDestination,
                "SemanticProperties.Description"));
        Assert.All(
            maskedDestination.Descendants(Maui + "Label"),
            label => Assert.Equal(
                "False",
                AttributeValue(
                    label,
                    "AutomationProperties.IsInAccessibleTree")));
        Assert.Equal(
            "{Binding ResendSemanticDescription}",
            AttributeValue(
                resend,
                "SemanticProperties.Description"));
        Assert.Equal(
            "{Binding CanResend}",
            AttributeValue(resend, "IsEnabled"));
        Assert.Equal(
            "{Binding ResendButtonText}",
            AttributeValue(resend, "Text"));
    }

    [Fact]
    public void Otp_code_control_exposes_only_its_numeric_entry()
    {
        var control = LoadUi(
            "Controls",
            "OtpCodeInput.xaml");
        var visibleDigits = control
            .Descendants(Maui + "Grid")
            .Single(grid =>
                AttributeValue(grid, Xaml + "Name") ==
                "DigitsLayout");
        var codeEntry = Assert.Single(
            control.Descendants(Maui + "Entry"));

        Assert.Equal(
            "False",
            AttributeValue(
                visibleDigits,
                "AutomationProperties.IsInAccessibleTree"));
        var digitLabels =
            visibleDigits.Descendants(Maui + "Label");
        Assert.Equal(6, digitLabels.Count());
        Assert.All(
            digitLabels,
            label => Assert.Equal(
                "False",
                AttributeValue(
                    label,
                    "AutomationProperties.IsInAccessibleTree")));
        Assert.Equal(
            "รหัสยืนยัน 6 หลัก",
            AttributeValue(
                codeEntry,
                "SemanticProperties.Description"));
        Assert.Equal(
            "OtpCodeEntry",
            AttributeValue(codeEntry, "AutomationId"));
    }

    [Fact]
    public void Email_change_routes_services_and_server_clock_are_registered()
    {
        var shell = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Ui",
            "AppShell.xaml.cs"));
        var program = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Ui",
            "MauiProgram.cs"));

        Assert.Contains(
            "nameof(ChangeEmailPage)",
            shell);
        Assert.Contains(
            "nameof(VerifyEmailChangePage)",
            shell);
        Assert.Contains(
            "AddTransient<ChangeEmailViewModel>()",
            program);
        Assert.Contains(
            "AddTransient<VerifyEmailChangeViewModel>()",
            program);
        Assert.Contains(
            "AddTransient<ChangeEmailPage>()",
            program);
        Assert.Contains(
            "AddTransient<VerifyEmailChangePage>()",
            program);
        Assert.Contains(
            "AddSingleton(TimeProvider.System)",
            program);
    }

    private static XDocument LoadPage(string fileName) =>
        LoadUi("Pages", fileName);

    private static XDocument LoadUi(
        params string[] segments) =>
        XDocument.Load(Path.Combine(
            [AppContext.BaseDirectory, "Ui", .. segments]));

    private static string? AttributeValue(
        XElement element,
        string name) =>
        element.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName == name)
            ?.Value;

    private static string? AttributeValue(
        XElement element,
        XName name) =>
        element.Attribute(name)?.Value;
}
