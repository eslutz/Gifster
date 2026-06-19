IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'gifforge')
BEGIN
  EXEC(N'CREATE SCHEMA gifforge');
END
GO

CREATE TABLE gifforge.users (
  user_id uniqueidentifier NOT NULL CONSTRAINT pk_gifforge_users PRIMARY KEY,
  apple_subject nvarchar(255) NULL,
  app_account_token uniqueidentifier NOT NULL,
  private_relay_email nvarchar(320) NULL,
  created_at datetimeoffset NOT NULL,
  updated_at datetimeoffset NOT NULL,
  deleted_at datetimeoffset NULL,
  CONSTRAINT uq_gifforge_users_app_account_token UNIQUE (app_account_token)
);
GO

CREATE UNIQUE INDEX ux_gifforge_users_apple_subject
ON gifforge.users(apple_subject)
WHERE apple_subject IS NOT NULL;
GO

CREATE TABLE gifforge.refresh_tokens (
  token_hash nvarchar(128) NOT NULL CONSTRAINT pk_gifforge_refresh_tokens PRIMARY KEY,
  user_id uniqueidentifier NOT NULL,
  family_id uniqueidentifier NOT NULL,
  expires_at datetimeoffset NOT NULL,
  created_at datetimeoffset NOT NULL,
  revoked_at datetimeoffset NULL,
  replaced_by_token_hash nvarchar(128) NULL,
  CONSTRAINT fk_gifforge_refresh_tokens_users FOREIGN KEY (user_id) REFERENCES gifforge.users(user_id)
);
GO

CREATE INDEX ix_gifforge_refresh_tokens_user_id ON gifforge.refresh_tokens(user_id);
GO

CREATE TABLE gifforge.iap_products (
  product_id nvarchar(160) NOT NULL CONSTRAINT pk_gifforge_iap_products PRIMARY KEY,
  credits int NOT NULL,
  active bit NOT NULL,
  apple_environment nvarchar(32) NOT NULL,
  created_at datetimeoffset NOT NULL CONSTRAINT df_gifforge_iap_products_created_at DEFAULT SYSUTCDATETIME(),
  CONSTRAINT ck_gifforge_iap_products_credits_positive CHECK (credits > 0)
);
GO

MERGE gifforge.iap_products AS target
USING (VALUES
  (N'dev.ericslutz.gifforge.credits.10', 10, CAST(1 AS bit), N'Sandbox'),
  (N'dev.ericslutz.gifforge.credits.25', 25, CAST(1 AS bit), N'Sandbox'),
  (N'dev.ericslutz.gifforge.credits.55', 55, CAST(1 AS bit), N'Sandbox')
) AS source(product_id, credits, active, apple_environment)
ON target.product_id = source.product_id
WHEN MATCHED THEN
  UPDATE SET credits = source.credits, active = source.active, apple_environment = source.apple_environment
WHEN NOT MATCHED THEN
  INSERT (product_id, credits, active, apple_environment)
  VALUES (source.product_id, source.credits, source.active, source.apple_environment);
GO

CREATE TABLE gifforge.iap_transactions (
  transaction_id nvarchar(128) NOT NULL CONSTRAINT pk_gifforge_iap_transactions PRIMARY KEY,
  original_transaction_id nvarchar(128) NOT NULL,
  user_id uniqueidentifier NOT NULL,
  product_id nvarchar(160) NOT NULL,
  app_account_token uniqueidentifier NOT NULL,
  environment nvarchar(32) NOT NULL,
  signed_payload_hash nvarchar(128) NOT NULL,
  status nvarchar(32) NOT NULL,
  created_at datetimeoffset NOT NULL,
  CONSTRAINT fk_gifforge_iap_transactions_users FOREIGN KEY (user_id) REFERENCES gifforge.users(user_id),
  CONSTRAINT fk_gifforge_iap_transactions_products FOREIGN KEY (product_id) REFERENCES gifforge.iap_products(product_id)
);
GO

CREATE INDEX ix_gifforge_iap_transactions_user_id ON gifforge.iap_transactions(user_id);
GO

CREATE TABLE gifforge.credit_reservations (
  reservation_id uniqueidentifier NOT NULL CONSTRAINT pk_gifforge_credit_reservations PRIMARY KEY,
  user_id uniqueidentifier NOT NULL,
  job_id nvarchar(64) NOT NULL,
  credits int NOT NULL,
  status nvarchar(32) NOT NULL,
  expires_at datetimeoffset NOT NULL,
  created_at datetimeoffset NOT NULL,
  captured_at datetimeoffset NULL,
  released_at datetimeoffset NULL,
  CONSTRAINT uq_gifforge_credit_reservations_job_id UNIQUE (job_id),
  CONSTRAINT fk_gifforge_credit_reservations_users FOREIGN KEY (user_id) REFERENCES gifforge.users(user_id),
  CONSTRAINT ck_gifforge_credit_reservations_credits_positive CHECK (credits > 0),
  CONSTRAINT ck_gifforge_credit_reservations_status CHECK (status IN (N'reserved', N'captured', N'released'))
);
GO

CREATE INDEX ix_gifforge_credit_reservations_user_status ON gifforge.credit_reservations(user_id, status, expires_at);
GO

CREATE TABLE gifforge.usage_ledger (
  ledger_id uniqueidentifier NOT NULL CONSTRAINT pk_gifforge_usage_ledger PRIMARY KEY,
  user_id uniqueidentifier NOT NULL,
  kind nvarchar(32) NOT NULL,
  credits int NOT NULL,
  reference_id nvarchar(160) NOT NULL,
  created_at datetimeoffset NOT NULL,
  CONSTRAINT fk_gifforge_usage_ledger_users FOREIGN KEY (user_id) REFERENCES gifforge.users(user_id),
  CONSTRAINT ck_gifforge_usage_ledger_kind CHECK (kind IN (N'grant', N'capture', N'release', N'reversal', N'adjustment'))
);
GO

CREATE INDEX ix_gifforge_usage_ledger_user_id ON gifforge.usage_ledger(user_id, created_at);
GO

CREATE TABLE gifforge.generation_ownership (
  job_id nvarchar(64) NOT NULL CONSTRAINT pk_gifforge_generation_ownership PRIMARY KEY,
  user_id uniqueidentifier NOT NULL,
  reservation_id uniqueidentifier NOT NULL,
  status nvarchar(32) NOT NULL,
  created_at datetimeoffset NOT NULL,
  CONSTRAINT fk_gifforge_generation_ownership_users FOREIGN KEY (user_id) REFERENCES gifforge.users(user_id),
  CONSTRAINT fk_gifforge_generation_ownership_reservations FOREIGN KEY (reservation_id) REFERENCES gifforge.credit_reservations(reservation_id)
);
GO

CREATE INDEX ix_gifforge_generation_ownership_user_id ON gifforge.generation_ownership(user_id);
GO

CREATE TABLE gifforge.auth_events (
  auth_event_id uniqueidentifier NOT NULL CONSTRAINT pk_gifforge_auth_events PRIMARY KEY,
  user_id uniqueidentifier NULL,
  event_type nvarchar(64) NOT NULL,
  created_at datetimeoffset NOT NULL,
  metadata_json nvarchar(max) NULL
);
GO

CREATE TABLE gifforge.purchase_events (
  purchase_event_id uniqueidentifier NOT NULL CONSTRAINT pk_gifforge_purchase_events PRIMARY KEY,
  user_id uniqueidentifier NULL,
  transaction_id nvarchar(128) NULL,
  event_type nvarchar(64) NOT NULL,
  created_at datetimeoffset NOT NULL,
  metadata_json nvarchar(max) NULL
);
GO
