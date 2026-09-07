using VsTestkit.Testing;

public class Timing
{
    public static TaskCompletionSource Preparing = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static TaskCompletionSource Prepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static TaskCompletionSource Cleaning = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static TaskCompletionSource Cleaned = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [BeforeEach]
    public async Task Setup() { Preparing.SetResult(); await Prepared.Task; }

    [VsTest(TimeoutMs = 10)]
    public void Body() { }

    [AfterEach]
    public async Task Cleanup() { Cleaning.SetResult(); await Cleaned.Task; }
}
