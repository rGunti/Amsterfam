namespace Amsterfam.Api.Dtos;

public record PaymentMethodResponse(
    int Id,
    string Title,
    string? Icon,
    string? Link,
    string? Description
);

public record UpsertPaymentMethodRequest(
    string Title,
    string? Icon,
    string? Link,
    string? Description
);
