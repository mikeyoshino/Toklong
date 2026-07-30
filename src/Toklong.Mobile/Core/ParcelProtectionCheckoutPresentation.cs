namespace Toklong.Mobile.Core;

public enum ParcelProtectionCheckoutStep
{
    Choose,
    SubmitIncludedCoverage,
    PresentPayment,
    Reconfirm
}

public static class ParcelProtectionCheckoutPresentation
{
    public static ParcelProtectionCheckoutStep Next(
        BuyerParcelProtection protection) =>
        protection.ReconfirmationRequired
            ? ParcelProtectionCheckoutStep.Reconfirm
            : protection.BookingReady ||
              protection.Election != "Pending"
                ? ParcelProtectionCheckoutStep.PresentPayment
                : protection.RequiresChoice
                        ? ParcelProtectionCheckoutStep.Choose
                        : ParcelProtectionCheckoutStep.SubmitIncludedCoverage;
}
