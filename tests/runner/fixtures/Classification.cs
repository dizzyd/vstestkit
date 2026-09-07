using VsTestkit.Testing;

public class Classification
{
    [VsTest] public void DirectAssertion() => Assert.Fail("assertion");
    [VsTest] public void DirectSkip() => Vs.Skip("skip");
    [VsTest] public void Error() => throw new InvalidOperationException("error");

    [VsTest]
    public async Task WrappedAssertion()
    {
        await Task.Yield();
        throw new AggregateException(new AssertionException("assertion"));
    }

    [VsTest]
    public async Task WrappedSkip()
    {
        await Task.Yield();
        throw new AggregateException(new SkipException("skip"));
    }
}
