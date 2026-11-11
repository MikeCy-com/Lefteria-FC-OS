namespace LefteriaFc.Application.Messaging;

// Web Push opt-in is scoped to one authenticated mobile identity and one device.
// No endpoint or key material is included in API responses or worker logs.
public sealed record MobilePushKeysBody(string? P256dh,string? Auth);
public sealed record MobilePushSubscriptionBody(string? Endpoint,MobilePushKeysBody? Keys);
public sealed record MobilePushUnsubscribeBody(string? Endpoint);
public sealed record MobilePushSubscriptionStateDto(bool Subscribed);
public sealed record MobilePushConfigDto(bool Enabled,string? PublicKey,int ActiveDevices);
public sealed record MobilePushDeviceCountDto(int ActiveDevices);

// A service acceptance (HTTP 201/202) never proves OS/device presentation.
public sealed record MobilePushLeaseDto(Guid Id,Guid LeaseToken,int Attempts,string Endpoint);
public sealed record MobilePushLeaseBatchDto(IReadOnlyList<MobilePushLeaseDto> Deliveries);
