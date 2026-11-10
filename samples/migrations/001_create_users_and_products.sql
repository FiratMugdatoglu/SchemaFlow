-- Migration 1: Kullanıcılar ve Ürünler
CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    username VARCHAR(50) NOT NULL UNIQUE,
    email VARCHAR(255) NOT NULL UNIQUE,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

COMMENT ON TABLE users IS 'Sisteme kayıtlı kullanıcı hesapları';
COMMENT ON COLUMN users.email IS 'Giriş için kullanılan tekil e-posta';

CREATE TABLE products (
    id SERIAL PRIMARY KEY,
    name VARCHAR(150) NOT NULL,
    price NUMERIC(10, 2) NOT NULL,
    stock_count INT DEFAULT 0
);
