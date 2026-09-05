using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Tests.Infrastructure;

namespace Amsterfam.Tests.Api;

public class PaymentMethodApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task GetMine_Returns401_WhenUnauthenticated()
    {
        var client = api.CreateClient();
        var response = await client.GetAsync("/api/v1/me/payment-methods/");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateAndListMine_RoundTrips()
    {
        var client = api.CreateClientWithUser("discord|pm-create");

        var create = await client.PostAsJsonAsync(
            "/api/v1/me/payment-methods/",
            new UpsertPaymentMethodRequest("Wise", "wise", "https://wise.com/pay/me", null)
        );
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<PaymentMethodResponse>();
        Assert.NotNull(created);
        Assert.Equal("Wise", created!.Title);

        var list = await (
            await client.GetAsync("/api/v1/me/payment-methods/")
        ).Content.ReadFromJsonAsync<List<PaymentMethodResponse>>();
        Assert.Single(list!);
        Assert.Equal(created.Id, list![0].Id);
    }

    [Fact]
    public async Task Update_ChangesFields_WhenOwnedByCurrentUser()
    {
        var client = api.CreateClientWithUser("discord|pm-update");

        var created = await (
            await client.PostAsJsonAsync(
                "/api/v1/me/payment-methods/",
                new UpsertPaymentMethodRequest("PayPal", "paypal", "https://paypal.me/x", null)
            )
        ).Content.ReadFromJsonAsync<PaymentMethodResponse>();

        var update = await client.PutAsJsonAsync(
            $"/api/v1/me/payment-methods/{created!.Id}",
            new UpsertPaymentMethodRequest(
                "PayPal (updated)",
                "paypal",
                "https://paypal.me/y",
                "prefer friends & family"
            )
        );
        update.EnsureSuccessStatusCode();
        var updated = await update.Content.ReadFromJsonAsync<PaymentMethodResponse>();
        Assert.Equal("PayPal (updated)", updated!.Title);
        Assert.Equal("prefer friends & family", updated.Description);
    }

    [Fact]
    public async Task Update_Returns404_WhenNotOwnedByCurrentUser()
    {
        var owner = api.CreateClientWithUser("discord|pm-owner");
        var intruder = api.CreateClientWithUser("discord|pm-intruder");

        var created = await (
            await owner.PostAsJsonAsync(
                "/api/v1/me/payment-methods/",
                new UpsertPaymentMethodRequest("Bank transfer", null, null, "ask for IBAN")
            )
        ).Content.ReadFromJsonAsync<PaymentMethodResponse>();

        var response = await intruder.PutAsJsonAsync(
            $"/api/v1/me/payment-methods/{created!.Id}",
            new UpsertPaymentMethodRequest("Hijacked", null, null, null)
        );
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesMethod_WhenOwnedByCurrentUser()
    {
        var client = api.CreateClientWithUser("discord|pm-delete");

        var created = await (
            await client.PostAsJsonAsync(
                "/api/v1/me/payment-methods/",
                new UpsertPaymentMethodRequest("bunq", "bunq", "https://bunq.me/x", null)
            )
        ).Content.ReadFromJsonAsync<PaymentMethodResponse>();

        var delete = await client.DeleteAsync($"/api/v1/me/payment-methods/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var list = await (
            await client.GetAsync("/api/v1/me/payment-methods/")
        ).Content.ReadFromJsonAsync<List<PaymentMethodResponse>>();
        Assert.Empty(list!);
    }

    [Fact]
    public async Task GetForUser_IsVisibleToOtherAuthenticatedUsers()
    {
        var owner = api.CreateClientWithUser("discord|pm-visible-owner");
        var viewer = api.CreateClientWithUser("discord|pm-visible-viewer");

        await owner.PostAsJsonAsync(
            "/api/v1/me/payment-methods/",
            new UpsertPaymentMethodRequest("Wise", "wise", "https://wise.com/pay/me", null)
        );
        var ownerId = (
            await (await owner.GetAsync("/api/v1/me/")).Content.ReadFromJsonAsync<UserResponse>()
        )!.Id;

        var response = await viewer.GetAsync($"/api/v1/users/{ownerId}/payment-methods");
        response.EnsureSuccessStatusCode();
        var methods = await response.Content.ReadFromJsonAsync<List<PaymentMethodResponse>>();
        Assert.Single(methods!);
        Assert.Equal("Wise", methods![0].Title);
    }

    [Fact]
    public async Task GetForUser_Returns404_WhenUserDoesNotExist()
    {
        var client = api.CreateClientWithUser("discord|pm-visible-viewer-2");
        var response = await client.GetAsync("/api/v1/users/999999/payment-methods");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
