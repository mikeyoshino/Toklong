# SHIPPOP certification runbook

ทุก assertion ต้องมีหลักฐานจากบัญชีและ environment ที่จะใช้งานจริง ผล
`unknown` หรือ `blocked` ถือว่าไม่ผ่าน และ capability นั้นต้องปิดต่อไป
ห้ามใช้ credential ที่เคยส่งผ่านแชตหรือ commit; ให้ rotate ก่อนเริ่ม
certification

## Safe setup

credential มาจาก local environment หรือ secret storage เท่านั้น และห้ามใช้
ที่อยู่หรือเบอร์ของลูกค้าจริง คำสั่ง opt-in ที่ต้องใช้จาก repository root คือ:

```bash
SHIPPOP_CERTIFY=1 \
SHIPPOP_BASE_URL=http://mkpservice.shippop.dev \
SHIPPOP_ALLOW_INSECURE_HTTP=1 \
SHIPPOP_API_KEY="$SHIPPOP_API_KEY" \
SHIPPOP_ACCOUNT_EMAIL="$SHIPPOP_ACCOUNT_EMAIL" \
SHIPPOP_SERVICE_CODE=EMST \
SHIPPOP_SYNTHETIC_ADDRESS_JSON=/absolute/path/synthetic-address.json \
dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj
```

`SHIPPOP_ALLOW_INSECURE_HTTP=1` ใช้ได้เฉพาะ SHIPPOP Dev HTTP endpoint และ
local Development/provider certification เท่านั้น เพราะ API key และข้อมูล
ที่อยู่จะเดินทางโดยไม่มี TLS ห้ามใช้กับ Production

ก่อน test อ่าน synthetic address, API key หรือ account email จะยอมรับเฉพาะ
string `http://mkpservice.shippop.dev` ตามคำสั่งข้างบนเท่านั้น ต้องมี
`SHIPPOP_ALLOW_INSECURE_HTTP=1` ด้วย URL ที่เป็น HTTPS, host อื่น, port,
trailing slash, path หรือ query—even a production host—ถูกปฏิเสธโดยไม่แสดง URL
หรืออ่าน credential

เมื่อไม่มี `SHIPPOP_CERTIFY=1` เฉพาะ live `[CertificationFact]` ต้องรายงาน
`Skipped`; endpoint guard และ deterministic harness แบบ offline ยังต้องทำงาน
เพื่อคง policy guard ไว้:

```bash
dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj \
  --no-restore
```

ไฟล์ JSON เป็น fixture สังเคราะห์ที่ได้รับอนุญาตเท่านั้น ต้องมี
`origin`, `destination`, `originPostalCode`, `destinationPostalCode`,
`parcelName`, `weightGrams`, `widthCentimeters`, `lengthCentimeters`,
`heightCentimeters` และ `declaredValueSatang`.

ทุกค่าเงินใน evidence ต้องเป็น integer satang; ห้ามส่ง decimal THB หรือ
คาดเดาการปัดเศษ ห้ามใส่วงเงินหรือ premium ที่ยังไม่ได้รับจาก response/document
ของ account ลงใน fixture เพื่อทำให้ test ผ่าน

เมื่อ provider เริ่มคืน optional add-on แล้ว fixture จึงเพิ่ม optional object
`certificationEvidence` ได้ โดยมี exact `includedCoverageLimitSatang`,
`maximumCoverageLimitSatang`, `providerCostSatang`, `customerPriceSatang`,
`termsVersion`, `insuranceCode` และ `optionReference` จาก account evidence
จริงเท่านั้น หาก add-on มีแต่ไม่มี object นี้ ผลคือ `blocked` ไม่ใช่ค่าเดา

test ไม่พิมพ์ API key, request body, contact, address, option reference,
tracking, terms text หรือ raw provider response. การรันที่ถึง live assertion
เขียนผลสรุปที่ sanitize แล้วใต้
`TestResults/shippop-certification/`; directory นี้ถูก ignore โดย Git. รายงานมี
เฉพาะ service code, เวลา UTC, unit `satang`, ชื่อ field parcel ที่ส่งพร้อม unit,
และผล `passed`/`blocked`/`failed` ของ assertion เท่านั้น

## Required evidence

บันทึกหนึ่งชุดต่อ account และ service code พร้อม reviewer, วันที่ และ
certification reference. บันทึกเฉพาะชื่อ field จาก provider และ unit ที่เห็น
จริง—not raw response/value/address/contact. ถ้า provider field หรือ unit ไม่ได้
ระบุชัด ให้ใส่ `blocked`, ไม่ใช่ชื่อที่คาดเดา

| Assertion | Evidence that must be recorded | Current adapter status |
|---|---|---|
| included coverage | provider response field name + integer-satang unit + exact value | blocked |
| optional maximum | provider response field name + integer-satang unit + exact value | blocked |
| add-on premium | provider response field name + source unit + exact integer-satang conversion | blocked |
| terms / insurance code | response field names and non-secret code/version shape | blocked |
| Buyer election → booking | documented payload fields that carry the selected option, exact returned fee/coverage/code | blocked |
| safe replay after timeout | documented TOKLONG/idempotency lookup and proof that it returns the original booking | blocked |
| cancellation before first scan | documented cancellable state and exact result without a scan | blocked |
| weight | required request field name and unit | `weight`, grams (adapter input only) |
| width | required request field name and unit | `width`, centimeters (adapter input only) |
| length | required request field name and unit | `length`, centimeters (adapter input only) |
| height | required request field name and unit | `height`, centimeters (adapter input only) |

The opt-in suite calls `IParcelProtectionQuoteProvider` through the same
disabled-by-default SHIPPOP boundary used by the application. Its executable
harness revalidates exact option coverage/cost/terms/code/reference, validates
the integer-satang customer price, and only then asks the separately defined
certification operations boundary for weight/dimension requirements, same-key
booking replay, lookup, and pre-scan cancellation. A missing capability,
evidence object, or lookup result produces a named `blocked` result; a changed
option, booking, replay, lookup, price, or parcel requirement produces `failed`.
It never enables a test profile or guesses a SHIPPOP endpoint or field.

The parcel-requirements operation must return the actual provider field name and
unit for each required dimension. The harness accepts and reports only the
allow-listed evidence `weight:grams`, `width:centimeters`,
`length:centimeters`, and `height:centimeters`; blank, renamed, or mismatched
values fail before a booking. The sanitized report derives these entries from
the returned certification contract rather than from a fixed test fixture.

`IncludedCoverageSatang` may be zero for a certified add-on-only service. The
selected add-on coverage must still be positive, at least the included limit,
and no greater than the documented certified maximum. Under the current
provider option contract, `SelectedCoverageLimitSatang` is that maximum and
must equal the evidence maximum exactly. When included coverage is positive,
the maximum must be strictly greater than it; certification does not require
coverage equal to the item price.

Terms version, insurance code, option reference, provider booking reference,
and lookup reference must each be 1–80 ASCII characters from
`[A-Za-z0-9._-]`. The live report records only named pass/block/fail outcomes,
not the identifier values.

Booking/replay/lookup/cancel are provider mutations. Even after an adapter
implements the certification operations boundary, they remain `blocked` unless
the operator additionally sets `SHIPPOP_CERTIFY_MUTATIONS=1` for a disposable
Dev-only synthetic shipment. Do not set that variable against Production.

## Current provider blocker — 30 July 2026

The checked-in SHIPPOP adapter has no documented, account-certified optional
protection payload. Its protected-booking path deliberately stops before a
provider mutation, and the public shipment boundary has no safe booking lookup
operation. Consequently the live test must fail closed until SHIPPOP supplies
all of the following for the actual account/service:

1. The optional-protection availability/revalidation response field names,
   units, included limit, maximum, premium rounding, terms/version, and
   insurance code.
2. The exact booking payload that binds the Buyer-elected option and the exact
   booking response fields that prove the same coverage/cost/code.
3. A TOKLONG/idempotency-reference lookup that makes a timeout outcome safe to
   reconcile before any replay.
4. Documented repeated-call behavior for booking, confirm, and cancel; and a
   cancellation-before-first-scan contract.
5. Account/service-specific weight and all-dimensions requirements, including
   field names and units.

Do not enable `OptionalProtectionEnabled`, `InsuranceEnabled`, booking, or any
service capability from a skipped test, a test fixture, a screenshot, or a
support assertion. Do not add payload fields until those provider facts are
documented and the live certification passes. Included-only checkout remains
the only permitted path.

## Enablement

After every required row passes for one service, retain the sanitized report
with the provider-owned evidence and set a non-empty `CertificationReference`.
Then make the smallest separately reviewed configuration change: set the
proved included and maximum values, leave unrelated services disabled, and
deploy API and Worker with the same configuration. Any contract drift, timeout
without lookup proof, missing delivery time, or new unknown field/unit closes
the affected capability immediately and opens a CRM case.
