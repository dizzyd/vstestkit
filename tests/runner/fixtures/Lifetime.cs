using VsTestkit.Testing;

public class Lifetime
{
    public static TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [VsTest(TimeoutMs = 500)]
    public async Task Held()
    {
        Entered.SetResult();
        await Release.Task;
        TestOutput.Write("held test finished");
    }

    [VsTest]
    public void Next() => TestOutput.Write("next test finished");
}
