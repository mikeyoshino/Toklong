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
        var success = account
            .Descendants()
            .Single(element =>
                AttributeValue(
                    element,
                    "AutomationId") ==
                "EmailChangeSuccessSummary");
        Assert.Equal(
            "{Binding HasSuccessMessage}",
            AttributeValue(success, "IsVisible"));
        Assert.Contains(
            success.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding SuccessMessage}");
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
            "{Binding SubmitButtonText}",
            AttributeValue(primaryButtons[0], "Text"));
        Assert.Equal(
            "{Binding SubmitSemanticDescription}",
            AttributeValue(
                primaryButtons[0],
                "SemanticProperties.Description"));
        Assert.Equal(
            "{Binding CanEditEmail}",
            AttributeValue(emailEntry, "IsEnabled"));
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
        var otpForm = Assert.Single(
            verify.Descendants(),
            element =>
                element.Name.LocalName ==
                "OtpVerificationFormView");
        var primaryButtons = verify
            .Descendants(Maui + "Button")
            .Where(button =>
                AttributeValue(button, "Style") ==
                "{StaticResource RefinedPrimaryButton}")
            .ToArray();
        var newRequest = primaryButtons.Single(button =>
            AttributeValue(button, "Command") ==
            "{Binding StartNewRequestCommand}");
        var returnToAccount = primaryButtons.Single(button =>
            AttributeValue(button, "Command") ==
            "{Binding ReturnToAccountCommand}");
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
        Assert.Equal(2, primaryButtons.Length);
        Assert.Equal(
            "{Binding CanUseChallenge}",
            AttributeValue(
                otpForm,
                "IsConfirmVisible"));
        Assert.Equal(
            "{Binding CanConfirm}",
            AttributeValue(otpForm, "CanConfirm"));
        Assert.Equal(
            "{Binding RequiresNewRequest}",
            AttributeValue(newRequest, "IsVisible"));
        Assert.Equal(
            "{Binding CanReturnToAccount}",
            AttributeValue(returnToAccount, "IsVisible"));
        Assert.Equal(
            "{Binding AccountReturnButtonText}",
            AttributeValue(returnToAccount, "Text"));
        Assert.Equal(
            "{Binding AccountReturnSemanticDescription}",
            AttributeValue(
                returnToAccount,
                "SemanticProperties.Description"));
        Assert.Equal(
            "ขอรหัสใหม่",
            AttributeValue(newRequest, "Text"));
        Assert.Equal(
            "{Binding Code, Mode=TwoWay}",
            AttributeValue(otpForm, "Code"));
        Assert.Equal(
            "ยืนยันอีเมลใหม่ด้วยรหัส 6 หลัก",
            AttributeValue(
                otpForm,
                "ConfirmSemanticDescription"));
        Assert.DoesNotContain(
            verify.Descendants(Maui + "Border"),
            border =>
                AttributeValue(border, "Style") ==
                "{StaticResource RefinedFormCard}");
        Assert.DoesNotContain(
            verify.Descendants(),
            element =>
                element.Name.LocalName ==
                "FormLabelView");
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
            "{Binding CanUseChallenge}",
            AttributeValue(resend, "IsVisible"));
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
        var controlHost = control
            .Descendants(Maui + "Grid")
            .First();

        Assert.Null(
            AttributeValue(
                controlHost,
                "HeightRequest"));
        Assert.Equal(
            "64",
            AttributeValue(
                controlHost,
                "MinimumHeightRequest"));
        Assert.All(
            visibleDigits
                .Elements(Maui + "Grid"),
            digit => Assert.Equal(
                "Auto,3",
                AttributeValue(
                    digit,
                    "RowDefinitions")));
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

    [Fact]
    public void Both_email_change_pages_bind_operations_to_page_lifetime()
    {
        foreach (var fileName in new[]
                 {
                     "ChangeEmailPage.xaml.cs",
                     "VerifyEmailChangePage.xaml.cs"
                 })
        {
            var source = File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory,
                "Ui",
                "Pages",
                fileName));

            Assert.Contains(
                "viewModel.Activate();",
                source);
            Assert.Contains(
                "viewModel.Deactivate();",
                source);
        }
    }

    [Fact]
    public void Verify_page_subscribes_before_activation_and_focuses_only_a_usable_challenge()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Ui",
            "Pages",
            "VerifyEmailChangePage.xaml.cs"));

        Assert.True(
            source.IndexOf(
                "ErrorPresented += OnErrorPresented",
                StringComparison.Ordinal) <
            source.IndexOf(
                "viewModel.Activate();",
                StringComparison.Ordinal));
        Assert.Contains(
            "if (viewModel.CanUseChallenge)",
            source);
    }

    [Fact]
    public void Email_change_errors_have_one_semantic_summary_and_page_focus_bridge()
    {
        foreach (var pageName in new[]
                 {
                     "ChangeEmailPage",
                     "VerifyEmailChangePage"
                 })
        {
            var page = LoadPage($"{pageName}.xaml");
            var summary = page
                .Descendants()
                .Single(element =>
                    AttributeValue(
                        element,
                        "AutomationId") ==
                    "EmailChangeErrorSummary");
            var source = File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory,
                "Ui",
                "Pages",
                $"{pageName}.xaml.cs"));

            Assert.Equal(
                "{Binding Message}",
                AttributeValue(
                    summary,
                    "SemanticProperties.Description"));
            Assert.All(
                summary.Descendants(Maui + "Label"),
                label => Assert.Equal(
                    "False",
                    AttributeValue(
                        label,
                        "AutomationProperties.IsInAccessibleTree")));
            Assert.Contains(
                "ErrorPresented += OnErrorPresented",
                source);
            Assert.Contains(
                "SemanticScreenReader.Announce",
                source);
            Assert.Contains(
                "ScrollToAsync",
                source);
            Assert.Contains(
                ".Focus()",
                source);
        }
    }

    [Fact]
    public void Login_and_email_verification_use_the_shared_otp_form()
    {
        var login = LoadPage("VerifyCodePage.xaml");
        var email = LoadPage("VerifyEmailChangePage.xaml");

        foreach (var page in new[] { login, email })
        {
            Assert.Single(
                page.Descendants(),
                element =>
                    element.Name.LocalName ==
                    "OtpVerificationFormView");
            Assert.DoesNotContain(
                page.Descendants(),
                element =>
                    element.Name.LocalName ==
                    "OtpCodeInput");
        }

        var loginForm = login.Descendants().Single(element =>
            element.Name.LocalName == "OtpVerificationFormView");
        Assert.Equal(
            "{Binding ConfirmCommand}",
            AttributeValue(loginForm, "ConfirmCommand"));
        Assert.Equal(
            "{Binding ConfirmButtonText}",
            AttributeValue(loginForm, "ConfirmText"));
        Assert.Equal(
            "{Binding HasDevelopmentHint}",
            AttributeValue(loginForm, "HasDevelopmentHint"));

        var emailForm = email.Descendants().Single(element =>
            element.Name.LocalName == "OtpVerificationFormView");
        Assert.Equal(
            "{Binding ConfirmCommand}",
            AttributeValue(emailForm, "ConfirmCommand"));
        Assert.Equal(
            "{Binding CanConfirm}",
            AttributeValue(emailForm, "CanConfirm"));
        Assert.Equal(
            "{Binding CanUseChallenge}",
            AttributeValue(
                emailForm,
                "IsConfirmVisible"));
        Assert.Null(
            AttributeValue(emailForm, "IsVisible"));
    }

    [Fact]
    public void Shared_otp_form_has_one_input_one_action_and_no_workflow_state()
    {
        var form = LoadUi("Controls", "OtpVerificationFormView.xaml");
        var codeInput = Assert.Single(
            form.Descendants(),
            element => element.Name.LocalName == "OtpCodeInput");
        var confirm = Assert.Single(form.Descendants(Maui + "Button"));
        var card = Assert.Single(
            form.Descendants(Maui + "Border"),
            border =>
                AttributeValue(border, "Style") ==
                "{StaticResource AuthFormCard}");
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Ui",
            "Controls",
            "OtpVerificationFormView.xaml.cs"));

        Assert.Equal(
            "{Binding Code, Source={x:Reference Root}, Mode=TwoWay}",
            AttributeValue(codeInput, "Code"));
        Assert.Equal(
            "{Binding ConfirmCommand, Source={x:Reference Root}}",
            AttributeValue(confirm, "Command"));
        Assert.Equal(
            "{Binding CanConfirm, Source={x:Reference Root}}",
            AttributeValue(confirm, "IsEnabled"));
        Assert.Equal(
            "{Binding IsConfirmVisible, Source={x:Reference Root}}",
            AttributeValue(confirm, "IsVisible"));
        Assert.Equal(
            "{Binding DisplayedConfirmText, Source={x:Reference Root}}",
            AttributeValue(confirm, "Text"));
        Assert.Contains(card, codeInput.Ancestors());
        Assert.Contains(card, confirm.Ancestors());
        Assert.DoesNotContain(
            form.Descendants(Maui + "Border"),
            border =>
                AttributeValue(border, "Style") ==
                "{StaticResource RefinedFormCard}");
        Assert.DoesNotContain("ViewModel", source);
        Assert.DoesNotContain("INavigation", source);
        Assert.DoesNotContain("Resend", source);
        Assert.DoesNotContain("TimeProvider", source);
    }

    [Fact]
    public void Account_and_email_verification_reference_only_declared_resources()
    {
        var app = LoadUi("App.xaml");
        var declaredKeys = app
            .Descendants()
            .Select(element => AttributeValue(element, "Key"))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);
        const string prefix = "{StaticResource ";

        foreach (var pageName in new[]
                 {
                     "AccountPage.xaml",
                     "VerifyEmailChangePage.xaml"
                 })
        {
            var missingKeys = LoadPage(pageName)
                .Descendants()
                .Attributes()
                .Select(attribute => attribute.Value)
                .Where(value =>
                    value.StartsWith(prefix, StringComparison.Ordinal) &&
                    value.EndsWith('}'))
                .Select(value => value[prefix.Length..^1])
                .Distinct(StringComparer.Ordinal)
                .Where(key => !declaredKeys.Contains(key))
                .ToArray();

            Assert.True(
                missingKeys.Length == 0,
                $"{pageName} references missing resources: " +
                string.Join(", ", missingKeys));
        }
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
