namespace MeetingShared;

public record UserDto(string Username);
public record ParticipantDto(string Username, bool IsHost, bool CamOn, bool MicOn);
