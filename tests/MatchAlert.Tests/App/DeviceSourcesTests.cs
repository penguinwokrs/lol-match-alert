// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Domain;

namespace MatchAlert.Tests.App;

public class DeviceSourcesTests
{
    private sealed class Broken : IDeviceSource
    {
        public IReadOnlyList<ILightingDevice> Discover() => throw new IOException("SetupAPI said no");
        public void RecoverInterruptedSessions() => throw new IOException("SetupAPI said no");
    }

    [Fact]
    public void Lists_every_source_and_skips_one_that_fails()
    {
        var a = new FakeDevice("a");
        var b = new FakeDevice("b");
        var log = new List<string>();
        var sources = new DeviceSources([new FakeDeviceSource(a), new Broken(), new FakeDeviceSource(b)], log.Add);

        Assert.Equal(["a", "b"], sources.Discover().Select(d => d.Id));
        Assert.Contains(log, l => l.Contains("SetupAPI said no"));
    }

    [Fact]
    public void Recovers_every_source_even_after_one_fails()
    {
        var first = new FakeDeviceSource();
        var last = new FakeDeviceSource();
        new DeviceSources([first, new Broken(), last], _ => { }).RecoverInterruptedSessions();
        Assert.Equal((1, 1), (first.Recovered, last.Recovered));
    }
}
