using Microsoft.Data.SqlClient;
using System;
using System.Security.Cryptography;

namespace MeetingServer;

public class Db
{
    private readonly string _conn;
    public Db(string conn) => _conn = conn;

    // Sinh salt ngẫu nhiên và hash mật khẩu (SHA-256) theo dạng: Hash(Salt || Password)
    private static (byte[] salt, byte[] hash) HashPassword(string password)
    {
        Span<byte> salt = stackalloc byte[16];
        RandomNumberGenerator.Fill(salt);
        using var sha = SHA256.Create();
        var data = new byte[salt.Length + System.Text.Encoding.UTF8.GetByteCount(password)];
        salt.CopyTo(data);
        System.Text.Encoding.UTF8.GetBytes(password, 0, password.Length, data, salt.Length);
        var hash = sha.ComputeHash(data);
        return (salt.ToArray(), hash);
    }

    // Kiểm tra mật khẩu nhập vào bằng cách hash với salt đã lưu và so sánh hằng thời gian
    private static bool VerifyPassword(string password, byte[] salt, byte[] hash)
    {
        using var sha = SHA256.Create();
        var data = new byte[salt.Length + System.Text.Encoding.UTF8.GetByteCount(password)];
        salt.CopyTo(data, 0);
        System.Text.Encoding.UTF8.GetBytes(password, 0, password.Length, data, salt.Length);
        var computed = sha.ComputeHash(data);
        return CryptographicOperations.FixedTimeEquals(computed, hash);
    }

    // Đăng ký tài khoản: kiểm tra trùng Username, sau đó lưu Hash + Salt
    public async Task<bool> RegisterAsync(string username, string password)
    {
        await using var con = new SqlConnection(_conn);
        await con.OpenAsync();
        await using var check = new SqlCommand("SELECT COUNT(1) FROM dbo.Users WHERE Username=@u", con);
        check.Parameters.AddWithValue("@u", username);
        var exists = Convert.ToInt32(await check.ExecuteScalarAsync() ?? 0) > 0;
        if (exists) return false;
        var (salt, hash) = HashPassword(password);
        await using var cmd = new SqlCommand("INSERT INTO dbo.Users(Username,PasswordHash,Salt) VALUES(@u,@h,@s)", con);
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.Add("@h", System.Data.SqlDbType.VarBinary, 32).Value = hash;
        cmd.Parameters.Add("@s", System.Data.SqlDbType.VarBinary, 16).Value = salt;
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    // Đăng nhập: lấy Salt + Hash, verify và ghi audit vào bảng LoginAudit
    public async Task<bool> LoginAsync(string username, string password)
    {
        await using var con = new SqlConnection(_conn);
        await con.OpenAsync();
        await using var cmd = new SqlCommand("SELECT PasswordHash, Salt FROM dbo.Users WHERE Username=@u", con);
        cmd.Parameters.AddWithValue("@u", username);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            await reader.DisposeAsync();
            // audit fail
            await using var audit0 = new SqlCommand("INSERT INTO dbo.LoginAudit(Username,Succeeded,Reason) VALUES(@u,0,@r)", con);
            audit0.Parameters.AddWithValue("@u", username);
            audit0.Parameters.AddWithValue("@r", "USER_NOT_FOUND");
            await audit0.ExecuteNonQueryAsync();
            return false;
        }
        var hash = (byte[])reader[0];
        var salt = (byte[])reader[1];
        var ok = VerifyPassword(password, salt, hash);
        await reader.DisposeAsync();
        await using (var audit = new SqlCommand("INSERT INTO dbo.LoginAudit(Username,Succeeded,Reason) VALUES(@u,@s,@r)", con))
        {
            audit.Parameters.AddWithValue("@u", username);
            audit.Parameters.AddWithValue("@s", ok ? 1 : 0);
            audit.Parameters.AddWithValue("@r", ok ? (object)DBNull.Value : "BAD_PASSWORD");
            await audit.ExecuteNonQueryAsync();
        }
        return ok;
    }
}
