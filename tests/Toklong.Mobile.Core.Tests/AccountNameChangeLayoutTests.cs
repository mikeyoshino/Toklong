using System.Xml.Linq;

namespace Toklong.Mobile.Core.Tests;

public sealed class AccountNameChangeLayoutTests
{
    private static readonly XNamespace Maui =
        "http://schemas.microsoft.com/dotnet/2021/maui";

    [Fact]
    public void Account_edit_is_inside_the_blue_profile_card_without_proactive_cooldown_copy()
    {
        var account = LoadPage("AccountPage.xaml");
        var edit = account.Descendants(Maui + "Button").Single(button =>
            Attribute(button, "Command") ==
            "{Binding OpenNameChangeCommand}");
        var profileCard = edit.Ancestors(Maui + "Border").Last();
        var profileGradient = Assert.Single(
            profileCard.Descendants(Maui + "LinearGradientBrush"));
        var allCopy = string.Join(
            " ",
            account.Descendants()
                .SelectMany(element => element.Attributes())
                .Select(attribute => attribute.Value));

        Assert.Contains("#3C8AF1", profileGradient.ToString());
        Assert.Equal("แก้ไข", Attribute(edit, "Text"));
        Assert.Equal("44", Attribute(edit, "MinimumHeightRequest"));
        Assert.DoesNotContain("เปลี่ยนได้อีกครั้ง", allCopy);
        Assert.DoesNotContain("ทุก 2 เดือน", allCopy);
        Assert.DoesNotContain("NextAllowedAt", allCopy);
    }

    [Fact]
    public void Name_form_has_separate_accessible_fields_and_one_primary_action()
    {
        var page = LoadPage("ChangeNamePage.xaml");
        var entries = page.Descendants(Maui + "Entry").ToArray();
        var primary = page.Descendants(Maui + "Button")
            .Where(button => Attribute(button, "Style") ==
                "{StaticResource LedgerPrimaryButton}")
            .ToArray();

        Assert.Contains(entries, entry =>
            Attribute(entry, "Text") ==
            "{Binding FirstName, Mode=TwoWay}" &&
            Attribute(entry, "SemanticProperties.Description") == "ชื่อ");
        Assert.Contains(entries, entry =>
            Attribute(entry, "Text") ==
            "{Binding LastName, Mode=TwoWay}" &&
            Attribute(entry, "SemanticProperties.Description") == "นามสกุล");
        Assert.Single(primary);
        Assert.Equal(
            "{Binding SubmitCommand}",
            Attribute(primary[0], "Command"));
        Assert.Contains(page.Descendants(Maui + "Label"), label =>
            Attribute(label, "Text") == "ขั้นตอน 1 จาก 2");
    }

    [Fact]
    public void Verification_composes_the_shared_otp_form_without_a_duplicate_input()
    {
        var page = LoadPage("VerifyNameChangePage.xaml");
        var form = Assert.Single(page.Descendants(), element =>
            element.Name.LocalName == "OtpVerificationFormView");

        Assert.DoesNotContain(page.Descendants(), element =>
            element.Name.LocalName == "OtpCodeInput");
        Assert.Equal(
            "{Binding Code, Mode=TwoWay}",
            Attribute(form, "Code"));
        Assert.Equal(
            "{Binding ConfirmCommand}",
            Attribute(form, "ConfirmCommand"));
        Assert.Equal(
            "ยืนยันและบันทึก",
            Attribute(form, "ConfirmText"));
        Assert.Contains(page.Descendants(Maui + "Label"), label =>
            Attribute(label, "Text") == "ขั้นตอน 2 จาก 2");
    }

    [Fact]
    public void Verification_primary_actions_are_bound_to_disjoint_view_model_states()
    {
        var page = LoadPage("VerifyNameChangePage.xaml");
        var form = Assert.Single(page.Descendants(), element =>
            element.Name.LocalName == "OtpVerificationFormView");
        var newRequest = page.Descendants(Maui + "Button").Single(button =>
            Attribute(button, "Command") ==
            "{Binding StartNewRequestCommand}");
        var accountReturn = page.Descendants(Maui + "Button").Single(button =>
            Attribute(button, "Command") ==
            "{Binding ReturnToAccountCommand}");

        Assert.Equal(
            "{Binding CanUseChallenge}",
            Attribute(form, "IsConfirmVisible"));
        Assert.Equal(
            "{Binding RequiresNewRequest}",
            Attribute(newRequest, "IsVisible"));
        Assert.Equal(
            "{Binding CanReturnToAccount}",
            Attribute(accountReturn, "IsVisible"));
    }

    [Fact]
    public void Name_pages_present_modal_notice_fields_and_release_reset_subscriptions_when_removed()
    {
        foreach (var file in new[]
                 {
                     "ChangeNamePage.xaml.cs",
                     "VerifyNameChangePage.xaml.cs"
                 })
        {
            var source = LoadUi("Pages", file);

            Assert.Contains("DisplayAlertAsync(", source);
            Assert.Contains("notice.Title", source);
            Assert.Contains("notice.Message", source);
            Assert.Contains("notice.AcceptText", source);
            Assert.Contains("OnParentSet", source);
            Assert.Contains("viewModel.Dispose();", source);
        }
    }

    [Fact]
    public void Name_change_routes_services_lifetimes_and_accessibility_bridges_are_registered()
    {
        var shell = LoadUi("AppShell.xaml.cs");
        var program = LoadUi("MauiProgram.cs");

        Assert.Contains("nameof(ChangeNamePage)", shell);
        Assert.Contains("nameof(VerifyNameChangePage)", shell);
        Assert.Contains("AddTransient<ChangeNameViewModel>()", program);
        Assert.Contains("AddTransient<VerifyNameChangeViewModel>()", program);
        Assert.Contains("AddTransient<ChangeNamePage>()", program);
        Assert.Contains("AddTransient<VerifyNameChangePage>()", program);
        Assert.Contains("AccountNameChangeCompletionState", program);

        var verificationPage = LoadUi(
            "Pages",
            "VerifyNameChangePage.xaml.cs");
        Assert.Contains(
            "await viewModel.LoadPendingAfterResetAsync();",
            verificationPage);

        foreach (var file in new[]
                 {
                     "ChangeNamePage.xaml.cs",
                     "VerifyNameChangePage.xaml.cs"
                 })
        {
            var source = LoadUi("Pages", file);
            Assert.Contains("viewModel.Activate();", source);
            Assert.Contains("viewModel.Deactivate();", source);
            Assert.Contains("SemanticScreenReader.Announce", source);
            Assert.Contains("ScrollToAsync", source);
        }
    }

    private static XDocument LoadPage(string fileName) =>
        XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Ui",
            "Pages",
            fileName));

    private static string LoadUi(params string[] segments) =>
        File.ReadAllText(Path.Combine(
            [AppContext.BaseDirectory, "Ui", .. segments]));

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == name)?.Value;
}
