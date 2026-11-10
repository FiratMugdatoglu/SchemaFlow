-- ==============================================================================
-- SchemaFlow.ai - Supabase Veritabanı Şeması & Güvenlik Politikaları (RLS)
-- ==============================================================================

-- 1. PROFILES Tablosu (auth.users tablosu ile 1-e-1 eşleşir)
CREATE TABLE public.profiles (
    id UUID PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    email VARCHAR(255) NOT NULL,
    full_name VARCHAR(150),
    subscription_tier VARCHAR(50) DEFAULT 'free', -- 'free', 'pro', 'team'
    subscription_status VARCHAR(50) DEFAULT 'active', -- 'active', 'past_due', 'canceled'
    lemon_squeezy_customer_id VARCHAR(100),
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

COMMENT ON TABLE public.profiles IS 'Kullanıcı profilleri ve abonelik/fatura durumları';

-- 2. PROJECTS Tablosu (Kullanıcının veritabanı projeleri)
CREATE TABLE public.projects (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES public.profiles(id) ON DELETE CASCADE,
    name VARCHAR(150) NOT NULL,
    slug VARCHAR(150) NOT NULL,
    description TEXT,
    dialect VARCHAR(50) DEFAULT 'postgres',
    is_public BOOLEAN DEFAULT false, -- Paylaşılabilir canlı linkler için
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    CONSTRAINT uq_user_project_slug UNIQUE (user_id, slug)
);

COMMENT ON TABLE public.projects IS 'Kullanıcıların SchemaFlow üzerinde oluşturduğu projeler';

-- 3. SCHEMA_VERSIONS Tablosu (Şema geçmişi & Versiyonlama / Time-travel)
CREATE TABLE public.schema_versions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES public.projects(id) ON DELETE CASCADE,
    version_number INT NOT NULL DEFAULT 1,
    commit_hash VARCHAR(100), -- GitHub Action'dan geldiyse commit hash'i
    commit_message TEXT,
    schema_json JSONB NOT NULL, -- Core motorumuzun ürettiği DatabaseSchema AST nesnesi
    mermaid_code TEXT NOT NULL, -- İnteraktif erDiagram sözdizimi
    markdown_docs TEXT NOT NULL, -- Zengin veri sözlüğü dokümantasyonu
    total_tables INT NOT NULL DEFAULT 0,
    total_relations INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

COMMENT ON TABLE public.schema_versions IS 'Projelerin zaman içindeki şema değişim geçmişi';

-- İndeksler (Sorgu Hızlandırma)
CREATE INDEX idx_projects_user_id ON public.projects (user_id);
CREATE INDEX idx_schema_versions_project_id ON public.schema_versions (project_id);
CREATE INDEX idx_schema_versions_created_at ON public.schema_versions (created_at DESC);

-- ==============================================================================
-- 4. Satır Düzeyinde Güvenlik (Row Level Security - RLS)
-- ==============================================================================
ALTER TABLE public.profiles ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.projects ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.schema_versions ENABLE ROW LEVEL SECURITY;

-- Profiles: Kullanıcı sadece kendi profilini görebilir ve güncelleyebilir
CREATE POLICY "Kullanıcı kendi profilini görebilir" 
    ON public.profiles FOR SELECT 
    USING (auth.uid() = id);

CREATE POLICY "Kullanıcı kendi profilini güncelleyebilir" 
    ON public.profiles FOR UPDATE 
    USING (auth.uid() = id);

-- Projects: Kullanıcı kendi projelerini yönetebilir veya halka açık projeleri görebilir
CREATE POLICY "Kullanıcı kendi projelerini veya halka açık projeleri görebilir" 
    ON public.projects FOR SELECT 
    USING (auth.uid() = user_id OR is_public = true);

CREATE POLICY "Kullanıcı kendi projelerini ekleyebilir" 
    ON public.projects FOR INSERT 
    WITH CHECK (auth.uid() = user_id);

CREATE POLICY "Kullanıcı kendi projelerini güncelleyebilir" 
    ON public.projects FOR UPDATE 
    USING (auth.uid() = user_id);

CREATE POLICY "Kullanıcı kendi projelerini silebilir" 
    ON public.projects FOR DELETE 
    USING (auth.uid() = user_id);

-- Schema Versions: Proje sahibi veya proje halka açıksa sürümler görülebilir
CREATE POLICY "Proje erişimi olanlar sürümleri görebilir" 
    ON public.schema_versions FOR SELECT 
    USING (
        EXISTS (
            SELECT 1 FROM public.projects p 
            WHERE p.id = project_id AND (p.user_id = auth.uid() OR p.is_public = true)
        )
    );

CREATE POLICY "Proje sahibi yeni sürüm ekleyebilir" 
    ON public.schema_versions FOR INSERT 
    WITH CHECK (
        EXISTS (
            SELECT 1 FROM public.projects p 
            WHERE p.id = project_id AND p.user_id = auth.uid()
        )
    );

-- ==============================================================================
-- 5. Otomatik Profil Oluşturma Tetikleyicisi (Auth Trigger)
-- ==============================================================================
CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO public.profiles (id, email, full_name)
    VALUES (new.id, new.email, new.raw_user_meta_data->>'full_name');
    RETURN new;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

CREATE TRIGGER on_auth_user_created
    AFTER INSERT ON auth.users
    FOR EACH ROW EXECUTE PROCEDURE public.handle_new_user();
