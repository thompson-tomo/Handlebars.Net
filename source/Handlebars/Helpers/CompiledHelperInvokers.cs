using System.Runtime.CompilerServices;
using HandlebarsDotNet.PathStructure;
using HandlebarsDotNet.Runtime;

namespace HandlebarsDotNet.Helpers
{
    /// <summary>
    /// Static entry points emitted by the compiler for helper-literal statements
    /// ({{name}} with no arguments). NoInlining keeps template JIT fast: dynamic methods
    /// are compiled at CreateDelegate, and expanding the options/context/arguments
    /// construction plus dispatch into every call site multiplied template JIT cost.
    /// These methods are JIT-compiled once per process; templates emit one thin call.
    /// </summary>
    internal static class CompiledHelperInvokers
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void WriteInvoke(
            in EncodedTextWriter writer,
            Ref<IHelperDescriptor<HelperOptions>> helper,
            PathInfo pathInfo,
            BindingContext bindingContext)
        {
            helper.Value.Invoke(writer, new HelperOptions(pathInfo, bindingContext), new Context(bindingContext), new Arguments(0));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object? Invoke(
            Ref<IHelperDescriptor<HelperOptions>> helper,
            PathInfo pathInfo,
            BindingContext bindingContext)
        {
            return helper.Value.Invoke(new HelperOptions(pathInfo, bindingContext), new Context(bindingContext), new Arguments(0));
        }
    }
}
