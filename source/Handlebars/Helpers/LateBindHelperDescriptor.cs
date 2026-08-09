using System;
using System.Runtime.CompilerServices;
using HandlebarsDotNet.Collections;
using HandlebarsDotNet.PathStructure;

namespace HandlebarsDotNet.Helpers
{
    public sealed class LateBindHelperDescriptor : IHelperDescriptor<HelperOptions>
    {
        // ObservableList.Count takes a ReaderWriterLockSlim per call, and this descriptor runs
        // for every simple {{name}} on every render — so the "are there helper resolvers" check
        // is cached in a flag kept up to date by subscribing to the (append-only) resolver list.
        private ObservableList<IHelperResolver>? _observedResolvers;
        private IObserver<IObservableEvent<IHelperResolver>>? _observerRoot; // strong root: ObservableList holds observers weakly
        private volatile bool _hasResolvers;

        public LateBindHelperDescriptor(string name) => Name = name;

        public PathInfo Name { get; }

        public object? Invoke(in HelperOptions options, in Context context, in Arguments arguments)
        {
            var bindingContext = options.Frame;

            // Frame-local helpers only exist once something wrote to a frame's helper registry
            // (decorators / in-render registration); skip the cascade walk in the common case.
            if(bindingContext.HasFrameHelpers && bindingContext.Helpers.TryGetValue(Name, out var contextHelper))
            {
                return contextHelper.Invoke(options, context, arguments);
            }

            var configuration = options.Frame.Configuration;
            var helperResolvers = (ObservableList<IHelperResolver>) configuration.HelperResolvers;
            if (!ReferenceEquals(_observedResolvers, helperResolvers)) ObserveResolvers(helperResolvers);
            if (_hasResolvers)
            {
                var targetType = arguments.Length > 0 ? arguments[0]!.GetType() : null;
                for (var index = 0; index < helperResolvers.Count; index++)
                {
                    var resolver = helperResolvers[index];
                    if (!resolver.TryResolveHelper(Name, targetType, out var helper)) continue;

                    return helper.Invoke(options, context, arguments);
                }
            }

            var value = PathResolver.ResolvePath(bindingContext, Name);
            if (!(value is UndefinedBindingResult)) return value;

            return configuration.Helpers["helperMissing"]!.Value.Invoke(options, context, arguments);
        }

        public void Invoke(in EncodedTextWriter output, in HelperOptions options, in Context context, in Arguments arguments)
        {
            output.Write(Invoke(options, context, arguments));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ObserveResolvers(ObservableList<IHelperResolver> resolvers)
        {
            // Subscribe before snapshotting Count so a concurrent Add can never be missed;
            // duplicate subscriptions from a racing first call are benign (same flag).
            var observer = ObserverBuilder<IObservableEvent<IHelperResolver>>.Create(this)
                .OnEvent<AddedObservableEvent<IHelperResolver>>((_, state) => state._hasResolvers = true)
                .Build();
            resolvers.Subscribe(observer);
            _observerRoot = observer;
            if (resolvers.Count != 0) _hasResolvers = true;
            _observedResolvers = resolvers;
        }
    }
}
