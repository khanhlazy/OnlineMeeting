namespace MeetingShared;

// DTO đơn giản dùng để truyền thông tin người dùng/participant giữa client và server (nếu cần JSON hoặc mở rộng)
public record UserDto(string Username);
public record ParticipantDto(string Username, bool IsHost, bool CamOn, bool MicOn);
