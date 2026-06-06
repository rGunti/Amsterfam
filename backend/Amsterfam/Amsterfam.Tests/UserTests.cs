using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Tests;

public class UserTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task CanCreateAndRetrieveUser()
    {
        await using var context = db.CreateDbContext();

        var user = new User
        {
            ExternalId = "discord|123456",
            DisplayName = "Test User",
            Email = "test@example.com",
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        await using var readContext = db.CreateDbContext();
        var saved = await readContext.Users.SingleAsync(u => u.ExternalId == "discord|123456");

        Assert.Equal("Test User", saved.DisplayName);
        Assert.Equal("test@example.com", saved.Email);
        Assert.True(saved.CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task ExternalIdMustBeUnique()
    {
        await using var context = db.CreateDbContext();

        context.Users.AddRange(
            new User { ExternalId = "discord|dupe", DisplayName = "A", Email = "a@example.com" },
            new User { ExternalId = "discord|dupe", DisplayName = "B", Email = "b@example.com" }
        );

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
