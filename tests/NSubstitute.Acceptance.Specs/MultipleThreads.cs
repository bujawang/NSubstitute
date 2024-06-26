using NSubstitute.Acceptance.Specs.Infrastructure;
using NSubstitute.Exceptions;
using NUnit.Framework;

namespace NSubstitute.Acceptance.Specs;

public class MultipleThreads
{
    [Test]
    public void Configure_substitutes_on_different_threads()
    {
        var firstSub = Substitute.For<IFoo>();
        var secondSub = Substitute.For<IFoo>();

        using (var interrupted = new AutoResetEvent(false))
        {
            var initialThread = new Thread(() =>
                                               {
                                                   firstSub.Number();
                                                   if (!interrupted.WaitOne(TimeSpan.FromSeconds(1))) Assert.Fail("Timed out waiting for interrupt");
                                                   1.Returns(1);
                                               });
            var interruptingThread = new Thread(() =>
                                                    {
                                                        secondSub.Number().Returns(2);
                                                        interrupted.Set();
                                                    });

            initialThread.Start();
            interruptingThread.Start();

            initialThread.Join();
            interruptingThread.Join();
        }

        Assert.That(firstSub.Number(), Is.EqualTo(1));
        Assert.That(secondSub.Number(), Is.EqualTo(2));
    }

    [Test]
    [Ignore("Long running, non-deterministic test. Reproduced prob with using CallResults from multiple threads.")]
    public void Call_substitute_method_that_needs_to_return_a_value_from_different_threads()
    {
        for (var i = 0; i < 1000; i++)
        {
            var sub = Substitute.For<IFoo>();
            var tasks = Enumerable.Range(0, 20).Select(x => new BackgroundTask(() => sub.Bar())).ToArray();
            BackgroundTask.StartAll(tasks);
            BackgroundTask.AwaitAll(tasks);
        }
    }

    [Test]
    [Ignore("Long running, non-deterministic test. Reproduced prob with using CallStack from multiple threads.")]
    public void Call_substitute_method_that_does_not_return_and_just_needs_to_be_recorded_from_different_threads()
    {
        for (var i = 0; i < 100000; i++)
        {
            var sub = Substitute.For<IFoo>();
            var tasks = Enumerable.Range(0, 20).Select(x => new BackgroundTask(() => sub.VoidMethod())).ToArray();
            BackgroundTask.StartAll(tasks);
            BackgroundTask.AwaitAll(tasks);
        }
    }

    [Test]
    [Ignore("Long running, non-deterministic test. Reproduced prob with exception message reading 'Expected x calls, actually received x matching calls'.")]
    public void Issue_64_check_received_while_calling_from_other_threads()
    {
        const int expected = 8;
        for (var i = 0; i < 1000; i++)
        {
            var foo = Substitute.For<IFoo>();
            var checkThread = new BackgroundTask(() => foo.Received(expected).VoidMethod());
            var callThreads = Enumerable.Range(1, expected).Select(x => new BackgroundTask(() => { Thread.Sleep(0); foo.VoidMethod(); }));
            var tasks = callThreads.Concat(new[] { checkThread }).ToArray();
            BackgroundTask.StartAll(tasks);
            try { BackgroundTask.AwaitAll(tasks); }
            catch (Exception ex)
            {
                if (ex.InnerException == null) throw;
                if (!(ex.InnerException is ReceivedCallsException)) throw;

                var receivedCallsEx = (ReceivedCallsException)ex.InnerException;
                Assert.That(receivedCallsEx.Message, Does.Not.Contain("Actually received " + expected + " matching calls"),
                    "Should not throw received calls exception if it actually received the same number of calls as expected. " +
                    "If we get that it means there was a race between checking the expected calls and accessing the calls to put in the exception message.");
            }
        }
    }

    [Test]
    [Ignore("Long running, non-deterministic test.")]
    // TODO no Timeout in NUnit Core
    //[Timeout(60 * 1000)]
    public void Create_Delegate_Substitute_From_Many_Threads()
    {
        var tasks =
            Enumerable.Range(0, 20).Select(_ =>
                new BackgroundTask(() =>
                    {
                        for (var i = 0; i < 1000; ++i)
                        {
                            Substitute.For<Func<string>>();
                        }
                    })).ToArray();

        BackgroundTask.StartAll(tasks);
        BackgroundTask.AwaitAll(tasks);
    }

    [Test]
    public void Returns_multiple_values_is_threadsafe()
    {
        const int parallelism = 10;
        var sut = Substitute.For<IFoo>();
        var expected = Enumerable.Range(0, parallelism).ToArray();
        sut.Number().Returns(expected[0], expected.Skip(1).ToArray());

        var tasks = Enumerable.Range(0, parallelism)
            .Select(_ => new Task<int>(() => sut.Number()))
            .ToArray();

        foreach (var task in tasks) { task.Start(); }
        var actual = System.Threading.Tasks.Task.WhenAll(tasks).Result;
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    public interface IFoo
    {
        int Number();
        IBar Bar();
        void VoidMethod();
    }

    public interface IBar { }
}
