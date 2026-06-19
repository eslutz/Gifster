IF COL_LENGTH(N'gifforge.users', N'apple_subject') IS NOT NULL
BEGIN
  DECLARE @constraint_name sysname;

  SELECT @constraint_name = kc.name
  FROM sys.key_constraints kc
  INNER JOIN sys.index_columns ic
    ON ic.object_id = kc.parent_object_id
   AND ic.index_id = kc.unique_index_id
  INNER JOIN sys.columns c
    ON c.object_id = ic.object_id
   AND c.column_id = ic.column_id
  WHERE kc.parent_object_id = OBJECT_ID(N'gifforge.users')
    AND kc.type = N'UQ'
    AND c.name = N'apple_subject';

  IF @constraint_name IS NOT NULL
  BEGIN
    EXEC(N'ALTER TABLE gifforge.users DROP CONSTRAINT ' + QUOTENAME(@constraint_name));
  END;

  IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'gifforge.users')
      AND name = N'apple_subject'
      AND is_nullable = 0
  )
  BEGIN
    ALTER TABLE gifforge.users ALTER COLUMN apple_subject nvarchar(255) NULL;
  END;

  IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'gifforge.users')
      AND name = N'ux_gifforge_users_apple_subject'
  )
  BEGIN
    CREATE UNIQUE INDEX ux_gifforge_users_apple_subject
    ON gifforge.users(apple_subject)
    WHERE apple_subject IS NOT NULL;
  END;
END;
GO
