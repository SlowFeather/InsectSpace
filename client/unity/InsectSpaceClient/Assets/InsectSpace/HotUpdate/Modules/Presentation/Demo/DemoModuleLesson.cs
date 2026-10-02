using System;
using System.Collections.Generic;
using InsectSpace.Foundation;

namespace InsectSpace.Gameplay.Demo
{
    public static class DemoModuleLesson
    {
        private sealed class LessonModule : IGameModule
        {
            private readonly Action<string> log;
            private readonly bool fail;
            public string Id { get; }
            public IReadOnlyList<string> Dependencies { get; }
            public LessonModule(string id, string[] dependencies, Action<string> log, bool fail = false)
            { Id = id; Dependencies = dependencies; this.log = log; this.fail = fail; }
            public void Start(ServiceRegistry services)
            { log("Start " + Id); if (fail) throw new InvalidOperationException("LOCAL fixture start failure"); }
            public void Tick(float elapsed) { log("Tick " + Id); }
            public void Stop() { log("Stop " + Id); }
        }

        public static string[] Run(bool fail)
        {
            var trace = new List<string>();
            var registry = new ServiceRegistry();
            using (var host = new ModuleHost())
            {
                try
                {
                    host.Start(new IGameModule[] {
                        new LessonModule("view", new[] { "data" }, trace.Add, fail),
                        new LessonModule("data", new[] { "config" }, trace.Add),
                        new LessonModule("config", Array.Empty<string>(), trace.Add)
                    }, registry);
                    host.Tick(0);
                }
                catch (InvalidOperationException) when (fail) { trace.Add("Caught expected fixture failure"); }
                try { registry.Add(new object()); }
                catch (InvalidOperationException) { trace.Add("ServiceRegistry frozen: replacement rejected"); }
            }
            return trace.ToArray();
        }
    }
}
