namespace LefteriaFc.Application.Finance;

public sealed record AdminPayablPaymentCreateBody(
    string? Purpose,string? Title,int? AmountCents,
    string? FirstName,string? LastName,string? Email);
public sealed record AdminPayablPaymentDto(
    string Id,string PublicToken,string Purpose,string Title,
    int AmountCents,string Currency,string Status,long Version,
    DateTimeOffset? CreatedAt=null,DateTimeOffset? PaidAt=null,
    string? GatewayTransactionId=null);
public sealed record AdminPayablPaymentsDto(
    string OrganizationId,IReadOnlyList<AdminPayablPaymentDto> Payments);
public sealed record PayablCheckoutClaimDto(string Id,string OrderId,
    int AmountCents,string PayerFirstName,string PayerLastName,
    string PayerEmail,Guid Lease);
public sealed record PayablCheckoutResultDto(
    string Id,string Status,long Version,string PublicToken);
public sealed record PublicPayablLinkDto(bool Available,string Status,
    string? OrganizationId=null,string? Title=null,int? AmountCents=null,
    string? Currency=null,string? CheckoutUrl=null);
public sealed record PayablNotificationResultDto(bool Accepted,bool Changed);

public interface IPayablFinanceStore
{
    Task<AdminPayablPaymentDto> CreateAsync(string cookie,string owner,
        AdminPayablPaymentCreateBody command,CancellationToken token);
    Task<AdminPayablPaymentsDto> ListAsync(string cookie,string owner,
        CancellationToken token);
    Task<PayablCheckoutClaimDto> ClaimCheckoutAsync(string cookie,string owner,
        string id,Guid lease,CancellationToken token);
    Task<PayablCheckoutResultDto> CompleteCheckoutAsync(string cookie,
        string owner,string id,Guid lease,string? url,string? transaction,
        bool success,CancellationToken token);
    Task<PublicPayablLinkDto> PublicLinkAsync(string publicToken,
        CancellationToken token);
    Task<PayablNotificationResultDto> RecordNotificationAsync(
        string orderId,string transactionId,string transactionType,
        string errorCode,DateTimeOffset timestamp,string digest,
        CancellationToken token);
}
