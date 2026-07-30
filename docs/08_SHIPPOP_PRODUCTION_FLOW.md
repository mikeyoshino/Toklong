# SHIPPOP production flow

เอกสารนี้เป็นกฎปฏิบัติของ integration ระหว่าง TOKLONG กับ SHIPPOP สำหรับ
สินค้าที่จัดส่งได้จริง โดยเอกสาร product และ state machine หลักยังมีลำดับ
ความสำคัญสูงกว่าเสมอ

## Capability gate

- `EMST`, `FLE`, `KRYX` และ `KRYS` ปิดแยกกันเป็นราย capability ตั้งแต่
  quote, outbound booking, confirm, return และ insurance
- เปิด capability ได้ต่อเมื่อมีผล certification ของบัญชีจริงและใส่
  `CertificationReference`
- รองรับเฉพาะ `DropOff`
- outbound booking ต้องมี operation lookup ที่พิสูจน์ผลเดิมได้ และมีประกัน
  ตาม maximum ที่ provider รับรองสำหรับ account/service นั้น
- API key, account email และ secret ต้องมาจาก secret storage เท่านั้น
  ห้ามใส่ใน source, migration, log, audit หรือ mobile response
- endpoint ของ Production ต้องเป็น HTTPS เสมอ ส่วน HTTP ของ SHIPPOP Dev
  เปิดได้เฉพาะ Development/certification ด้วย explicit opt-in และค่าเริ่มต้น
  ต้องปิด
- certification harness รับเฉพาะ exact Dev origin
  `http://mkpservice.shippop.dev` พร้อม insecure opt-in ก่อนอ่าน credential
  หรือ synthetic address; ห้ามใช้ HTTPS/Production/host, port, path หรือ query
  อื่นเพื่อ certification
- optional parcel protection ปิดอยู่จนกว่า account/service เดียวกันจะพิสูจน์
  included limit, maximum, premium conversion เป็น integer satang, terms/code,
  Buyer-elected booking result, safe timeout lookup/replay, cancellationก่อน
  first scan และ weight/width/length/height field/unit ครบทั้งหมด
- certification ต้องคืนชื่อ field และ unit ของ provider สำหรับ weight, width,
  length, height ที่ผ่าน allow-list ที่ sanitize แล้ว; รายงานต้องมาจาก contract
  ที่คืนจริง ไม่ใช่ fixture. `IncludedCoverageSatang = 0` ใช้ได้สำหรับ add-on
  ที่ได้รับการรับรอง แต่ selected coverage ต้องมากกว่า 0 และไม่เกิน maximum
  ที่เปิดเผย (ไม่บังคับให้เท่าราคาสินค้า)
- หาก SHIPPOP ไม่ให้ optional-protection payload ที่แยกได้หลัง Buyer election
  ห้ามสร้างหรือเดาชื่อ field; บันทึกเป็น provider blocker และใช้
  included-only checkout ต่อไป

## Outbound flow

1. ผู้ขายเลือกบริการที่ยังไม่หมดอายุและมีวงเงินคุ้มครองตามที่รับรอง
2. API บันทึก immutable shipment intent กับ `BookOutbound` ใน transaction
   เดียวกัน แล้วตอบ `202 Accepted`
3. ผู้ซื้อยังชำระไม่ได้ระหว่าง booking ค้างอยู่
4. Worker claim งานด้วย lease และ idempotency key แล้วจึงเรียก SHIPPOP
5. เมื่อผล provider ตรงกับ carrier, service, ค่าจัดส่ง, ค่าเบี้ย,
   declared value และ insurance code ที่เลือกไว้ ระบบจึงบันทึก reservation
   และเริ่มเวลาชำระ 1 ชั่วโมง
6. payment-provider webhook ที่ตรวจลายเซ็นแล้วเท่านั้นที่ queue
   `ConfirmOutbound`
7. label พร้อมแต่ยังไม่มี trusted scan แสดง “เตรียมจัดส่ง”
8. trusted first scan แสดง “ขนส่งรับพัสดุแล้ว”
9. trusted in-transit event แสดง “กำลังจัดส่ง”
10. trusted delivered event ที่มีเวลาจาก carrier แสดง “ส่งถึงแล้ว” และเริ่ม
    inspection window 72 ชั่วโมงจากเวลานั้น

## Timeout and retry

- ทุก mutation commit operation ก่อนเรียก provider
- definite failure ที่ยังไม่ได้ส่งคำขอ retry ด้วย exponential backoff และ
  jitter ได้ไม่เกินจำนวนครั้งที่กำหนด
- timeout หลังอาจส่งคำขอแล้วเป็น `OutcomeUnknown` และห้ามยิง mutation ซ้ำ
- retry `OutcomeUnknown` ได้เฉพาะเมื่อ certified lookup พิสูจน์ว่า replay
  ปลอดภัย มิฉะนั้นส่ง CRM review
- idempotency key มี unique index ทั้งระบบ และ request fingerprint ต้องตรงกับ
  intent เดิม

## Tracking and money gates

- เก็บ tracking event แบบ idempotent และคงเวลา carrier occurrence แยกจากเวลา
  ที่ระบบรับข้อมูล
- `complete` ที่ไม่มี trusted delivery time ห้ามตั้ง `DeliveredAt`,
  inspection deadline หรือ payout deadline
- `problem`, `invalid`, `return`, unknown status, tracking mismatch,
  surcharge, `OutcomeUnknown`, `NeedsReview` และ insurance case ที่ยังเปิด
  ต้องกัน payout และ automatic refund
- ผู้ซื้อยืนยันรับของเพื่อ release เร็วได้เมื่อไม่มี dispute หรือ shipping
  exception และ payment พร้อมจ่ายเท่านั้น
- AI ช่วยจัดหมวดหรือสรุปหลักฐานได้ แต่ไม่มีสิทธิ์ตัดสิน refund หรือ payout

## Expiry and cancellation

- เวลาชำระหมดแล้วเปลี่ยนรายการเป็น `Expired / BuyerDidNotPay` ทันที
- queue `CancelOutbound` แยกต่างหากโดยไม่ยืดเวลาชำระ
- ถ้าเริ่มมี trusted carrier scan แล้ว ระบบห้ามยกเลิก shipment อัตโนมัติและ
  ต้องเข้ากระบวนการ review

## Return and insurance

- return ใช้ `ManagedShipment` คนละรายการกับ outbound และห้าม reuse purchase
  หรือ tracking reference
- ต้นทาง return คือปลายทาง outbound และปลายทาง return คือต้นทาง outbound
- TOKLONG รับผิดชอบต้นทุน return ใน operational adjustment; ห้ามแก้ paid
  snapshot, buyer total หรือ seller expected net
- refund เริ่มไม่ได้จนกว่าจะมี trusted return delivery หรือ authorized manual
  return resolution
- insurance resolution บันทึกผล provider เท่านั้น ไม่ทำ payout/refund
  transition โดยตัวมันเอง

## Consumer disclosure

- ผู้ซื้อเห็นราคาสินค้า ค่าจัดส่ง ค่าประกัน ค่าคุ้มครอง และยอดรวมก่อนจ่าย
- ผู้ขายเห็นราคาสินค้า ค่าจัดส่ง ค่าประกัน มูลค่าที่เอาประกัน และยอดรับสุทธิ
  แต่ไม่เห็นค่าคุ้มครองผู้ซื้อหรือยอดรวมฝั่งผู้ซื้อ
- mobile refresh อ่านข้อมูลจากฐานข้อมูลเท่านั้น ห้ามทำให้เกิด SHIPPOP mutation
  หรือ tracking poll

## Implementation status — 30 July 2026

ฝั่ง application ที่ทำเสร็จและทดสอบแล้ว:

- signed quote รุ่น `sp2` ผูกค่าขนส่ง ค่าเบี้ย มูลค่าประกัน insurance code,
  service, parcel, ต้นทาง/ปลายทาง และเวลาหมดอายุ
- timeout, HTTP error, response ผิดรูปแบบ หรือผล mutation ที่พิสูจน์ไม่ได้
  ถูกบันทึกเป็น `OutcomeUnknown`; Worker ไม่ยิง mutation ซ้ำ
- Worker ตรวจ request fingerprint จาก immutable shipment intent ก่อนเรียก
  provider และส่งงานที่ไม่ตรงเข้า review
- เมื่อเลย ship-by ระบบ queue `CancelOutbound` แบบ durable ก่อน refund
- return ใช้ `BookReturn → ConfirmReturn`, มีใบปะหน้าสำหรับผู้ซื้อ, poll
  tracking แยกจาก outbound, บันทึกต้นทุนที่ TOKLONG อนุมัติเป็น operational
  adjustment โดยไม่แก้ paid snapshot และ refund รอ trusted return delivery
- `problem`, `invalid`, `return` และ status ที่ไม่รู้จักเข้า carrier exception;
  return exception และ tracking-unverified block automatic outcome
- post-payment adjustment ปิดได้เฉพาะ authorized CRM พร้อม audit; adjustment
  ที่ปิดแล้วไม่ค้าง block รายการตลอดไป
- CRM ปิด carrier exception ที่ระดับ transaction และ shipment พร้อมกัน,
  รองรับ authorized manual return resolution และให้ retry
  `OutcomeUnknown`/`NeedsReview` ได้เฉพาะเมื่อใส่หลักฐานผลตรวจ provider
- สถานะรอ เช่น `booking` ที่ไม่มี carrier event อัปเดตเพียงเวลาตรวจล่าสุด
  โดยไม่เปลี่ยน shipment เป็น tracking-unverified

สิ่งที่ยังเป็น provider certification blocker และห้ามเปิด capability:

- optional-protection availability/revalidation/booking payload field names,
  units, limits, premium rounding, terms/code และ exact Buyer-election result
- certification operations boundary สำหรับ required parcel fields, same-key
  booking replay, lookup หลัง timeout และ cancel ก่อน first scan; adapter
  ปัจจุบันยังไม่ implement จึงได้ผล `blocked` แบบมีชื่อ capability
- lookup รายการ booking เดิมด้วย TOKLONG reference หลัง timeout และ repeated
  booking/confirm/cancel semantics ที่ทำให้ replay ปลอดภัย
- cancellation ก่อน first scan, trusted POD timestamp, surcharge schema,
  return contract และ rate limit ต่อ service
- weight และทุก dimension requirement/field/unit ที่ยืนยันโดย account จริง

โค้ดจึงคง `EMST`, `FLE`, `KRYX`, `KRYS` และ optional parcel protection เป็น
disabled-by-default และไม่สร้างค่าประกันหรือ provider contract ที่ SHIPPOP
ยังไม่ได้ยืนยัน
