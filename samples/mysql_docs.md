# 🗄️ Veritabanı Şema Dokümantasyonu

> **Motor (Dialect):** `MySql`  
> **Toplam Tablo Sayısı:** `3`  
> **Oluşturulma Tarihi:** `2026-11-09 14:06:46 UTC`  
> *Bu dokümantasyon [SchemaFlow.ai](https://github.com) tarafından otomatik üretilmiştir.*

## 📑 İçindekiler
- [📊 İlişkisel ER Diyagramı](#-ilişkisel-er-diyagramı)
- [📋 Tablolar](#-tablolar)
  - [public.customers](#customers)
  - [public.invoices](#invoices)
  - [public.invoice_items](#invoice_items)

## 📊 İlişkisel ER Diyagramı
```mermaid
erDiagram
    customers ||--o{ invoices : "fk_invoices_customer"
    invoices ||--o{ invoice_items : "fk_items_invoice"

    customers {
        int id PK
        varchar(150) company_name
        varchar(255) contact_email
        tinyint(1) is_active
        timestamp created_at
    }

    invoices {
        bigint id PK
        int customer_id FK
        varchar(50) invoice_number
        decimal(12_2) total_amount
        enum('draft'_'paid'_'cancelled') status
        date issue_date
    }

    invoice_items {
        bigint id PK
        bigint invoice_id FK
        varchar(255) description
        int quantity
        decimal(10_2) unit_price
    }
```

## 📋 Tablolar

### `public.customers`
#### Sütunlar
| Adı | Tipi | PK | Nullable | Varsayılan | Açıklama |
| :--- | :--- | :---: | :---: | :--- | :--- |
| **id** | `int` | ✅ | **Hayır** | — | — |
| **company_name** | `varchar(150)` | — | **Hayır** | — | — |
| **contact_email** | `varchar(255)` | — | **Hayır** | — | — |
| **is_active** | `tinyint(1)` | — | Evet | `'1'` | — |
| **created_at** | `timestamp` | — | Evet | `CURRENT_TIMESTAMP` | — |

#### İndeksler
| İndeks Adı | Kolonlar | Tekil (Unique)? | Filtre |
| :--- | :--- | :---: | :--- |
| `uq_customers_email` | `contact_email` | ✅ | — |

---

### `public.invoices`
#### Sütunlar
| Adı | Tipi | PK | Nullable | Varsayılan | Açıklama |
| :--- | :--- | :---: | :---: | :--- | :--- |
| **id** | `bigint` | ✅ | **Hayır** | — | — |
| **customer_id** | `int` | — | **Hayır** | — | — |
| **invoice_number** | `varchar(50)` | — | **Hayır** | — | — |
| **total_amount** | `decimal(12,2)` | — | **Hayır** | — | — |
| **status** | `enum('draft','paid','cancelled')` | — | Evet | `'draft'` | — |
| **issue_date** | `date` | — | **Hayır** | — | — |

#### İlişkiler (Foreign Keys)
| Kısıt Adı | Kolon | Hedef Tablo | Hedef Kolon | On Delete |
| :--- | :--- | :--- | :--- | :--- |
| `fk_invoices_customer` | `customer_id` | [customers](#customers) | `id` | `CASCADE` |

#### İndeksler
| İndeks Adı | Kolonlar | Tekil (Unique)? | Filtre |
| :--- | :--- | :---: | :--- |
| `uq_invoices_number` | `invoice_number` | ✅ | — |
| `idx_invoices_customer` | `customer_id` | — | — |

---

### `public.invoice_items`
#### Sütunlar
| Adı | Tipi | PK | Nullable | Varsayılan | Açıklama |
| :--- | :--- | :---: | :---: | :--- | :--- |
| **id** | `bigint` | ✅ | **Hayır** | — | — |
| **invoice_id** | `bigint` | — | **Hayır** | — | — |
| **description** | `varchar(255)` | — | **Hayır** | — | — |
| **quantity** | `int` | — | **Hayır** | `'1'` | — |
| **unit_price** | `decimal(10,2)` | — | **Hayır** | — | — |

#### İlişkiler (Foreign Keys)
| Kısıt Adı | Kolon | Hedef Tablo | Hedef Kolon | On Delete |
| :--- | :--- | :--- | :--- | :--- |
| `fk_items_invoice` | `invoice_id` | [invoices](#invoices) | `id` | `CASCADE` |

#### İndeksler
| İndeks Adı | Kolonlar | Tekil (Unique)? | Filtre |
| :--- | :--- | :---: | :--- |
| `idx_items_invoice` | `invoice_id` | — | — |

---