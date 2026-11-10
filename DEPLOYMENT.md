# 🚀 SchemaFlow.ai - Canlıya Alma ve Entegrasyon Rehberi (Deployment Guide)

Bu rehber, SchemaFlow.ai'yi yerel geliştirme ortamından çıkarıp **canlı bir SaaS ürününe** dönüştürmeniz için gereken adımları 5 dakikada tamamlamanızı sağlar.

---

## 📑 İçindekiler
1. [Veritabanı Kurulumu (Supabase - 2 Dakika)](#1-veritabanı-kurulumu-supabase)
2. [Ödeme Altyapısı (Lemon Squeezy - 3 Dakika)](#2-ödeme-altyapısı-lemon-squeezy)
3. [Web API ve Stüdyo Dağıtımı (Fly.io / Render / Railway)](#3-web-api-ve-stüdyo-dağıtımı)
4. [GitHub Action Marketplace Yayınlama](#4-github-action-marketplace-yayınlama)

---

## 1. Veritabanı Kurulumu (Supabase)

SchemaFlow, kullanıcı yönetimi, şema geçmişi ve abonelik durumları için **Supabase (PostgreSQL)** kullanır.

1. [Supabase.com](https://supabase.com)'a gidin ve ücretsiz bir hesap açın.
2. **New Project** butonuna tıklayarak yeni bir veritabanı projesi oluşturun (örn: `schemaflow-prod`).
3. Sol menüden **SQL Editor** sekmesine geçin.
4. Repomuzdaki `database/supabase_schema.sql` dosyasının içeriğini kopyalayıp editöre yapıştırın ve **Run** butonuna basın.
   * ✅ `profiles` (Kullanıcı profilleri & abonelik paketleri)
   * ✅ `projects` (Kullanıcı projeleri)
   * ✅ `schema_versions` (Şema sürümleri & diff geçmişi)
   * ✅ Row Level Security (RLS) güvenlik politikaları otomatik olarak kurulacaktır.
5. **Project Settings -> API** bölümünden şu değerleri not edin:
   * `Project URL`
   * `anon / public key`

---

## 2. Ödeme Altyapısı (Lemon Squeezy)

Lemon Squeezy, Merchant of Record (MoR) olarak global KDV/vergi işlemlerini sizin yerinize yönetir.

1. [LemonSqueezy.com](https://lemonsqueezy.com)'a gidip bir hesap açın ve mağazanızı (Store) oluşturun.
2. **Products** sekmesine giderek iki abonelik ürünü ekleyin:
   * **SchemaFlow Pro:** $9 / ay (veya $79 / yıl)
   * **SchemaFlow Team:** $29 / ay (veya $249 / yıl)
3. Sol menüden **Settings -> Webhooks** bölümüne gidin ve **Add Webhook** deyin:
   * **Callback URL:** `https://api.schemaflow.ai/api/v1/billing/webhook` (veya canlı API adresiniz)
   * **Signing Secret:** Rastgele güçlü bir gizli anahtar oluşturun (örn: `whsec_9a8b7c6d...`).
   * **Events:** Şu olayları işaretleyin:
     * `subscription_created`
     * `subscription_updated`
     * `subscription_cancelled`
     * `subscription_expired`
4. Bu `Signing Secret` değerini bir sonraki adımda API çevre değişkenlerine ekleyeceksiniz.

---

## 3. Web API ve Stüdyo Dağıtımı

API ve Web Stüdyomuz `Dockerfile.api` sayesinde her bulut platformunda tek tıkla çalışır.

### Seçenek A: Fly.io ile Dağıtım (Önerilen - Ücretsiz/Çok Ucuz)
```bash
# 1. Fly CLI ile giriş yapın
fly auth login

# 2. Uygulamayı Dockerfile.api ile başlatın
fly launch --dockerfile Dockerfile.api --name schemaflow-api

# 3. Gizli anahtarlarınızı çevre değişkeni olarak tanımlayın
fly secrets set LemonSqueezy__WebhookSecret="whsec_sizin_secretiniz"
fly secrets set LemonSqueezy__ApiKey="sizin_lemon_api_keyiniz"

# 4. Canlıya alın
fly deploy
```

### Seçenek B: Render veya Railway ile Dağıtım
1. [Render.com](https://render.com) veya [Railway.app](https://railway.app)'a gidin.
2. GitHub reponuzu bağlayın.
3. Build ayarlarında **Dockerfile Path** olarak `Dockerfile.api` seçin.
4. **Environment Variables** bölümüne şunları ekleyin:
   * `LemonSqueezy__WebhookSecret`: Lemon Squeezy'de belirlediğiniz secret.
   * `ASPNETCORE_ENVIRONMENT`: `Production`
5. **Deploy** butonuna basın. API'niz ve Web Stüdyonuz dakikalar içinde canlıda!

---

## 4. GitHub Action Marketplace Yayınlama

Geliştiricilerin `uses: schemaflow/schemaflow-action@v1` şeklinde Action'ı kendi repolarında kullanabilmesi için:

1. Bu repoyu GitHub'a **Public** olarak yükleyin.
2. Repo anasayfasında sağdaki **Releases -> Draft a new release** seçeneğine tıklayın.
3. **"Publish this Action to the GitHub Marketplace"** kutucuğunu işaretleyin.
4. Etiket (tag) olarak `v1.0.0` girin.
5. **Publish Release** butonuna tıklayın. 
🎉 SchemaFlow Action artık resmi GitHub Marketplace vitrininde milyonlarca geliştiricinin erişimine açık!

---

## 🔑 Çevre Değişkenleri (Environment Variables) Özeti

| Değişken Adı | Açıklama | Örnek Değer |
| :--- | :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | Çalışma ortamı | `Production` |
| `ASPNETCORE_HTTP_PORTS` | Dinlenecek port | `8080` |
| `LemonSqueezy__WebhookSecret` | Webhook imza doğrulama anahtarı | `whsec_xyz123...` |
| `LemonSqueezy__StoreId` | Lemon Squeezy Mağaza ID | `12345` |
| `Supabase__Url` | Supabase Proje URL'si | `https://xyz.supabase.co` |
| `Supabase__AnonKey` | Supabase Anonim İstemci Anahtarı | `eyJh...` |
