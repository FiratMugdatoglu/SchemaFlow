# 🔍 SchemaFlow Şema Değişiklik & Breaking Change Raporu

> **Temel Dosya:** `v1_schema.sql`  
> **Hedef Dosya:** `v2_schema.sql`  
> **Tarih:** `2026-11-09 14:39:04 UTC`  

## ⚠️ DİKKAT: Canlı Sistemi Bozabilecek Değişiklikler Bulundu!
- 🔴 **Kritik Değişiklik Sayısı:** `3`
- 🟢 **Güvenli Değişiklik Sayısı:** `1`

### 📋 Değişiklik Listesi
| Durum | Tür | Tablo | Açıklama |
| :---: | :--- | :--- | :--- |
| **🔴 BREAKING** | `ColumnDropped` | `users` | 'users' tablosundan 'email' (VARCHAR(255)) sütunu silindi! |
| **🔴 BREAKING** | `ColumnTypeChanged` | `orders` | 'orders.total' veri tipi değiştirildi: 'INT' ➡️ 'DECIMAL(10,2)' |
| **🔴 BREAKING** | `ColumnAdded` | `orders` | 'orders' tablosuna varsayılan değeri olmayan 'NOT NULL coupon_code' sütunu eklendi! (Mevcut kayıtlar varsa hata verir) |
| **🟢 SAFE** | `TableAdded` | `coupons` | 'coupons' tablosu (3 sütun) sisteme yeni eklendi. |

---
*Bu rapor [SchemaFlow.ai](https://github.com) tarafından otomatik oluşturulmuştur.*
