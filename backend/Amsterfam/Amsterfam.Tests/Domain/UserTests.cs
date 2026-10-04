using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Tests.Domain;

public class UserTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task CanCreateAndRetrieveUser()
    {
        await using var context = db.CreateDbContext();

        var user = new User
        {
            ExternalId = "discord|123456",
            Handle = "Test User",
            Email = "test@example.com",
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        await using var readContext = db.CreateDbContext();
        var saved = await readContext.Users.SingleAsync(u => u.ExternalId == "discord|123456");

        Assert.Equal("Test User", saved.Handle);
        Assert.Equal("test@example.com", saved.Email);
        Assert.True(saved.CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task ExternalIdMustBeUnique()
    {
        await using var context = db.CreateDbContext();

        context.Users.AddRange(
            new User
            {
                ExternalId = "discord|dupe",
                Handle = "A",
                Email = "a@example.com",
            },
            new User
            {
                ExternalId = "discord|dupe",
                Handle = "B",
                Email = "b@example.com",
            }
        );

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task HandleMustBeUniquePerAuthSource()
    {
        await using var context = db.CreateDbContext();

        context.Users.AddRange(
            new User
            {
                ExternalId = "discord|handle-dupe-1",
                Handle = "klaus",
                AuthSource = "discord",
                Email = "k1@example.com",
            },
            new User
            {
                ExternalId = "discord|handle-dupe-2",
                Handle = "klaus",
                AuthSource = "discord",
                Email = "k2@example.com",
            }
        );

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task HandleMustBeUnique_EvenWhileAuthSourceIsUnknown()
    {
        await using var context = db.CreateDbContext();

        context.Users.AddRange(
            new User
            {
                ExternalId = "legacy|1",
                Handle = "legacy",
                Email = "l1@example.com",
            },
            new User
            {
                ExternalId = "legacy|2",
                Handle = "legacy",
                Email = "l2@example.com",
            }
        );

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task SameHandle_IsAllowed_FromDifferentAuthSources()
    {
        await using var context = db.CreateDbContext();

        context.Users.AddRange(
            new User
            {
                ExternalId = "discord|greta",
                Handle = "greta",
                AuthSource = "discord",
                Email = "g1@example.com",
            },
            new User
            {
                ExternalId = "internal|greta",
                Handle = "greta",
                AuthSource = "internal",
                Email = "g2@example.com",
            }
        );

        await context.SaveChangesAsync();
    }
}
