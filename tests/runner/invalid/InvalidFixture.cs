using VsTestkit.Testing;

public class InvalidFixture
{
#if BAD_BEFORE
    [BeforeEach]
#elif BAD_AFTER
    [AfterEach]
#else
    [VsTest]
#endif
#if BAD_VALUE_TASK
    public async ValueTask Bad() { await Task.Yield(); }
#else
    public async void Bad() { await Task.Yield(); Assert.Fail("must never run"); }
#endif

    [VsTest] public void Valid() { }
}
