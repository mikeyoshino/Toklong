namespace Toklong.Mobile.Core;

public enum ParcelProtectionCheckoutStep
{
    Choose,
    SubmitIncludedCoverage,
    WaitForBooking,
    PresentPayment,
    Reconfirm
}

public static class ParcelProtectionCheckoutPresentation
{
    public static ParcelProtectionCheckoutStep Next(
        BuyerParcelProtection protection) =>
        protection.ReconfirmationRequired
            ? ParcelProtectionCheckoutStep.Reconfirm
            : protection.RequiresChoice
                ? ParcelProtectionCheckoutStep.Choose
                : protection.BookingReady
                    ? ParcelProtectionCheckoutStep.PresentPayment
                    : protection.Election == "Pending"
                        ? ParcelProtectionCheckoutStep.SubmitIncludedCoverage
                        : ParcelProtectionCheckoutStep.WaitForBooking;
}
