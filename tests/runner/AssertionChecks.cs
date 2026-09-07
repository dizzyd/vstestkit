using VsTestkit.Testing;

public static class AssertionChecks
{
    public static void Run()
    {
        Different(9007199254740992L, 9007199254740993L);
        Different(1.0000000000000000000000000001m, 1.0000000000000000000000000002m);
        Different(9007199254740993L, 9007199254740992d);
        Different(ulong.MaxValue, (double)ulong.MaxValue);
        Different(0.1m, 0.1d);
        Different(0m, double.Epsilon);
        Different(0m, double.NaN);
        Different(decimal.MaxValue, double.PositiveInfinity);

        Assert.Equal(1, 1f);
        Assert.Equal(9007199254740992L, 9007199254740992d);
        Assert.Equal(ulong.MaxValue, (decimal)ulong.MaxValue);
        Assert.Equal(1.5m, 1.5d);
        Assert.Equal(-1.5d, -1.5m);
        Assert.Equal(long.MinValue, (double)long.MinValue);
        Assert.Equal(0m, -0d);
        Assert.Equal(9223372036854775808m, 9223372036854775808d);
        Console.WriteLine("PASS: exact numeric equality preserves integer, decimal and binary boundaries");
    }

    static void Different(object a, object b)
    {
        Assert.Throws<AssertionException>(() => Assert.Equal(a, b));
        Assert.Throws<AssertionException>(() => Assert.Equal(b, a));
        Assert.NotEqual(a, b);
    }
}
