using Amsterfam.Api.Services;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace Amsterfam.Tests.Services;

public class EmailSenderTests
{
    [Fact]
    public void RecipientRefusedWith5xx_IsPermanent()
    {
        var error = MailKitEmailSender.Translate(
            new SmtpCommandException(
                SmtpErrorCode.RecipientNotAccepted,
                SmtpStatusCode.MailboxUnavailable,
                "550 5.1.1 <someone@example.com>: no such user"
            )
        );

        Assert.NotNull(error);
        Assert.True(error.Permanent);
        Assert.Equal("The mail server refused the message (550).", error.Message);
    }

    [Fact]
    public void RecipientRefusedWith4xx_IsRetried()
    {
        var error = MailKitEmailSender.Translate(
            new SmtpCommandException(
                SmtpErrorCode.RecipientNotAccepted,
                SmtpStatusCode.MailboxBusy,
                "450 try later"
            )
        );

        Assert.False(error!.Permanent);
    }

    [Theory]
    [InlineData(SmtpErrorCode.SenderNotAccepted)]
    [InlineData(SmtpErrorCode.MessageNotAccepted)]
    [InlineData(SmtpErrorCode.UnexpectedStatusCode)]
    public void Other5xx_AreRetriedBecauseTheyAreAboutOurSetup(SmtpErrorCode code)
    {
        var error = MailKitEmailSender.Translate(
            new SmtpCommandException(code, SmtpStatusCode.MailboxUnavailable, "550 relay denied")
        );

        Assert.False(error!.Permanent);
    }

    [Fact]
    public void Failures_UseOurOwnMessages()
    {
        Assert.Equal(
            "The mail server rejected the login.",
            MailKitEmailSender
                .Translate(new AuthenticationException("535 bad password hunter2"))!
                .Message
        );
        Assert.Equal(
            "The mail server requires a login.",
            MailKitEmailSender.Translate(new ServiceNotAuthenticatedException("530 auth"))!.Message
        );
        Assert.Equal(
            "The mail server couldn't be reached (ServiceNotConnectedException).",
            MailKitEmailSender.Translate(new ServiceNotConnectedException("gone"))!.Message
        );
    }

    [Fact]
    public void OtherExceptions_AreNotTranslated()
    {
        Assert.Null(MailKitEmailSender.Translate(new InvalidOperationException("bug")));
    }
}
