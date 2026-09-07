using VsTestkit.Testing;

public class Teardown
{
    [VsTest] public void Passing() { }
    [VsTest] public void AlreadyFailed() => Assert.Fail("body failure");
    [VsTest] public void Skipped() => Vs.Skip("not applicable");
    [AfterEach] public void Cleanup() => Assert.Fail("cleanup failure");
}

public class ErrorTeardown
{
    [VsTest] public void Passing() { }
    [AfterEach] public void Cleanup() => throw new InvalidOperationException("cleanup failure");
}
