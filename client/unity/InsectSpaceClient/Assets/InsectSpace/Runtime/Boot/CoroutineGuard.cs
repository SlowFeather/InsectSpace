using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace InsectSpace.Client
{
    internal static class CoroutineGuard
    {
        public static IEnumerator Run(IEnumerator root, Action<Exception> failed)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            try
            {
                while (stack.Count > 0)
                {
                    var current = stack.Peek();
                    bool moved = false;
                    object value = null;
                    Exception error = null;
                    try
                    {
                        moved = current.MoveNext();
                        if (moved) value = current.Current;
                    }
                    catch (Exception exception) { error = exception; }
                    if (error != null) { failed(error); yield break; }
                    if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (value is IEnumerator nested && !(value is AsyncOperationBase) &&
                        !(value is HandleBase) && !(value is CustomYieldInstruction))
                        stack.Push(nested);
                    else
                        yield return value;
                }
            }
            finally
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            }
        }
    }
}
