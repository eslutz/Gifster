MERGE gifforge.iap_products AS target
USING (VALUES
  (N'dev.ericslutz.gifforge.credits.55', 55, CAST(1 AS bit), N'Sandbox')
) AS source(product_id, credits, active, apple_environment)
ON target.product_id = source.product_id
WHEN MATCHED THEN
  UPDATE SET credits = source.credits, active = source.active, apple_environment = source.apple_environment
WHEN NOT MATCHED THEN
  INSERT (product_id, credits, active, apple_environment)
  VALUES (source.product_id, source.credits, source.active, source.apple_environment);
GO

UPDATE gifforge.iap_products
SET active = CAST(0 AS bit)
WHERE product_id = N'dev.ericslutz.gifforge.credits.60';
GO
