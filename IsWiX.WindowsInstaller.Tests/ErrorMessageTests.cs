using Xunit;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>An error raised for an underlying failure, such as a file that could not be written, keeps that failure's message.</summary>
public class ErrorMessageTests
{
    private static readonly IOException Cause = new("The process cannot access the file because it is being used by another process.");

    [Fact]
    public void MessageIncludesTheUnderlyingFailure()
    {
        var error = new MsiErrorException((uint) MsiError.FUNCTION_FAILED, Cause);

        Exception exception = InstallerException.ExceptionFromReturnCode(error);

        Assert.Contains(Cause.Message, exception.Message);
    }

    [Fact]
    public void MessageKeepsTheCallersContextAndTheUnderlyingFailure()
    {
        var error = new MsiErrorException((uint) MsiError.FUNCTION_FAILED, Cause);

        Exception exception = InstallerException.ExceptionFromReturnCode(error, "Database=\"a.msi\"");

        Assert.Contains("Database=\"a.msi\"", exception.Message);
        Assert.Contains(Cause.Message, exception.Message);
    }

    [Fact]
    public void ErrorWithoutAnUnderlyingFailureIsUnchanged()
    {
        Exception plain = InstallerException.ExceptionFromReturnCode((uint) MsiError.FUNCTION_FAILED, null);

        Exception exception = InstallerException.ExceptionFromReturnCode(new MsiErrorException((uint) MsiError.FUNCTION_FAILED));

        Assert.Equal(plain.GetType(), exception.GetType());
        Assert.Equal(plain.Message, exception.Message);
    }
}
