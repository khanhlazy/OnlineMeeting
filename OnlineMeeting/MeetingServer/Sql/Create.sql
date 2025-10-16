-- Tạo Database và bảng Users (lưu mật khẩu dạng hash + salt)
IF DB_ID('OnlineMeetingDb') IS NULL
BEGIN
  CREATE DATABASE OnlineMeetingDb;
END
GO
USE OnlineMeetingDb;
GO

IF OBJECT_ID('dbo.Users') IS NULL
BEGIN
  CREATE TABLE dbo.Users(
    Username NVARCHAR(50) NOT NULL PRIMARY KEY,
    PasswordHash VARBINARY(32) NOT NULL,
    Salt VARBINARY(16) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  );
END
ELSE
BEGIN
  -- Nâng cấp bảng cũ (nếu tồn tại cột Password plaintext)
  IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'Password' AND Object_ID = OBJECT_ID(N'dbo.Users'))
  BEGIN
    ALTER TABLE dbo.Users ADD PasswordHash VARBINARY(32) NULL, Salt VARBINARY(16) NULL;
    -- Dữ liệu cũ không thể chuyển đổi an toàn; buộc người dùng đặt lại mật khẩu.
    UPDATE dbo.Users SET PasswordHash = 0x, Salt = 0x;
    ALTER TABLE dbo.Users DROP COLUMN Password;
    ALTER TABLE dbo.Users ALTER COLUMN PasswordHash VARBINARY(32) NOT NULL;
    ALTER TABLE dbo.Users ALTER COLUMN Salt VARBINARY(16) NOT NULL;
  END
END
GO

-- Ràng buộc và chỉ mục bổ sung
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Users_CreatedAt' AND object_id = OBJECT_ID('dbo.Users'))
BEGIN
  CREATE INDEX IX_Users_CreatedAt ON dbo.Users(CreatedAt);
END
GO

-- Bảng lưu sự kiện đăng nhập (audit tối giản)
IF OBJECT_ID('dbo.LoginAudit') IS NULL
BEGIN
  CREATE TABLE dbo.LoginAudit(
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    Username NVARCHAR(50) NOT NULL,
    Succeeded BIT NOT NULL,
    Reason NVARCHAR(200) NULL,
    AtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  );
  CREATE INDEX IX_LoginAudit_AtUtc ON dbo.LoginAudit(AtUtc);
END
GO
