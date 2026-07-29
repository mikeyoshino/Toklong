namespace Toklong.Mobile.Core;

public enum TransactionProgressGlyph
{
    Agreement,
    Payment,
    PhysicalHandoff,
    PhysicalReceipt,
    DigitalHandoff,
    Payout,
    SellerAgreementProof,
    SellerPhysicalShipmentProof,
    SellerPayoutProof
}

public sealed record TransactionProgressStep(
    string Label,
    string Icon,
    TransactionProgressGlyph Glyph,
    string BackgroundColor,
    string StrokeColor,
    string LabelColor,
    string SemanticDescription);
