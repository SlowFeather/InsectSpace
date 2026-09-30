using System;
using System.Collections.Generic;
using System.Linq;

namespace InsectSpace.Foundation
{
    public interface IGameModule
    {
        string Id { get; }
        IReadOnlyList<string> Dependencies { get; }
        void Start(ServiceRegistry services);
        void Tick(float elapsedSeconds);
        void Stop();
    }

    public sealed class ServiceRegistry
    {
        private readonly Dictionary<Type, object> services = new Dictionary<Type, object>();
        private bool frozen;

        public void Add<T>(T service) where T : class
        {
            if (frozen) throw new InvalidOperationException("Service registration is frozen.");
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (services.ContainsKey(typeof(T))) throw new InvalidOperationException("Duplicate service: " + typeof(T));
            services.Add(typeof(T), service);
        }

        public T Get<T>() where T : class
        {
            if (!services.TryGetValue(typeof(T), out object service))
                throw new InvalidOperationException("Missing service: " + typeof(T));
            return (T)service;
        }

        public void Freeze() { frozen = true; }
    }

    // Registration is explicit so stripping/IL2CPP cannot remove reflection-only modules.
    public sealed class ModuleHost : IDisposable
    {
        private readonly List<IGameModule> active = new List<IGameModule>();
        private bool started;
        private bool disposed;
        public IReadOnlyList<string> ActiveModuleIds => active.Select(m => m.Id).ToArray();

        public void Start(IEnumerable<IGameModule> modules, ServiceRegistry services)
        {
            if (started || disposed) throw new InvalidOperationException("A module host is single-use.");
            if (services == null) throw new ArgumentNullException(nameof(services));
            var ordered = Sort(modules);
            services.Freeze();
            started = true;
            try
            {
                foreach (var module in ordered)
                {
                    // Register first so partial Start failures also receive Stop.
                    active.Add(module);
                    module.Start(services);
                }
            }
            catch (Exception startError)
            {
                try { Dispose(); }
                catch (Exception stopError) { throw new AggregateException(startError, stopError); }
                throw;
            }
        }

        public void Tick(float elapsedSeconds)
        {
            if (!started || disposed) throw new InvalidOperationException("Modules are not running.");
            if (float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds) || elapsedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            foreach (var module in active) module.Tick(elapsedSeconds);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var errors = new List<Exception>();
            for (int i = active.Count - 1; i >= 0; i--)
                try { active[i].Stop(); } catch (Exception error) { errors.Add(error); }
            active.Clear();
            if (errors.Count > 0) throw new AggregateException("Module shutdown failed.", errors);
        }

        private static List<IGameModule> Sort(IEnumerable<IGameModule> modules)
        {
            if (modules == null) throw new ArgumentNullException(nameof(modules));
            var lookup = new Dictionary<string, IGameModule>(StringComparer.Ordinal);
            foreach (var module in modules)
            {
                if (module == null || string.IsNullOrWhiteSpace(module.Id))
                    throw new ArgumentException("Modules need unique, nonempty IDs.");
                if (lookup.ContainsKey(module.Id)) throw new ArgumentException("Duplicate module: " + module.Id);
                lookup.Add(module.Id, module);
            }
            var ordered = new List<IGameModule>();
            var visited = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var id in lookup.Keys.OrderBy(id => id, StringComparer.Ordinal))
                Visit(id, lookup, visited, ordered);
            return ordered;
        }

        private static void Visit(string id, Dictionary<string, IGameModule> lookup,
            Dictionary<string, bool> visited, List<IGameModule> ordered)
        {
            if (!lookup.TryGetValue(id, out var module)) throw new ArgumentException("Missing module dependency: " + id);
            if (visited.TryGetValue(id, out bool done))
            {
                if (!done) throw new ArgumentException("Module dependency cycle at: " + id);
                return;
            }
            visited.Add(id, false);
            foreach (var dependency in module.Dependencies) Visit(dependency, lookup, visited, ordered);
            visited[id] = true;
            ordered.Add(module);
        }
    }
}
