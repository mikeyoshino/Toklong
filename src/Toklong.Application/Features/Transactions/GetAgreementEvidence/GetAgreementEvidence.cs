using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Transactions.GetAgreementEvidence;

public sealed record GetAgreementEvidenceQuery(
    Guid TransactionId,
    Guid? BuyerId,
    Guid? SellerId) : IRequest<AgreementEvidenceDownload>;

public sealed record AgreementEvidenceDownload(
    string JsonFileName,
    byte[] JsonBytes,
    string HtmlFileName,
    byte[] HtmlBytes,
    string EvidenceHash);

public sealed class GetAgreementEvidenceHandler(
    ITransactionRepository repository)
    : IRequestHandler<
        GetAgreementEvidenceQuery,
        AgreementEvidenceDownload>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<AgreementEvidenceDownload> Handle(
        GetAgreementEvidenceQuery request,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(
            request.TransactionId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        var viewer = Viewer(transaction, request);
        if (viewer is null)
            throw new ForbiddenException(
                "บัญชีนี้ไม่มีสิทธิ์ดาวน์โหลดหลักฐานรายการ");
        if (!transaction.HasValidAgreementSnapshot() ||
            !transaction.HasMatchingPartyAcceptances())
            throw new DomainException(
                "หลักฐานข้อตกลงของทั้งสองฝ่ายยังไม่ครบหรือไม่ตรงกับ hash");
        if (transaction.SnapshotSchemaVersion >= 11 &&
            !transaction.HasValidBuyerCheckoutAnnexAcceptance())
            throw new DomainException(
                "หลักฐานภาคผนวกการชำระของผู้ซื้อไม่ครบหรือไม่ตรงกับ hash");

        var payload = CreatePayload(
            transaction,
            viewer == EvidenceViewer.Buyer);
        var payloadJson = JsonSerializer.Serialize(
            payload,
            JsonOptions);
        var evidenceHash = Hash(payloadJson);
        using var payloadDocument =
            JsonDocument.Parse(payloadJson);
        var document = new
        {
            SchemaVersion = 1,
            EvidenceHashSha256 = evidenceHash,
            Evidence = payloadDocument.RootElement.Clone()
        };
        var json = JsonSerializer.Serialize(
            document,
            JsonOptions);
        var html = CreateHtml(
            transaction,
            evidenceHash,
            viewer == EvidenceViewer.Buyer);
        var prefix =
            $"TOKLONG-agreement-{transaction.Id:N}";
        return new AgreementEvidenceDownload(
            $"{prefix}.json",
            Encoding.UTF8.GetBytes(json),
            $"{prefix}.html",
            Encoding.UTF8.GetBytes(html),
            evidenceHash);
    }

    private static EvidenceViewer? Viewer(
        SaleTransaction transaction,
        GetAgreementEvidenceQuery request)
    {
        if (request.BuyerId.HasValue &&
            transaction.BuyerId == request.BuyerId)
            return EvidenceViewer.Buyer;
        if (request.SellerId.HasValue &&
            transaction.SellerId == request.SellerId)
            return EvidenceViewer.Seller;
        return null;
    }

    private enum EvidenceViewer
    {
        Buyer,
        Seller
    }

    private static object CreatePayload(
        SaleTransaction transaction,
        bool includeBuyerProtection)
    {
        var payload = new Dictionary<string, object?>
        {
            ["id"] = transaction.Id,
            ["snapshotSchemaVersion"] =
                transaction.SnapshotSchemaVersion,
            ["agreementType"] =
                "electronic-click-acceptance",
            ["item"] = new
            {
                transaction.ProductName,
                FulfillmentType =
                    transaction.FulfillmentType.ToString(),
                Condition =
                    transaction.Condition.ToString(),
                transaction.Description,
                transaction.KnownDefects,
                transaction.PhotoUrl
            },
            ["deliveryRegion"] =
                transaction.FulfillmentType ==
                FulfillmentType.PhysicalShipment
                    ? new
                    {
                        transaction.DeliveryProvinceName,
                        transaction.DeliveryPostalCode
                    }
                    : null,
            ["amount"] = CreateAmount(transaction, includeBuyerProtection),
            ["terms"] = CreateTerms(transaction, includeBuyerProtection),
            ["parties"] = new
            {
                Buyer = new
                {
                    transaction.BuyerDisplayName,
                    Contact = Mask(
                        transaction.BuyerContact)
                },
                Seller = new
                {
                    transaction.SellerDisplayName,
                    Contact = Mask(
                        transaction.SellerContact)
                }
            },
            ["hashes"] = new
            {
                transaction.AgreementCoreSnapshotHash,
                transaction.TermsSnapshotHash,
                transaction.ProductSnapshotHash
            },
            ["acceptance"] = transaction
                .AgreementAcceptances
                .OrderBy(item => item.Role)
                .Select(item => new
                {
                    Role = item.Role.ToString(),
                    item.AuthenticationMethod,
                    item.TermsVersion,
                    item.AgreementCoreSnapshotHash,
                    item.TermsSnapshotHash,
                    item.AcceptedAt
                })
                .ToArray(),
            ["timeline"] = new
            {
                transaction.SellerAcceptedAt,
                transaction.BuyerAcceptedAt,
                transaction.AgreementSnapshotCreatedAt,
                transaction.AgreementSnapshotSealedAt,
                transaction.PaymentConfirmedAt,
                transaction.ShipByAt,
                transaction.DeliveredAt,
                transaction.DisputeWindowEndsAt,
                transaction.PayoutConfirmedAt
            },
            ["notice"] =
                "บันทึกการยอมรับข้อตกลงทางอิเล็กทรอนิกส์ ไม่ใช่ลายเซ็นดิจิทัลแบบมีใบรับรองหรือคำแนะนำทางกฎหมาย"
        };
        if (includeBuyerProtection &&
            transaction.SnapshotSchemaVersion >= 11)
        {
            var annex = transaction.BuyerCheckoutAnnexAcceptances.Single();
            var election = transaction.FulfillmentType ==
                FulfillmentType.DigitalHandoff
                    ? ParcelProtectionElectionStatus.NotApplicable
                    : transaction.ParcelProtectionElection;
            var buyerAnnex = new Dictionary<string, object?>
            {
                ["parcelProtectionElection"] = election.ToString(),
                ["productSnapshotHash"] = transaction.ProductSnapshotHash,
                ["payloadHashSha256"] = annex.PayloadHash,
                ["acceptedAt"] = annex.AcceptedAt,
                ["currency"] = transaction.Currency
            };
            if (transaction.FulfillmentType ==
                FulfillmentType.PhysicalShipment)
            {
                buyerAnnex["customerPriceSatang"] =
                    transaction.ParcelInsuranceFeeSatang;
                buyerAnnex["termsVersion"] =
                    transaction.ParcelProtectionTermsVersion;
                buyerAnnex["parcelProtectionBuyerElectedAt"] =
                    transaction.ParcelProtectionBuyerElectedAt;
                var coverageIsKnown =
                    transaction.ParcelProtectionElection !=
                        ParcelProtectionElectionStatus.Unavailable ||
                    transaction.ParcelProtectionIncludedCoverageSatang != 0 ||
                    transaction.ParcelProtectionSelectedCoverageSatang != 0;
                if (coverageIsKnown)
                {
                    buyerAnnex["includedCoverageLimitSatang"] =
                        transaction.ParcelProtectionIncludedCoverageSatang;
                    buyerAnnex["selectedCoverageLimitSatang"] =
                        transaction.ParcelProtectionSelectedCoverageSatang;
                }
            }

            payload["buyerCheckoutAnnex"] = buyerAnnex;
        }

        return payload;
    }

    private static object CreateAmount(
        SaleTransaction transaction,
        bool includeBuyerProtection) =>
        includeBuyerProtection
            ? new
            {
                transaction.PriceSatang,
                transaction.ShippingFeeSatang,
                transaction.BuyerTotalSatang,
                transaction.BuyerProtectionFeeSatang,
                transaction.PlatformFeeSatang,
                transaction.SellerExpectedNetSatang,
                transaction.Currency
            }
            : new
            {
                transaction.PriceSatang,
                transaction.ShippingFeeSatang,
                transaction.SellerExpectedNetSatang,
                transaction.Currency
            };

    private static object CreateTerms(
        SaleTransaction transaction,
        bool includeBuyerProtection) =>
        includeBuyerProtection
            ? new
            {
                transaction.TermsVersion,
                transaction.FeePolicyVersion,
                transaction.ShipByDurationHours,
                transaction.InspectionWindowDurationHours
            }
            : new
            {
                transaction.TermsVersion,
                transaction.ShipByDurationHours,
                transaction.InspectionWindowDurationHours
            };

    private static string CreateHtml(
        SaleTransaction transaction,
        string evidenceHash,
        bool includeBuyerProtection)
    {
        var acceptances = string.Join(
            "",
            transaction.AgreementAcceptances
                .OrderBy(item => item.Role)
                .Select(item =>
                    $"<tr><td>{H(Role(item.Role))}</td>" +
                    $"<td>{H(ThaiTime(item.AcceptedAt))}</td>" +
                    "<td>บัญชีที่ยืนยันด้วยเบอร์โทร</td></tr>"));
        var buyerParcelProtectionRows =
            includeBuyerProtection &&
            transaction.SnapshotSchemaVersion >= 11 &&
            transaction.FulfillmentType ==
                FulfillmentType.PhysicalShipment
                ? $$"""
                <dt>ตัวเลือกความคุ้มครองพัสดุ</dt><dd>{{H(ParcelProtectionElection(transaction.ParcelProtectionElection))}}</dd>
                <dt>ค่าความคุ้มครองพัสดุ</dt><dd>{{H(Money(transaction.ParcelInsuranceFeeSatang, transaction.Currency))}}</dd>
                {{BuyerCoverageRows(transaction)}}
                <dt>Parcel Protection Terms version</dt><dd>{{H(transaction.ParcelProtectionTermsVersion)}}</dd>
                <dt>Buyer Checkout Annex Hash</dt><dd class="hash">{{H(transaction.BuyerCheckoutAnnexAcceptances.Single().PayloadHash)}}</dd>
                """
                : "";
        var buyerProtectionRows = includeBuyerProtection
            ? $$"""
                <dt>ค่าคุ้มครองผู้ซื้อ</dt><dd>{{H(Money(transaction.BuyerProtectionFeeSatang, transaction.Currency))}}</dd>
                {{buyerParcelProtectionRows}}
                <dt>ยอดรวม</dt><dd>{{H(Money(transaction.BuyerTotalSatang, transaction.Currency))}}</dd>
                """
            : $$"""
                <dt>ยอดที่จะได้รับ</dt><dd>{{H(Money(transaction.SellerExpectedNetSatang, transaction.Currency))}}</dd>
                """;
        return $$"""
            <!doctype html>
            <html lang="th">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>หลักฐานข้อตกลง TOKLONG</title>
              <style>
                body { font-family: system-ui,sans-serif; margin: 32px auto; max-width: 820px; padding: 0 18px; color: #172033; }
                h1 { font-size: 24px; } h2 { margin-top: 28px; font-size: 18px; }
                dl { display: grid; grid-template-columns: 190px 1fr; gap: 8px 14px; }
                dt { color: #657085; } dd { margin: 0; overflow-wrap: anywhere; }
                table { width: 100%; border-collapse: collapse; }
                th,td { text-align: left; border-bottom: 1px solid #dfe5ed; padding: 9px 6px; }
                .hash { font-family: ui-monospace,monospace; font-size: 12px; overflow-wrap: anywhere; }
                .notice { margin-top: 28px; padding: 14px; background: #f4f7fb; border-radius: 10px; }
                @media print { button { display: none; } body { margin-top: 0; } }
              </style>
            </head>
            <body>
              <button onclick="window.print()">พิมพ์หรือบันทึกเป็น PDF</button>
              <h1>หลักฐานการยอมรับข้อตกลงทางอิเล็กทรอนิกส์</h1>
              <dl>
                <dt>เลขรายการ</dt><dd>{{transaction.Id}}</dd>
                <dt>สินค้า</dt><dd>{{H(transaction.ProductName)}}</dd>
                <dt>ราคาสินค้า</dt><dd>{{H(Money(transaction.PriceSatang, transaction.Currency))}}</dd>
                <dt>ค่าจัดส่ง</dt><dd>{{H(Money(transaction.ShippingFeeSatang, transaction.Currency))}}</dd>
                {{buyerProtectionRows}}
                <dt>พื้นที่จัดส่ง</dt><dd>{{H(Region(transaction))}}</dd>
                <dt>Terms version</dt><dd>{{H(transaction.TermsVersion)}}</dd>
                <dt>Agreement Core Hash</dt><dd class="hash">{{H(transaction.AgreementCoreSnapshotHash)}}</dd>
                <dt>Terms Hash</dt><dd class="hash">{{H(transaction.TermsSnapshotHash)}}</dd>
                <dt>Product Snapshot Hash</dt><dd class="hash">{{H(transaction.ProductSnapshotHash)}}</dd>
                <dt>Evidence Hash</dt><dd class="hash">{{H(evidenceHash)}}</dd>
              </dl>
              <h2>การยอมรับของคู่สัญญา</h2>
              <table>
                <thead><tr><th>ฝ่าย</th><th>เวลา</th><th>วิธียืนยันบัญชี</th></tr></thead>
                <tbody>{{acceptances}}</tbody>
              </table>
              <div class="notice">
                เอกสารนี้เป็นบันทึกการยอมรับข้อตกลงทางอิเล็กทรอนิกส์ของรายการ
                ไม่ใช่ลายเซ็นดิจิทัลแบบมีใบรับรอง และไม่ใช่คำแนะนำทางกฎหมายจาก TOKLONG
              </div>
            </body>
            </html>
            """;
    }

    private static string Region(
        SaleTransaction transaction) =>
        transaction.FulfillmentType ==
        FulfillmentType.PhysicalShipment
            ? $"{transaction.DeliveryProvinceName} {transaction.DeliveryPostalCode}"
            : "ไม่ใช้การจัดส่งทางกายภาพ";

    private static string Role(
        AgreementAcceptanceRole role) =>
        role == AgreementAcceptanceRole.Buyer
            ? "ผู้ซื้อ"
            : "ผู้ขาย";

    private static string ParcelProtectionElection(
        ParcelProtectionElectionStatus election) =>
        election switch
        {
            ParcelProtectionElectionStatus.Accepted =>
                "เลือกเพิ่มความคุ้มครอง",
            ParcelProtectionElectionStatus.Declined =>
                "ไม่เพิ่มความคุ้มครอง",
            ParcelProtectionElectionStatus.Unavailable =>
                "ไม่มีตัวเลือกเพิ่มเติม",
            ParcelProtectionElectionStatus.NotApplicable =>
                "ไม่ใช้กับรายการนี้",
            _ => election.ToString()
        };

    private static string BuyerCoverageRows(
        SaleTransaction transaction)
    {
        if (transaction.ParcelProtectionElection ==
                ParcelProtectionElectionStatus.Unavailable &&
            transaction.ParcelProtectionIncludedCoverageSatang == 0 &&
            transaction.ParcelProtectionSelectedCoverageSatang == 0)
            return "";
        return $$"""
            <dt>วงเงินคุ้มครองที่รวม</dt><dd>{{H(Money(transaction.ParcelProtectionIncludedCoverageSatang, transaction.Currency))}}</dd>
            <dt>วงเงินคุ้มครองที่เลือก</dt><dd>{{H(Money(transaction.ParcelProtectionSelectedCoverageSatang, transaction.Currency))}}</dd>
            """;
    }

    private static string ThaiTime(
        DateTimeOffset value) =>
        value.ToOffset(TimeSpan.FromHours(7))
            .ToString("dd/MM/yyyy HH:mm 'น. (เวลาไทย)'");

    private static string Money(
        long satang,
        string currency) =>
        $"{currency} {satang / 100m:N2}";

    private static string Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "—";
        var clean = value.Trim();
        return clean.Length <= 4
            ? "••••"
            : $"{new string('•', Math.Min(6, clean.Length - 4))}{clean[^4..]}";
    }

    private static string H(string? value) =>
        WebUtility.HtmlEncode(value ?? "—");

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
