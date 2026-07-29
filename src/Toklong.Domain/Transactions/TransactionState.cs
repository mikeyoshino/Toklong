namespace Toklong.Domain.Transactions;

public enum TransactionState
{
    SellerDraft,
    BuyerOfferDraft,
    AwaitingSellerAcceptance,
    SellerAcceptedAwaitingPayment,
    LinkActive,
    CheckoutStarted,
    PaymentPending,
    PaidAwaitingShipment,
    PaidAwaitingDigitalDelivery,
    DigitalDeliverySubmitted,
    TrackingSubmitted,
    TrackingUnverified,
    InTransit,
    CarrierException,
    DeliveredDisputeWindow,
    BuyerConfirmedReceipt,
    Disputed,
    ResolutionPending,
    PayoutEligible,
    PayoutPending,
    PaidOut,
    ShipmentOverdue,
    RefundPending,
    Refunded,
    Expired,
    Cancelled
}

public enum TransactionExpirationReason
{
    SellerDidNotRespond,
    BuyerDidNotPay
}

public enum FulfillmentType
{
    PhysicalShipment,
    DigitalHandoff
}

public enum ActorRole
{
    Seller,
    Buyer,
    PaymentProvider,
    CarrierProvider,
    System,
    Reconciliation
}

public enum InitiatorRole
{
    Seller,
    Buyer
}

public enum ConditionCode
{
    AsDescribed,
    New,
    UsedGood,
    UsedDefects
}

public enum DisputeReason
{
    NotReceived,
    WrongItem,
    NotAsDescribed,
    UndisclosedDamage,
    SuspectedCounterfeit,
    EmptyOrTamperedParcel,
    Other
}

public enum TrackingVerificationStatus
{
    Submitted,
    VerifiedInTransit,
    Unverified,
    Delivered
}

public enum PayoutReleaseReason
{
    BuyerConfirmedAfterInspection,
    PhysicalInspectionWindowElapsed,
    DigitalManualReview,
    DisputeResolvedForSeller
}
