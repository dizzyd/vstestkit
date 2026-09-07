using VsTestkit.Testing;

var fixture = Path.GetFullPath(args[0]);
using var context = new GameContext();
Vs.PrimaryCtx = context;

TaskCompletionSource Gate(string field) =>
    (TaskCompletionSource)TestRunner.Loaded.First(t => t.ClassName == "Lifetime")
        .DeclaringType.GetField(field).GetValue(null);

async Task WaitForIdle()
{
    // Releasing the fixture gate schedules a continuation. Do not assume the
    // game thread has consumed it when SetResult returns.
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (true)
    {
        try { TestRunner.Load(fixture); return; }
        catch (InvalidOperationException) when (DateTime.UtcNow < deadline) { await Task.Delay(1); }
    }
}

TestRunner.Load(fixture);
var held = Task.Run(() => TestRunner.Run("Lifetime.Held", true));
await Gate("Entered").Task.WaitAsync(TimeSpan.FromSeconds(5));
Assert.Throws<InvalidOperationException>(() => TestRunner.Run("Lifetime.Next", true));
Assert.Throws<InvalidOperationException>(() => TestRunner.Load(fixture));
Gate("Release").SetResult();
var result = await held;
Assert.Equal(1, result.passed);
Assert.Equal("held test finished", result.results.Single().output.Single());
Console.WriteLine("PASS: concurrent runs and reloads are rejected without stealing output");

TestRunner.Load(fixture);
held = Task.Run(() => TestRunner.Run("Lifetime.Held", true));
await Gate("Entered").Task.WaitAsync(TimeSpan.FromSeconds(5));
result = await held.WaitAsync(TimeSpan.FromSeconds(40));
Assert.Equal(1, result.timedOut);
Assert.Throws<InvalidOperationException>(() => TestRunner.Run("Lifetime.Next", true));
Assert.Throws<InvalidOperationException>(() => TestRunner.Load(fixture));
Gate("Release").SetResult();
await WaitForIdle();
Assert.Equal("timedout", result.results.Single().status);
Assert.Equal(0, result.results.Single().output.Count);
var next = TestRunner.Run("Lifetime.Next", true);
Assert.Equal("next test finished", next.results.Single().output.Single());
Console.WriteLine("PASS: timeout retains ownership until teardown completes; its report stays immutable");
