# SHIPPOP certification runbook

ทุกช่องต้องมีหลักฐานจากบัญชีและ environment ที่จะใช้งานจริง ผล “ไม่ทราบ”
ถือว่าไม่ผ่านและ capability นั้นต้องปิดต่อไป ห้ามใช้ credential ที่เคยส่งผ่าน
แชตหรือ commit; ให้ rotate ก่อนเริ่ม certification

## Safe setup

กำหนด secret ผ่าน environment เท่านั้น:

```text
SHIPPOP_CERTIFY=1
SHIPPOP_BASE_URL=http://mkpservice.shippop.dev
SHIPPOP_ALLOW_INSECURE_HTTP=1
SHIPPOP_API_KEY=<secret>
SHIPPOP_ACCOUNT_EMAIL=<secret>
SHIPPOP_SERVICE_CODE=EMST
SHIPPOP_SYNTHETIC_ADDRESS_JSON=/absolute/path/synthetic-address.json
```

ใช้ชื่อ เบอร์ และที่อยู่สังเคราะห์ที่ได้รับอนุญาต ห้ามใช้ข้อมูลลูกค้าจริง
ไฟล์ JSON ต้องมี `origin`, `destination`, parcel dimensions,
`declaredValueSatang` และ postal codes ตามที่ certification test อ่าน

`SHIPPOP_ALLOW_INSECURE_HTTP=1` ใช้ได้เฉพาะเมื่อบัญชี Dev ของ SHIPPOP
ให้ endpoint แบบ HTTP เท่านั้น การตั้งค่านี้เป็น explicit opt-in เพราะ API key
และข้อมูลที่อยู่จะเดินทางโดยไม่มี TLS ห้ามใช้กับ Production

รันจาก repository root:

```bash
./scripts/shippop-certify.sh
```

test ไม่พิมพ์ API key, request body, contact, address หรือ raw response
เมื่อไม่ได้ตั้ง `SHIPPOP_CERTIFY=1` test จะรายงานเป็น `Skipped` อย่างชัดเจน
ไม่รายงานผ่านแบบ no-op

## Evidence matrix

ทำตารางหนึ่งชุดต่อ service และบันทึก reviewer กับวันที่

| Capability | EMST | FLE | KRYX | KRYS |
|---|---|---|---|---|
| quote fields/units | ☐ | ☐ | ☐ | ☐ |
| full-value insurance code/value/premium | ☐ | ☐ | ☐ | ☐ |
| unconfirmed booking | ☐ | ☐ | ☐ | ☐ |
| lookup by TOKLONG/idempotency reference | ☐ | ☐ | ☐ | ☐ |
| safe timeout reconciliation | ☐ | ☐ | ☐ | ☐ |
| confirm | ☐ | ☐ | ☐ | ☐ |
| 4×6 label | ☐ | ☐ | ☐ | ☐ |
| trusted first-scan timestamp | ☐ | ☐ | ☐ | ☐ |
| in-transit normalization | ☐ | ☐ | ☐ | ☐ |
| delivery/POD timestamp | ☐ | ☐ | ☐ | ☐ |
| complete without timestamp fails closed | ☐ | ☐ | ☐ | ☐ |
| cancel before scan | ☐ | ☐ | ☐ | ☐ |
| cancel after scan rejected | ☐ | ☐ | ☐ | ☐ |
| surcharge evidence/reference | ☐ | ☐ | ☐ | ☐ |
| managed return with distinct references | ☐ | ☐ | ☐ | ☐ |
| rate-limit and retry contract | ☐ | ☐ | ☐ | ☐ |
| reviewer / date / certification ref | ☐ | ☐ | ☐ | ☐ |

## Enablement

หลังทุกช่องที่จำเป็นผ่านแล้ว:

1. เก็บหลักฐานและกำหนด certification reference ที่ตรวจย้อนกลับได้
2. ตั้ง `MaximumCoverageSatang` จากขีดจำกัดที่พิสูจน์แล้ว
3. เปิด `QuoteEnabled` ก่อน และตรวจ metrics/error rate
4. เปิด booking ได้เมื่อ `OperationLookupEnabled` และ `InsuranceEnabled`
   ผ่านแล้วเท่านั้น
5. เปิด confirm, return และ capability อื่นแยกกัน
6. deploy API และ Worker ด้วย configuration เดียวกัน
7. เฝ้าดู pending age, expired lease, outcome unknown, retry,
   confirmation/tracking lag, cancellation backlog, missing delivery time,
   surcharge และ open cases
8. หากพบ contract drift ให้ปิด capability ที่เกี่ยวข้องทันทีและเปิด CRM case

## Current status

ณ วันที่ 29 กรกฎาคม 2026 ทุก capability ของ `EMST`, `FLE`, `KRYX` และ
`KRYS` ยังปิดอยู่ เพราะยังไม่มี account-specific certification ของ insurance,
operation lookup/idempotent replay, trusted POD time, surcharge และ return
contract ที่ครบถ้วน
