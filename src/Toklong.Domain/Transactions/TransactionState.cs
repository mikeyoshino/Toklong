namespace Toklong.Domain.Transactions;

public enum TransactionState
{
    SellerDraft,
    LinkActive,
    CheckoutStarted,
    PaymentPending,
    PaidAwaitingShipment,
    PaidAwaitingDigitalDelivery,
    DigitalDeliverySubmitted,
    TrackingSubmitted,
    TrackingUnverified,
    InTransit,
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
