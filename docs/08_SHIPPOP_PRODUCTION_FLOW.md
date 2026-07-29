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
  เต็มมูลค่าสินค้าตลอดช่วงราคาที่แอปรองรับ
- API key, account email และ secret ต้องมาจาก secret storage เท่านั้น
  ห้ามใส่ใน source, migration, log, audit หรือ mobile response

## Outbound flow

1. ผู้ขายเลือกบริการที่ยังไม่หมดอายุและมีประกันเต็มมูลค่า
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
