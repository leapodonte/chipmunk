-- 每个员工演示账号独立的随机开发凭据；前端开发密钥不能登录员工账号。
CREATE TABLE platform_staff_credential(
 user_id text PRIMARY KEY REFERENCES platform_user(id), token_hash text NOT NULL,
 revoked boolean NOT NULL DEFAULT false, created_at timestamptz NOT NULL DEFAULT now(),
 CHECK(token_hash ~ '^[0-9a-f]{64}$'));
