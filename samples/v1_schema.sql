CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    email VARCHAR(255) UNIQUE NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE orders (
    id BIGSERIAL PRIMARY KEY,
    user_id INT NOT NULL,
    total INT NOT NULL,
    status VARCHAR(20) DEFAULT 'pending',
    CONSTRAINT fk_orders_user FOREIGN KEY (user_id) REFERENCES users(id)
);
