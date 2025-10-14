-- Tạo Database và bảng Users đơn giản (mật khẩu plaintext chỉ cho demo)
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
    Password NVARCHAR(200) NOT NULL
  );
END
GO
