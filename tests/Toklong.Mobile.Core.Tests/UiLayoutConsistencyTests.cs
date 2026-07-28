using System.Xml.Linq;

namespace Toklong.Mobile.Core.Tests;

public sealed class UiLayoutConsistencyTests
{
    private static readonly XNamespace Maui = "http://schemas.microsoft.com/dotnet/2021/maui";

    [Fact]
    public void SharedFormTokens_KeepPhoneAndVerificationCodeAligned()
    {
        var resources = Load("Ui", "App.xaml");

        Assert.Equal("52", ResourceValue(resources, "InputMinimumHeight"));
        Assert.Equal("16", StyleSetterValue(resources, "RefinedEntry", "FontSize"));
        Assert.Equal(
            "{StaticResource InputMinimumHeight}",
            StyleSetterValue(resources, "RefinedInputBorder", "MinimumHeightRequest"));

        var signIn = Load("Ui", "Pages", "SignInPage.xaml");
        var sharedPhoneField = signIn
            .Descendants()
            .Single(element =>
                element.Name.LocalName ==
                "ThaiMobilePhoneField");
        Assert.Equal(
            "{Binding PhoneNumber, Mode=TwoWay}",
            AttributeValue(
                sharedPhoneField,
                "PhoneNumber"));

        var phoneField = Load(
            "Ui",
            "Controls",
            "ThaiMobilePhoneField.xaml");
        var phone = phoneField
            .Descendants()
            .Single(element =>
                element.Name.LocalName ==
                "ThaiMobilePhoneEntry");
        Assert.Equal("{StaticResource RefinedEntry}", AttributeValue(phone, "Style"));
        Assert.Equal(
            "{Binding PhoneNumber, Source={x:Reference Root}, Mode=TwoWay}",
            AttributeValue(phone, "PhoneNumber"));
        Assert.Null(phone.Attribute("Text"));
        Assert.Equal(
            "{StaticResource RefinedInputBorder}",
            AttributeValue(phone.Parent!.Parent!, "Style"));

        var verifyCode = Load("Ui", "Pages", "VerifyCodePage.xaml");
        var codeInput = verifyCode
            .Descendants()
            .Single(element => element.Name.LocalName == "OtpCodeInput");
        Assert.Equal(
            "{Binding Code, Mode=TwoWay}",
            AttributeValue(codeInput, "Code"));

        var otpControl = Load("Ui", "Controls", "OtpCodeInput.xaml");
        var codeEntry = otpControl.Descendants(Maui + "Entry").Single();
        Assert.Equal(
            "{StaticResource RefinedEntry}",
            AttributeValue(codeEntry, "Style"));
        Assert.Equal("6", AttributeValue(codeEntry, "MaxLength"));
        Assert.Equal("Numeric", AttributeValue(codeEntry, "Keyboard"));
        Assert.Equal("OtpCodeEntry", AttributeValue(codeEntry, "AutomationId"));
        Assert.Equal("0.01", AttributeValue(codeEntry, "Opacity"));
        Assert.Equal(6, otpControl.Descendants(Maui + "BoxView").Count());
        Assert.Null(codeEntry.Attribute("FontSize"));
    }

    [Fact]
    public void AuthenticationStartsAtWelcome_AndKeepsLoginAndSignupSeparate()
    {
        var shell = Load("Ui", "AppShell.xaml");
        var firstShellContent = shell
            .Descendants()
            .First(element => element.Name.LocalName == "ShellContent");
        Assert.Equal("welcome", AttributeValue(firstShellContent, "Route"));

        var welcome = Load("Ui", "Pages", "WelcomePage.xaml");
        var buttonLabels = welcome
            .Descendants(Maui + "Button")
            .Select(button => AttributeValue(button, "Text"))
            .ToArray();

        Assert.Contains("เข้าสู่ระบบ", buttonLabels);
        Assert.Contains("สมัครสมาชิก", buttonLabels);
        Assert.Contains(
            welcome.Descendants(),
            element =>
                element.Name.LocalName ==
                "CenteredAuthBrandView");
    }

    [Fact]
    public void AuthenticatedHome_UsesCenteredBrandAndBuyerFirstActions()
    {
        var home = Load(
            "Ui",
            "Pages",
            "AuthenticatedHomePage.xaml");
        var labels = home
            .Descendants(Maui + "Label")
            .Select(label => AttributeValue(label, "Text"))
            .ToArray();
        var buttons = home.Descendants(Maui + "Button").ToArray();

        Assert.Contains(
            home.Descendants(),
            element =>
                element.Name.LocalName ==
                "CenteredAuthBrandView");
        Assert.Contains("เริ่มดีลอย่างมั่นใจ", labels);
        Assert.Contains(
            "สร้างข้อเสนอซื้อ หรือจัดการรายการขาย",
            labels);
        Assert.Contains(
            buttons,
            button =>
                AttributeValue(button, "AutomationId") ==
                    "OpenBuyingHomeButton" &&
                AttributeValue(
                    button,
                    "SemanticProperties.Description") ==
                    "ซื้อ สร้างข้อเสนอ ตรวจรายละเอียด และติดตามรายการ");
        Assert.Contains(
            buttons,
            button =>
                AttributeValue(button, "AutomationId") ==
                    "OpenSellingHomeButton" &&
                AttributeValue(
                    button,
                    "SemanticProperties.Description") ==
                    "ขาย ตรวจข้อเสนอ ส่งสินค้า และติดตามยอดรับ");
        Assert.Contains(
            buttons,
            button =>
                AttributeValue(button, "AutomationId") ==
                    "OpenAllTransactionsButton" &&
                AttributeValue(button, "Text") ==
                    "รายการของฉัน");
        Assert.DoesNotContain("เข้าสู่ระบบ", labels);
        Assert.DoesNotContain("สมัครสมาชิก", labels);
        Assert.DoesNotContain("สร้างลิงก์ขาย", labels);
    }

    [Fact]
    public void Shell_RegistersAuthenticatedHomeOutsideTheMainTabBar()
    {
        var shell = Load("Ui", "AppShell.xaml");
        var home = shell
            .Descendants(Maui + "ShellContent")
            .Single(element =>
                AttributeValue(element, "Route") == "home");

        Assert.Equal(
            "{DataTemplate pages:AuthenticatedHomePage}",
            AttributeValue(home, "ContentTemplate"));
        Assert.Equal(
            "False",
            AttributeValue(home, "Shell.FlyoutItemIsVisible"));
        Assert.DoesNotContain(
            home.Ancestors(),
            element => element.Name.LocalName == "TabBar");
    }

    [Fact]
    public void RegistrationCompletionCollectsEmail_AndCheckoutDoesNotRenderAnEmailField()
    {
        var completion = Load(
            "Ui",
            "Pages",
            "CompleteRegistrationPage.xaml");
        var emailEntry = completion
            .Descendants(Maui + "Entry")
            .Single(entry =>
                AttributeValue(entry, "Text") == "{Binding Email}");
        Assert.Equal("Email", AttributeValue(emailEntry, "Keyboard"));
        Assert.Equal("254", AttributeValue(emailEntry, "MaxLength"));

        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        Assert.DoesNotContain(
            detail.Descendants(Maui + "Entry"),
            entry =>
                AttributeValue(entry, "Text") == "{Binding ReceiptEmail}");
    }

    [Fact]
    public void HomeActionSpotlightHasRequestedEmptyState()
    {
        var transactions = Load(
            "Ui",
            "Pages",
            "TransactionsPage.xaml");
        var labels = transactions
            .Descendants(Maui + "Label")
            .Select(label => AttributeValue(label, "Text"))
            .ToArray();
        var emptyState = transactions
            .Descendants(Maui + "Grid")
            .Single(element =>
                AttributeValue(element, "AutomationId") ==
                "ActionSpotlightEmptyState");
        var spotlight = transactions
            .Descendants(Maui + "Border")
            .Single(element =>
                AttributeValue(element, "AutomationId") ==
                "ActionSpotlightCard");

        Assert.Contains("ยังไม่มีรายการ", labels);
        Assert.DoesNotContain("รายการดำเนินเรียบร้อย", labels);
        Assert.Equal(
            AttributeValue(spotlight, "MinimumHeightRequest"),
            AttributeValue(emptyState, "MinimumHeightRequest"));
        Assert.Contains(
            emptyState.Descendants(Maui + "Label"),
            label => AttributeValue(label, "Text") == "ยังไม่มีรายการ");
    }

    [Fact]
    public void TransactionsUseTopLevelBuySellModes()
    {
        var transactions = Load(
            "Ui",
            "Pages",
            "TransactionsPage.xaml");
        var pageTitle = transactions
            .Descendants(Maui + "Label")
            .Single(label =>
                AttributeValue(label, "AutomationId") ==
                "TransactionsPageTitle");
        var modeSwitch = transactions
            .Descendants(Maui + "Border")
            .Single(border =>
                AttributeValue(border, "AutomationId") ==
                "TransactionRoleModeSwitch");
        var modeLabels = modeSwitch
            .Descendants(Maui + "Button")
            .Select(button => AttributeValue(button, "Text"))
            .ToArray();
        var buyerFilters = transactions
            .Descendants(Maui + "ScrollView")
            .Single(scroll =>
                AttributeValue(scroll, "AutomationId") ==
                "BuyerStatusFilters");
        var sellerFilters = transactions
            .Descendants(Maui + "ScrollView")
            .Single(scroll =>
                AttributeValue(scroll, "AutomationId") ==
                "SellerStatusFilters");

        Assert.Equal("รายการของคุณ", AttributeValue(pageTitle, "Text"));
        Assert.DoesNotContain(
            transactions.Descendants(Maui + "Label"),
            label => AttributeValue(label, "Text") == "TOKLONG");
        Assert.Equal(new[] { "ซื้อ", "ขาย" }, modeLabels);
        Assert.Equal(
            "{Binding IsBuying}",
            AttributeValue(buyerFilters, "IsVisible"));
        Assert.Equal(
            "{Binding IsSelling}",
            AttributeValue(sellerFilters, "IsVisible"));
        Assert.Contains(
            transactions.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding ModeSectionTitle}");
        Assert.Contains(
            transactions.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "Text") ==
                    "+ สร้างดีลซื้อ" &&
                AttributeValue(button, "IsVisible") ==
                    "{Binding IsBuying}");
    }

    [Fact]
    public void CreateOfferUsesApprovedOfferHeading()
    {
        var createOffer = Load(
            "Ui",
            "Pages",
            "CreateOfferPage.xaml");
        var labels = createOffer
            .Descendants(Maui + "Label")
            .Select(label => AttributeValue(label, "Text"))
            .ToArray();
        var quickDealForm = createOffer
            .Descendants(Maui + "VerticalStackLayout")
            .Single(layout =>
                AttributeValue(layout, "AutomationId") ==
                "QuickDealForm");

        Assert.Contains("สร้างข้อเสนอ", labels);
        Assert.Contains("ส่งให้ผู้ขายตรวจและตอบรับ", labels);
        Assert.DoesNotContain("สร้างข้อตกลงซื้อขาย", labels);
        Assert.DoesNotContain("สร้างดีลผู้ซื้อกับผู้ขาย", labels);
        Assert.DoesNotContain(
            "คุณกรอก ผู้ขายยืนยันหรือปฏิเสธ",
            labels);
        Assert.DoesNotContain(
            "เมื่อผู้ขายตกลง คุณจะได้ตรวจรายละเอียดเดิมอีกครั้งก่อนจ่ายเงิน ผู้ขายต้องส่งของภายใน 3 วันหลังจ่ายสำเร็จ",
            labels);
        Assert.DoesNotContain("บันทึกสิ่งที่ตกลงกัน", labels);
        Assert.DoesNotContain("ดีลซื้อขายส่วนตัว", labels);
        Assert.Equal(
            "24",
            AttributeValue(quickDealForm, "Spacing"));
        Assert.DoesNotContain(
            quickDealForm.Ancestors(Maui + "Border"),
            border =>
                AttributeValue(border, "Style") ==
                "{StaticResource RefinedFormCard}");
    }

    [Fact]
    public void CreateOfferUsesApprovedEssentialFirstVisualHierarchy()
    {
        var resources = Load("Ui", "App.xaml");
        var createOffer = Load(
            "Ui",
            "Pages",
            "CreateOfferPage.xaml");
        var header = createOffer
            .Descendants(Maui + "Border")
            .Single(border =>
                AttributeValue(border, "AutomationId") ==
                "CreateOfferHeader");
        var progress = header
            .Descendants(Maui + "Grid")
            .Single(grid =>
                AttributeValue(grid, "AutomationId") ==
                "CreateOfferProgress");
        var priceSection = createOffer
            .Descendants(Maui + "VerticalStackLayout")
            .Single(layout =>
                AttributeValue(layout, "AutomationId") ==
                "ItemPriceSection");
        var amountBorder = priceSection
            .Descendants(Maui + "Border")
            .Single();
        var orderedSections = createOffer
            .Descendants()
            .Select(element =>
                AttributeValue(element, "AutomationId"))
            .Where(value => value is
                "SellerPhoneSection" or
                "ProductNameSection" or
                "ItemPriceSection" or
                "DeliveryAddressSection" or
                "SecondaryOfferOptions")
            .ToArray();

        Assert.Equal(
            "False",
            AttributeValue(
                createOffer.Root!,
                "Shell.NavBarIsVisible"));
        Assert.Contains(
            header.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "สร้างข้อเสนอ");
        Assert.Contains(
            header.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "ส่งให้ผู้ขายตรวจและตอบรับ");
        Assert.Equal(
            2,
            progress.Elements(Maui + "BoxView").Count());
        Assert.Equal(
            "{StaticResource RefinedAmountBorder}",
            AttributeValue(amountBorder, "Style"));
        Assert.Null(
            AttributeValue(priceSection, "BackgroundColor"));
        Assert.Equal(
            new[]
            {
                "SellerPhoneSection",
                "ProductNameSection",
                "ItemPriceSection",
                "DeliveryAddressSection",
                "SecondaryOfferOptions"
            },
            orderedSections);
        Assert.Equal(
            "None",
            StyleSetterValue(
                resources,
                "RefinedFormLabel",
                "FontAttributes"));
        Assert.Equal(
            "NotoSansThaiMedium",
            StyleSetterValue(
                resources,
                "RefinedFormLabel",
                "FontFamily"));
        Assert.Equal(
            "None",
            StyleSetterValue(
                resources,
                "RefinedHelperText",
                "FontAttributes"));
    }

    [Fact]
    public void CreateOfferAiHelperStaysCollapsedBehindOneAction()
    {
        var createOffer = Load(
            "Ui",
            "Pages",
            "CreateOfferPage.xaml");
        var openButton = createOffer
            .Descendants(Maui + "Button")
            .Single(button =>
                AttributeValue(button, "AutomationId") ==
                "OpenAiAgreementDraftButton");
        var sheet = createOffer
            .Descendants(Maui + "Grid")
            .Single(grid =>
                AttributeValue(grid, "AutomationId") ==
                "AiAgreementDraftSheet");

        Assert.Equal("ให้ AI ช่วยกรอก", AttributeValue(openButton, "Text"));
        Assert.Equal(
            "ui_ai_assist.png",
            AttributeValue(openButton, "ImageSource"));
        Assert.Equal(
            "Left,10",
            AttributeValue(openButton, "ContentLayout"));
        Assert.Equal(
            "{Binding IsAiSheetOpen}",
            AttributeValue(sheet, "IsVisible"));
        Assert.Contains(
            sheet.Descendants(Maui + "Editor"),
            editor =>
                AttributeValue(editor, "Text") ==
                "{Binding AiChatText}");
        Assert.Contains(
            sheet.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "AutomationId") ==
                "ApplyAiAgreementDraftButton");
    }

    [Fact]
    public void CreateOfferUsesQuickDealThenReviewProgressiveDisclosure()
    {
        var createOffer = Load(
            "Ui",
            "Pages",
            "CreateOfferPage.xaml");
        var reviewButton = createOffer
            .Descendants(Maui + "Button")
            .Single(button =>
                AttributeValue(button, "AutomationId") ==
                "ReviewQuickDealButton");
        var reviewSheet = createOffer
            .Descendants(Maui + "Grid")
            .Single(grid =>
                AttributeValue(grid, "AutomationId") ==
                "QuickDealReviewSheet");
        var finalButton = reviewSheet
            .Descendants(Maui + "Button")
            .Single(button =>
                AttributeValue(button, "AutomationId") ==
                "SubmitReviewedOfferButton");
        var optionalDetails = createOffer
            .Descendants(Maui + "Editor")
            .Single(editor =>
                AttributeValue(editor, "Text") ==
                "{Binding AgreementDetails}");
        var optionalDisclosure = createOffer
            .Descendants(Maui + "Border")
            .Single(border =>
                AttributeValue(border, "AutomationId") ==
                "OptionalDealDetailsDisclosure");
        var optionalPhoto = createOffer
            .Descendants(Maui + "VerticalStackLayout")
            .Single(layout =>
                AttributeValue(layout, "AutomationId") ==
                "OptionalProductPhotoField");

        Assert.Equal(
            "ตรวจข้อมูลก่อนส่ง  →",
            AttributeValue(reviewButton, "Text"));
        Assert.Equal(
            "{Binding ReviewCommand}",
            AttributeValue(reviewButton, "Command"));
        Assert.Equal(
            "{Binding IsReviewSheetOpen}",
            AttributeValue(reviewSheet, "IsVisible"));
        Assert.Equal(
            "{Binding SubmitCommand}",
            AttributeValue(finalButton, "Command"));
        Assert.Contains(
            optionalDetails.Ancestors(Maui + "VerticalStackLayout"),
            element =>
                AttributeValue(element, "IsVisible") ==
                "{Binding ShowOptionalDetails}");
        Assert.Contains(
            optionalDisclosure.Descendants(Maui + "TapGestureRecognizer"),
            gesture =>
                AttributeValue(gesture, "Command") ==
                "{Binding ToggleOptionalDetailsCommand}");
        Assert.Contains(
            optionalPhoto.Descendants(),
            element =>
                AttributeValue(element, "Text") ==
                "รูปสินค้า (ไม่บังคับ)" &&
                string.IsNullOrEmpty(
                    AttributeValue(element, "IsRequired")));
        Assert.Contains(
            optionalDisclosure.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding OptionalDetailsChevron}");
        Assert.Contains(
            reviewSheet.Descendants(Maui + "Editor"),
            editor =>
                AttributeValue(editor, "Text") ==
                "{Binding KnownDefects}");
        Assert.DoesNotContain(
            createOffer.Descendants(Maui + "Picker"),
            picker =>
                AttributeValue(picker, "ItemsSource") ==
                "{Binding ConditionOptions}");
    }

    [Fact]
    public void CreateOfferReviewContainsTheOnlyBuyerCostBreakdown()
    {
        var createOffer = Load(
            "Ui",
            "Pages",
            "CreateOfferPage.xaml");
        var reviewSheet = createOffer
            .Descendants(Maui + "Grid")
            .Single(grid =>
                AttributeValue(grid, "AutomationId") ==
                "QuickDealReviewSheet");
        var costSummary = reviewSheet
            .Descendants(Maui + "Border")
            .Single(border =>
                AttributeValue(border, "AutomationId") ==
                "ReviewCostSummary");
        var reviewButton = createOffer
            .Descendants(Maui + "Button")
            .Single(button =>
                AttributeValue(button, "AutomationId") ==
                "ReviewQuickDealButton");

        Assert.DoesNotContain(
            createOffer.Descendants(),
            element =>
                AttributeValue(element, "AutomationId") is
                    "BuyerCostPreviewSummary" or
                    "BuyerCostPreviewSheet" or
                    "BuyerCostPreviewFormSpacer");
        Assert.Contains(
            costSummary.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding CostProtectionFeeText}");
        Assert.Contains(
            costSummary.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding CostShippingText}");
        Assert.Contains(
            costSummary.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding CostTotalText}");
        Assert.Contains(
            costSummary.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "ยังไม่ตัดเงินในขั้นตอนนี้");
        Assert.DoesNotContain(
            reviewSheet.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") is
                    "กำหนดส่งสินค้า" or
                    "ผู้ขายต้องส่งภายใน 3 วันหลังระบบยืนยันยอดชำระ");
        var pricingTrigger = reviewButton
            .Descendants(Maui + "DataTrigger")
            .Single(trigger =>
                AttributeValue(trigger, "Binding") ==
                "{Binding IsReviewPricing}");
        Assert.Contains(
            pricingTrigger.Descendants(Maui + "Setter"),
            setter =>
                AttributeValue(setter, "Property") ==
                    "Text" &&
                AttributeValue(setter, "Value") ==
                    "กำลังคำนวณค่าใช้จ่าย...");
    }

    [Fact]
    public void OtpInput_UsesSixUnderlineSlots_InsteadOfVisibleTextBoxes()
    {
        var verifyCode = Load("Ui", "Pages", "VerifyCodePage.xaml");

        Assert.DoesNotContain(
            verifyCode.Descendants(),
            element => element.Name.LocalName == "Entry");
        Assert.DoesNotContain(
            verifyCode.Descendants(),
            element => element.Name.LocalName == "RefinedInputBorder");

        var otpControl = Load("Ui", "Controls", "OtpCodeInput.xaml");
        Assert.Equal(6, otpControl.Descendants(Maui + "BoxView").Count());
        Assert.Equal(
            6,
            otpControl
                .Descendants(Maui + "Label")
                .Count(label => AttributeValue(label, "FontSize") == "25"));
    }

    [Fact]
    public void TrackingForm_UsesSupportedCarrierPicker_NotFreeText()
    {
        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        var carrierPicker = detail
            .Descendants(Maui + "Picker")
            .Single(picker =>
                AttributeValue(
                    picker,
                    "ItemsSource") == "{Binding Carriers}");

        Assert.Equal(
            "{Binding SelectedCarrier}",
            AttributeValue(carrierPicker, "SelectedItem"));
        Assert.Equal(
            "{StaticResource RefinedPicker}",
            AttributeValue(carrierPicker, "Style"));
        Assert.DoesNotContain(
            detail.Descendants(Maui + "Entry"),
            entry => AttributeValue(
                entry,
                "Text") == "{Binding CarrierCode}");
    }

    [Fact]
    public void ManagedShippingLabelOpensAFullScreenViewer()
    {
        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        var labelCard = detail
            .Descendants(Maui + "Border")
            .Single(border =>
                AttributeValue(border, "AutomationId") ==
                "ManagedShippingLabelCard");
        Assert.Contains(
            labelCard.Descendants(Maui + "TapGestureRecognizer"),
            gesture =>
                AttributeValue(gesture, "Command") ==
                "{Binding OpenShippingLabelCommand}");
        Assert.Contains(
            labelCard.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "›");
        Assert.DoesNotContain(
            labelCard.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "↗");
        Assert.Contains(
            detail.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "AutomationId") ==
                    "OpenFullShippingLabelButton" &&
                AttributeValue(button, "Text") ==
                    "แตะเพื่อดูใบปะหน้าเต็มจอ");

        var viewer = Load(
            "Ui",
            "Pages",
            "ShippingLabelPage.xaml");
        var webView = viewer
            .Descendants(Maui + "WebView")
            .Single();
        Assert.Equal(
            "{Binding LabelSource}",
            AttributeValue(webView, "Source"));
        Assert.Equal(
            "ShippingLabelWebView",
            AttributeValue(webView, "AutomationId"));
        Assert.Contains(
            viewer.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "AutomationId") ==
                    "SaveShippingLabelButton" &&
                AttributeValue(button, "Command") ==
                    "{Binding SaveCommand}" &&
                AttributeValue(button, "Text") ==
                    "บันทึกลงเครื่อง");
        Assert.Contains(
            viewer.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "AutomationId") ==
                    "ShareOrPrintShippingLabelButton" &&
                AttributeValue(button, "Command") ==
                    "{Binding ShareOrPrintCommand}" &&
                AttributeValue(button, "Text") ==
                    "แชร์หรือพิมพ์");
        Assert.DoesNotContain(
            viewer.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "FontAttributes") ==
                    "Bold");
    }

    [Fact]
    public void SellerDetailShowsOnlyUsefulProductInformation()
    {
        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        var agreementDisclosure = detail
            .Descendants(Maui + "Grid")
            .Single(grid =>
                AttributeValue(grid, "AutomationId") ==
                "SellerAgreementDetailsDisclosure");

        Assert.Contains(
            agreementDisclosure.Descendants(Maui + "TapGestureRecognizer"),
            gesture =>
                AttributeValue(gesture, "Command") ==
                "{Binding ToggleAgreementDetailsCommand}");
        Assert.Contains(
            detail.Descendants(Maui + "VerticalStackLayout"),
            stack =>
                AttributeValue(stack, "IsVisible") ==
                "{Binding ShowAgreementDetailsContent}");
        Assert.Contains(
            detail.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding Transaction.ConditionLabel}");
        Assert.Contains(
            detail.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding Transaction.KnownDefects}");
        Assert.DoesNotContain(
            detail.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding Transaction.AgreementEvidenceHash}");
        Assert.DoesNotContain(
            detail.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "หลักฐานข้อตกลงร่วม");
        Assert.DoesNotContain(
            detail.Descendants(Maui + "Label"),
            label =>
                AttributeValue(label, "Text") ==
                "{Binding Transaction.TermsDisplayText}");
        Assert.DoesNotContain(
            detail.Descendants(),
            element =>
                AttributeValue(element, "AutomationId") ==
                "TransactionRecordDisclosure");
    }

    [Fact]
    public void TransactionDetailGradients_HaveColorsBeforeTransactionLoads()
    {
        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        var dynamicGradientStops = detail
            .Descendants(Maui + "GradientStop")
            .Select(stop => AttributeValue(stop, "Color"))
            .Where(color =>
                color?.Contains(
                    "{Binding Transaction.",
                    StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(5, dynamicGradientStops.Length);
        Assert.All(
            dynamicGradientStops,
            color =>
            {
                Assert.Contains(
                    "FallbackValue=#",
                    color,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "TargetNullValue=#",
                    color,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public void BuyerCanSharePhoneProtectedOfferWithoutClipboardOpen()
    {
        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        var transactions = Load(
            "Ui",
            "Pages",
            "TransactionsPage.xaml");

        Assert.DoesNotContain(
            transactions.Descendants(),
            element =>
                AttributeValue(element, "AutomationId") ==
                "OpenCopiedSellerLinkButton");
        var invitationCard = detail
            .Descendants()
            .Single(element =>
                AttributeValue(element, "AutomationId") ==
                "SellerInvitationCard");
        Assert.Equal(
            "{Binding ShowSellerInvitation}",
            AttributeValue(invitationCard, "IsVisible"));
        Assert.Contains(
            invitationCard.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "AutomationId") ==
                    "CopySellerInvitationLinkButton" &&
                AttributeValue(button, "Command") ==
                    "{Binding CopyInvitationLinkCommand}");
        Assert.Contains(
            invitationCard.Descendants(Maui + "Button"),
            button =>
                AttributeValue(button, "AutomationId") ==
                    "ShareSellerInvitationLinkButton" &&
                AttributeValue(button, "Command") ==
                    "{Binding ShareInvitationLinkCommand}");
    }

    [Fact]
    public void TransactionDetail_ReferencesOnlyDeclaredResources()
    {
        var app = Load("Ui", "App.xaml");
        var detail = Load(
            "Ui",
            "Pages",
            "TransactionDetailPage.xaml");
        var declaredKeys = app
            .Descendants()
            .Concat(detail.Descendants())
            .Select(element =>
                AttributeValue(element, "Key"))
            .Where(key =>
                !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);
        var resourcePrefix = "{StaticResource ";
        var referencedKeys = detail
            .Descendants()
            .Attributes()
            .Select(attribute => attribute.Value)
            .Where(value =>
                value.StartsWith(
                    resourcePrefix,
                    StringComparison.Ordinal) &&
                value.EndsWith(
                    '}'))
            .Select(value =>
                value[resourcePrefix.Length..^1])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.All(
            referencedKeys,
            key => Assert.True(
                declaredKeys.Contains(key),
                $"TransactionDetailPage.xaml references missing resource '{key}'."));
    }

    [Fact]
    public void CreateOffer_RequiresTargetSellerPhoneAndProductName()
    {
        var create = Load(
            "Ui",
            "Pages",
            "CreateOfferPage.xaml");
        var sellerPhone = create
            .Descendants()
            .Single(element =>
                element.Name.LocalName ==
                    "ThaiMobilePhoneEntry" &&
                AttributeValue(
                    element,
                    "PhoneNumber") ==
                    "{Binding SellerPhoneNumber}");
        Assert.Equal(
            "{StaticResource RefinedEntry}",
            AttributeValue(sellerPhone, "Style"));

        var productName = create
            .Descendants(Maui + "Entry")
            .Single(entry =>
                AttributeValue(
                    entry,
                    "Text") ==
                    "{Binding ProductName}");
        Assert.Equal(
            "180",
            AttributeValue(productName, "MaxLength"));
        Assert.Equal(
            "{StaticResource RefinedEntry}",
            AttributeValue(productName, "Style"));
    }

    [Fact]
    public void ActivityPage_UsesApiBackedNotificationCollection()
    {
        var activity = Load(
            "Ui",
            "Pages",
            "ActivityPage.xaml");
        var collection = activity
            .Descendants(Maui + "CollectionView")
            .Single();
        Assert.Equal(
            "{Binding Items}",
            AttributeValue(collection, "ItemsSource"));
        Assert.DoesNotContain(
            activity.Descendants(Maui + "Label"),
            label => AttributeValue(label, "Text") ==
                     "ผู้ขายตกลงขายกล้องแล้ว");
    }

    [Fact]
    public void StartupLogo_IsNonInteractiveAndHasOneAccessibleName()
    {
        var page = Load("Ui", "Pages", "StartupLogoPage.xaml");
        var mark = Load(
            "Ui",
            "Controls",
            "TransactionRailMarkView.xaml");

        Assert.Empty(page.Descendants(Maui + "Button"));
        Assert.Empty(page.Descendants(Maui + "Entry"));
        Assert.Contains(
            page.Descendants(),
            element =>
                element.Name.LocalName == "TransactionRailMarkView");

        Assert.Equal(
            "True",
            AttributeValue(
                mark.Root!,
                "AutomationProperties.IsInAccessibleTree"));
        Assert.Equal(
            "โลโก้ TOKLONG",
            AttributeValue(
                mark.Root!,
                "SemanticProperties.Description"));

        var decorativeChildren = mark
            .Descendants()
            .Where(element =>
                element.Name.LocalName is "Image" or "Border" or "Label");
        Assert.All(
            decorativeChildren,
            element => Assert.Equal(
                "False",
                AttributeValue(
                    element,
                    "AutomationProperties.IsInAccessibleTree")));
    }

    [Fact]
    public void StartupLogo_IsNotPartOfShellHistory()
    {
        var shell = Load("Ui", "AppShell.xaml");

        Assert.DoesNotContain(
            shell.Descendants(),
            element =>
                AttributeValue(element, "Route") == "startup");
        Assert.Equal(
            "welcome",
            AttributeValue(
                shell.Descendants()
                    .First(element =>
                        element.Name.LocalName == "ShellContent"),
                "Route"));
    }

    [Fact]
    public void StartupServices_AreRegisteredAndShellIsInstalledAfterIntro()
    {
        var program = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Ui",
                "MauiProgram.cs"));
        var app = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Ui",
                "App.xaml.cs"));

        Assert.Contains(
            "AddSingleton<IStartupMotionPreference, StartupMotionPreference>()",
            program);
        Assert.Contains("AddSingleton<StartupCoordinator>()", program);
        Assert.Contains("AddSingleton<StartupLogoPage>()", program);
        Assert.Contains("new Window(startupPage)", app);
        Assert.Contains("window.Page = shell", app);
        Assert.DoesNotContain(
            "IAuthenticationService authentication",
            app);
    }

    [Fact]
    public void EveryUserFacingFormControl_UsesTheSharedControlStyle()
    {
        var pageDirectory = Path.Combine(AppContext.BaseDirectory, "Ui", "Pages");
        var pages = Directory.GetFiles(pageDirectory, "*.xaml");

        Assert.NotEmpty(pages);

        foreach (var page in pages)
        {
            var document = XDocument.Load(page);

            AssertStyle(document, page, "Entry", "RefinedEntry", "RefinedAmountEntry");
            AssertStyle(document, page, "ThaiMobilePhoneEntry", "RefinedEntry");
            AssertStyle(document, page, "Picker", "RefinedPicker");
            AssertStyle(document, page, "Editor", "RefinedEditor");
        }
    }

    private static void AssertStyle(
        XDocument document,
        string page,
        string controlName,
        params string[] allowedStyles)
    {
        foreach (var control in document.Descendants().Where(element => element.Name.LocalName == controlName))
        {
            var style = AttributeValue(control, "Style");
            var isAllowed = allowedStyles.Any(
                allowed => style == $"{{StaticResource {allowed}}}");

            Assert.True(
                isAllowed,
                $"{Path.GetFileName(page)} contains {controlName} without an approved shared style.");
            Assert.Null(control.Attribute("FontSize"));
        }
    }

    private static XDocument Load(params string[] pathSegments)
    {
        var path = pathSegments.Prepend(AppContext.BaseDirectory).ToArray();
        return XDocument.Load(Path.Combine(path));
    }

    private static string ResourceValue(XDocument document, string key)
    {
        var resource = document
            .Descendants()
            .Single(element => AttributeValue(element, "Key") == key);

        return resource.Value.Trim();
    }

    private static string StyleSetterValue(XDocument document, string styleKey, string property)
    {
        var style = document
            .Descendants(Maui + "Style")
            .Single(element => AttributeValue(element, "Key") == styleKey);
        var setter = style
            .Elements(Maui + "Setter")
            .Single(element => AttributeValue(element, "Property") == property);

        return AttributeValue(setter, "Value")
            ?? throw new InvalidOperationException($"Style '{styleKey}' has no value for '{property}'.");
    }

    private static string? AttributeValue(XElement element, string localName)
    {
        return element
            .Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == localName)
            ?.Value;
    }

    private static int RequiredIntAttribute(
        XElement element,
        string localName) =>
        int.Parse(
            AttributeValue(element, localName)
            ?? throw new InvalidOperationException(
                $"Element '{element.Name.LocalName}' has no '{localName}' attribute"));
}
