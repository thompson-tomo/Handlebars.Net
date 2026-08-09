using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using HandlebarsDotNet.Collections;
using HandlebarsDotNet.EqualityComparers;
using HandlebarsDotNet.Runtime;

namespace HandlebarsDotNet.ObjectDescriptors
{
    public class ObjectDescriptorFactory : IObjectDescriptorProvider, IObserver<IObservableEvent<IObjectDescriptorProvider>>
    {
        private readonly ObservableList<IObjectDescriptorProvider> _providers;
        private readonly LookupSlim<Type, DeferredValue<Type, ObjectDescriptor>, ReferenceEqualityComparer<Type>> _descriptorsCache = new LookupSlim<Type, DeferredValue<Type, ObjectDescriptor>, ReferenceEqualityComparer<Type>>(new ReferenceEqualityComparer<Type>());

        private static readonly Func<Type, ObservableList<IObjectDescriptorProvider>, DeferredValue<Type, ObjectDescriptor>> ValueFactory = (key, providers) => new DeferredValue<Type, ObjectDescriptor>(key, t =>
        {
            for (var index = providers.Count - 1; index >= 0; index--)
            {
                if (!providers[index].TryGetDescriptor(t, out var descriptor)) continue;

                return descriptor;
            }

            return ObjectDescriptor.Empty!;
        });

        private readonly IObserver<IObservableEvent<IObjectDescriptorProvider>> _observer;

        private int _version;

        public static ObjectDescriptorFactory? Current => AmbientContext.Current?.ObjectDescriptorFactory;

        /// <summary>
        /// Monotonic stamp bumped whenever the provider set changes; lets external
        /// per-call-site descriptor caches (see <see cref="PathStructure.ChainSegment"/>)
        /// detect that previously resolved descriptors may be stale.
        /// </summary>
        internal int Version => Volatile.Read(ref _version);

        public ObjectDescriptorFactory(ObservableList<IObjectDescriptorProvider>? providers = null)
        {
            _providers = new ObservableList<IObjectDescriptorProvider>();

            if (providers != null) Append(providers);

            _observer = ObserverBuilder<IObservableEvent<IObjectDescriptorProvider>>.Create(this)
                .OnEvent<AddedObservableEvent<IObjectDescriptorProvider>>((@event, state) =>
                {
                    state._descriptorsCache.Clear();
                    Interlocked.Increment(ref state._version);
                })
                .Build();

            _providers.Subscribe(this);
        }

        public ObjectDescriptorFactory Append(ObservableList<IObjectDescriptorProvider> providers)
        {
            _providers.AddMany(providers);
            providers.Subscribe(_providers);

            return this;
        }
        
        public ObjectDescriptorFactory Append(ObjectDescriptorFactory factory)
        {
            _providers.AddMany(factory._providers);
            factory._providers.Subscribe(_providers);
            
            return this;
        }
        
        public bool TryGetDescriptor(Type type, [NotNullWhen(true)] out ObjectDescriptor? value)
        {
            value = _descriptorsCache.GetOrAdd(type, ValueFactory, _providers).Value;
            return !ReferenceEquals(value, ObjectDescriptor.Empty);
        }

        public void OnCompleted() => _observer.OnCompleted();

        public void OnError(Exception error) => _observer.OnError(error);

        public void OnNext(IObservableEvent<IObjectDescriptorProvider> value) => _observer.OnNext(value);
    }
}