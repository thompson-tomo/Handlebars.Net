using System;
using System.Runtime.CompilerServices;
using HandlebarsDotNet.Collections;
using HandlebarsDotNet.PathStructure;

namespace HandlebarsDotNet.Helpers.BlockHelpers
{
    public sealed class LateBindBlockHelperDescriptor : IHelperDescriptor<BlockHelperOptions>
    {
        // See LateBindHelperDescriptor: ObservableList.Count locks per call, so the
        // "are there helper resolvers" check is cached behind an observer-maintained flag.
        private ObservableList<IHelperResolver>? _observedResolvers;
        private IObserver<IObservableEvent<IHelperResolver>>? _observerRoot; // strong root: ObservableList holds observers weakly
        private volatile bool _hasResolvers;

        public LateBindBlockHelperDescriptor(string name) => Name = name;

        public PathInfo Name { get; }

        public object Invoke(in BlockHelperOptions options, in Context context, in Arguments arguments)
        {
            return this.ReturnInvoke(options, context, arguments);
        }

        public void Invoke(in EncodedTextWriter output, in BlockHelperOptions options, in Context context, in Arguments arguments)
        {
            // Frame-local helpers only exist once something wrote to a frame's helper registry
            // (decorators / in-render registration); skip the cascade walk in the common case.
            if(options.Frame.HasFrameHelpers && options.Frame.BlockHelpers.TryGetValue(Name, out var contextHelper))
            {
                contextHelper.Invoke(options, context, arguments);
                return;
            }

            var configuration = options.Frame.Configuration;
            var helperResolvers = (ObservableList<IHelperResolver>) configuration.HelperResolvers;
            if (!ReferenceEquals(_observedResolvers, helperResolvers)) ObserveResolvers(helperResolvers);
            if (_hasResolvers)
            {
                for (var index = 0; index < helperResolvers.Count; index++)
                {
                    if (!helperResolvers[index].TryResolveBlockHelper(Name, out var descriptor)) continue;

                    descriptor.Invoke(output, options, context, arguments);
                    return;
                }
            }

            configuration.BlockHelpers["blockHelperMissing"]!.Value
                .Invoke(output, options, context, arguments);
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
