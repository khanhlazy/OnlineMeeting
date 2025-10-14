using Microsoft.Data.SqlClient;
using System;

namespace MeetingServer;

public class Db
{
    private readonly string _conn;
    public Db(string conn) => _conn = conn;

    public async Task<bool> RegisterAsync(string username, string password)
    {
        await using var con = new SqlConnection(_conn);
        await con.OpenAsync();
        await using var check = new SqlCommand("SELECT COUNT(1) FROM dbo.Users WHERE Username=@u", con);
        check.Parameters.AddWithValue("@u", username);
        var exists = Convert.ToInt32(await check.ExecuteScalarAsync() ?? 0) > 0;
        if (exists) return false;
        await using var cmd = new SqlCommand("INSERT INTO dbo.Users(Username,Password) VALUES(@u,@p)", con);
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@p", password);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        await using var con = new SqlConnection(_conn);
        await con.OpenAsync();
        await using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Users WHERE Username=@u AND Password=@p", con);
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@p", password);
        var ok = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0) > 0;
        return ok;
    }
}
